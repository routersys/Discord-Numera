using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record DormancyMaintenanceReport(int Assessed, int Closed, int Transitioned);

public sealed class DormancyMaintenanceService
{
    public const int BatchSize = 100;
    public const int MaximumCatchUpDues = 8;
    public const int InactivityMonths = 2;
    public const long DormancyIntervalMilliseconds = 7L * 24 * 60 * 60 * 1000;
    public const string OperationType = "DORMANCY_FEE";
    public const string TransactionType = "DORMANCY_FEE";
    public const string DescriptionCode = "DORMANCY_FEE";

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public DormancyMaintenanceService(
        IBankingWriteGateway writeGateway,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public async Task<DormancyMaintenanceReport> ProcessDueAsync(CancellationToken cancellationToken)
    {
        int transitioned = await TransitionInactiveAsync(cancellationToken).ConfigureAwait(false);

        Result<IReadOnlyList<DepositAccountId>> due = await writeGateway
            .ExecuteAsync(ListDue, cancellationToken)
            .ConfigureAwait(false);

        if (!due.IsSuccess)
        {
            return new DormancyMaintenanceReport(0, 0, transitioned);
        }

        int assessed = 0;
        int closed = 0;

        foreach (DepositAccountId accountId in due.Value)
        {
            for (int attempt = 0; attempt < MaximumCatchUpDues; attempt++)
            {
                Result<DormancyOutcome> outcome = await writeGateway
                    .ExecuteAsync(unitOfWork => PostNextDue(unitOfWork, accountId), cancellationToken)
                    .ConfigureAwait(false);

                if (!outcome.IsSuccess || outcome.Value == DormancyOutcome.NotDue)
                {
                    break;
                }

                if (outcome.Value == DormancyOutcome.Assessed)
                {
                    assessed++;
                    continue;
                }

                closed++;
                break;
            }
        }

        return new DormancyMaintenanceReport(assessed, closed, transitioned);
    }

    private async Task<int> TransitionInactiveAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DepositAccountId>> candidates = await writeGateway
            .ExecuteAsync(ListInactive, cancellationToken)
            .ConfigureAwait(false);

        if (!candidates.IsSuccess)
        {
            return 0;
        }

        int transitioned = 0;

        foreach (DepositAccountId accountId in candidates.Value)
        {
            Result<bool> outcome = await writeGateway
                .ExecuteAsync(unitOfWork => TransitionInactive(unitOfWork, accountId), cancellationToken)
                .ConfigureAwait(false);

            if (outcome.IsSuccess && outcome.Value)
            {
                transitioned++;
            }
        }

        return transitioned;
    }

    private Result<IReadOnlyList<DepositAccountId>> ListInactive(IBankingUnitOfWork unitOfWork) =>
        Result<IReadOnlyList<DepositAccountId>>.Success(
        [
            .. unitOfWork.DepositAccounts
                .ListDormancyCandidates(Shift(clock.Now(), -InactivityMonths), BatchSize)
                .Select(static account => account.Id),
        ]);

    private Result<bool> TransitionInactive(IBankingUnitOfWork unitOfWork, DepositAccountId accountId)
    {
        UtcTimestamp now = clock.Now();

        if (unitOfWork.DepositAccounts.Find(accountId) is not { } account ||
            account.Status is not (DepositAccountStatus.Active or DepositAccountStatus.Restricted) ||
            Shift(account.LastCustomerActivityAt, InactivityMonths) > now)
        {
            return Result<bool>.Success(false);
        }

        LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(account.LedgerAccountId)
            ?? LedgerBalance.Empty;

        if (balance.PostedBalance.Value == 0 && balance.HeldAmount.IsZero)
        {
            account.RequestClosure(ClosureReason.Dormancy, now);
        }
        else
        {
            account.MarkDormant(now.AddMilliseconds(DormancyIntervalMilliseconds));
        }

        unitOfWork.DepositAccounts.Update(account);

        return Result<bool>.Success(true);
    }

    internal static UtcTimestamp Shift(UtcTimestamp at, int months) =>
        UtcTimestamp.FromUnixMilliseconds(
            DateTimeOffset.FromUnixTimeMilliseconds(at.UnixMilliseconds)
                .AddMonths(months)
                .ToUnixTimeMilliseconds());

    private Result<IReadOnlyList<DepositAccountId>> ListDue(IBankingUnitOfWork unitOfWork) =>
        Result<IReadOnlyList<DepositAccountId>>.Success(
        [
            .. unitOfWork.DepositAccounts.ListDueDormant(clock.Now(), BatchSize)
                .Select(static account => account.Id),
        ]);

    private Result<DormancyOutcome> PostNextDue(
        IBankingUnitOfWork unitOfWork,
        DepositAccountId accountId)
    {
        UtcTimestamp now = clock.Now();

        if (unitOfWork.DepositAccounts.Find(accountId) is not { } account ||
            account.Status != DepositAccountStatus.Dormant ||
            account.NextDormancyFeeAt is not { } due ||
            due > now)
        {
            return Result<DormancyOutcome>.Success(DormancyOutcome.NotDue);
        }

        IdempotencyKey idempotencyKey = IdempotencyKey.Create(
            OperationType,
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{account.Id.Value}:{due.UnixMilliseconds}"));

        if (unitOfWork.BusinessOperations.Find(idempotencyKey) is not null)
        {
            return Result<DormancyOutcome>.Success(DormancyOutcome.NotDue);
        }

        if (unitOfWork.Banks.Find(account.BankId) is not { } bank)
        {
            return Result<DormancyOutcome>.Success(DormancyOutcome.NotDue);
        }

        LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(account.LedgerAccountId)
            ?? LedgerBalance.Empty;

        if (balance.AvailableBalance.Value <= 0)
        {
            return CloseIfSettled(unitOfWork, account, balance, now);
        }

        Result<MoneyMinor> charged = Charge(unitOfWork, bank, account, balance, now);

        if (!charged.IsSuccess)
        {
            return Result<DormancyOutcome>.Failure(charged.Error!);
        }

        if (charged.Value.Value <= 0)
        {
            return CloseIfSettled(unitOfWork, account, balance, now);
        }

        Result posted = Post(unitOfWork, bank, account, charged.Value, idempotencyKey, now);

        if (!posted.IsSuccess)
        {
            return Result<DormancyOutcome>.Failure(posted.Error!);
        }

        account.AdvanceDormancyFeeDue(due.AddMilliseconds(DormancyIntervalMilliseconds));
        unitOfWork.DepositAccounts.Update(account);

        LedgerBalance settled = unitOfWork.LedgerAccounts.FindProjection(account.LedgerAccountId)
            ?? LedgerBalance.Empty;

        return settled.AvailableBalance.Value <= 0 && settled.HeldAmount.IsZero
            ? CloseIfSettled(unitOfWork, account, settled, now)
            : Result<DormancyOutcome>.Success(DormancyOutcome.Assessed);
    }

    private static Result<DormancyOutcome> CloseIfSettled(
        IBankingUnitOfWork unitOfWork,
        DepositAccount account,
        LedgerBalance balance,
        UtcTimestamp now)
    {
        if (balance.PostedBalance.Value != 0 || !balance.HeldAmount.IsZero)
        {
            return Result<DormancyOutcome>.Success(DormancyOutcome.NotDue);
        }

        account.RequestClosure(ClosureReason.Dormancy, now);
        unitOfWork.DepositAccounts.Update(account);

        return Result<DormancyOutcome>.Success(DormancyOutcome.Closed);
    }

    private static Result<MoneyMinor> Charge(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        DepositAccount account,
        LedgerBalance balance,
        UtcTimestamp now)
    {
        if (EconomyBusinessCalendar.Resolve(
                unitOfWork.EconomyCalendars, bank.EconomyScopeId, now) is not { } point)
        {
            return Result<MoneyMinor>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        Result<FeeAssessmentPlan> plan = FeeResolver.Resolve(
            unitOfWork,
            bank,
            account,
            FeeType.DormancyWeekly,
            FeeChannel.System,
            counterpartyBankId: null,
            balance.AvailableBalance,
            point);

        if (!plan.IsSuccess)
        {
            return Result<MoneyMinor>.Failure(plan.Error!);
        }

        long calculated = Math.Max(1L, plan.Value.Quote.Amount.Value);

        return Result<MoneyMinor>.Success(
            MoneyMinor.FromMinor(Math.Min(balance.AvailableBalance.Value, calculated)));
    }

    private Result Post(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        DepositAccount account,
        MoneyMinor charged,
        IdempotencyKey idempotencyKey,
        UtcTimestamp now)
    {
        BusinessDate businessDate = BusinessDate.FromDayNumber(
            DateOnly.FromDateTime(
                DateTimeOffset.FromUnixTimeMilliseconds(now.UnixMilliseconds).UtcDateTime).DayNumber);

        if (unitOfWork.AccountingPeriods.FindOpen(bank.GeneralLedgerBookId, businessDate)
            is not { } period)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        if (unitOfWork.LedgerAccounts.FindPostingByKind(
                bank.GeneralLedgerBookId, LedgerAccountKind.FeeRevenue, account.CurrencyId)
            is not { } revenue)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.FeeRevenueAccountUnavailable);
        }

        LedgerAccount depositLedger = unitOfWork.LedgerAccounts.Find(account.LedgerAccountId)!;

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            OperationType,
            bank.EconomyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            idempotencyKey,
            now);

        unitOfWork.BusinessOperations.Add(operation);

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Deposit(depositLedger, EntrySide.Debit, charged));
        posting.Add(PostingLine.Institutional(revenue, EntrySide.Credit, charged));

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bank.GeneralLedgerBookId,
                operation.Id,
                account.CurrencyId,
                businessDate,
                now,
                now,
                TransactionType,
                DescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            period);

        posting.ApplyProjections(unitOfWork, ordered, now);

        unitOfWork.FeeAssessments.Add(FeeAssessment.Assess(
            FeeAssessmentId.FromValue(idGenerator.NextId()),
            operation.Id,
            bank.CurrentFeeScheduleVersionId,
            feeRuleId: null,
            account.CurrencyId,
            depositLedger.Id,
            revenue.Id,
            FeeType.DormancyWeekly,
            charged,
            now));

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        return Result.Success();
    }

    private enum DormancyOutcome
    {
        NotDue = 0,
        Assessed = 1,
        Closed = 2,
    }
}
