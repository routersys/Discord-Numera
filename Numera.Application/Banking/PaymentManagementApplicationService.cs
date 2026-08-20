using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record ListBeneficiariesQuery(CustomerAccountId CustomerAccountId, string? Cursor);

public sealed record ListScheduledPaymentsQuery(CustomerAccountId CustomerAccountId, string? Cursor);

public sealed record ListDirectDebitMandatesQuery(CustomerAccountId CustomerAccountId, string? Cursor);

public sealed record SaveBeneficiaryCommand(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DestinationDepositAccountId,
    string DisplayName);

public sealed record HideBeneficiaryCommand(
    CustomerAccountId CustomerAccountId,
    SavedBeneficiaryId SavedBeneficiaryId);

public sealed record CreateScheduledPaymentCommand(
    ulong GuildId,
    CustomerAccountId CustomerAccountId,
    DepositAccountId SourceDepositAccountId,
    DepositAccountId DestinationDepositAccountId,
    ScheduledPaymentKind Kind,
    long AmountMinor,
    int LocalMinuteOfDay,
    int? AnchorDayOfMonth,
    SavedBeneficiaryId? SavedBeneficiaryId = null);

public sealed record SetScheduledPaymentStateCommand(
    CustomerAccountId CustomerAccountId,
    ScheduledPaymentPlanId ScheduledPaymentPlanId,
    ScheduledPaymentPlanStatus DesiredStatus);

public sealed record CreateDirectDebitMandateCommand(
    CustomerAccountId DebtorCustomerAccountId,
    DepositAccountId DebtorDepositAccountId,
    DepositAccountId CreditorSettlementAccountId,
    long SingleCollectionLimitMinor,
    long? ValidUntil);

public sealed record SetDirectDebitMandateStateCommand(
    CustomerAccountId DebtorCustomerAccountId,
    DirectDebitMandateId DirectDebitMandateId,
    DirectDebitMandateStatus DesiredStatus);

public sealed record SavedBeneficiaryView(
    SavedBeneficiaryId Id,
    string DisplayName,
    string InstitutionCode,
    string AccountNumberSuffix,
    SavedBeneficiaryStatus Status);

public sealed record SavedBeneficiaryPageView(IReadOnlyList<SavedBeneficiaryView> Items, string? NextCursor);

public sealed record ScheduledPaymentPlanView(
    ScheduledPaymentPlanId Id,
    ScheduledPaymentKind Kind,
    ScheduledPaymentPlanStatus Status,
    MoneyMinor Amount,
    long? NextDueAt);

public sealed record ScheduledPaymentPageView(
    IReadOnlyList<ScheduledPaymentPlanView> Items,
    string? NextCursor);

public sealed record DirectDebitMandateView(
    DirectDebitMandateId Id,
    DirectDebitMandateStatus Status,
    MoneyMinor SingleCollectionLimit,
    long ValidFrom,
    long? ValidUntil);

public sealed record DirectDebitMandatePageView(
    IReadOnlyList<DirectDebitMandateView> Items,
    string? NextCursor);

public interface IPaymentManagementApplicationService
{
    Task<Result<SavedBeneficiaryPageView>> ListBeneficiariesAsync(
        ListBeneficiariesQuery query,
        CancellationToken cancellationToken);

    Task<Result<ScheduledPaymentPageView>> ListScheduledPaymentsAsync(
        ListScheduledPaymentsQuery query,
        CancellationToken cancellationToken);

    Task<Result<DirectDebitMandatePageView>> ListDirectDebitMandatesAsync(
        ListDirectDebitMandatesQuery query,
        CancellationToken cancellationToken);

    Task<Result<SavedBeneficiaryView>> SaveBeneficiaryAsync(
        SaveBeneficiaryCommand command,
        CancellationToken cancellationToken);

    Task<Result> HideBeneficiaryAsync(
        HideBeneficiaryCommand command,
        CancellationToken cancellationToken);

    Task<Result<ScheduledPaymentPlanView>> CreateScheduledPaymentAsync(
        CreateScheduledPaymentCommand command,
        CancellationToken cancellationToken);

    Task<Result<ScheduledPaymentPlanView>> SetScheduledPaymentStateAsync(
        SetScheduledPaymentStateCommand command,
        CancellationToken cancellationToken);

    Task<Result<DirectDebitMandateView>> CreateDirectDebitMandateAsync(
        CreateDirectDebitMandateCommand command,
        CancellationToken cancellationToken);

    Task<Result<DirectDebitMandateView>> SetDirectDebitMandateStateAsync(
        SetDirectDebitMandateStateCommand command,
        CancellationToken cancellationToken);
}

public sealed class PaymentManagementApplicationService : IPaymentManagementApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public PaymentManagementApplicationService(
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

    public Task<Result<SavedBeneficiaryPageView>> ListBeneficiariesAsync(
        ListBeneficiariesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                IReadOnlyList<SavedBeneficiary> fetched = unitOfWork.PaymentManagement.ListBeneficiaries(
                    query.CustomerAccountId,
                    Cursor(query.Cursor),
                    PaginationBudget.ListPageSize + PaginationBudget.QueryLookAhead);

                return Result<SavedBeneficiaryPageView>.Success(new SavedBeneficiaryPageView(
                    [.. Page(fetched, PaginationBudget.ListPageSize).Select(ToItem)],
                    NextCursor(
                        fetched,
                        PaginationBudget.ListPageSize,
                        static item => item.CreatedAt.UnixMilliseconds)));
            },
            cancellationToken);
    }

    public Task<Result<ScheduledPaymentPageView>> ListScheduledPaymentsAsync(
        ListScheduledPaymentsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                IReadOnlyList<ScheduledPaymentPlan> fetched = unitOfWork.PaymentManagement.ListPlans(
                    query.CustomerAccountId,
                    Cursor(query.Cursor),
                    PaginationBudget.ListPageSize + PaginationBudget.QueryLookAhead);

                return Result<ScheduledPaymentPageView>.Success(new ScheduledPaymentPageView(
                    [.. Page(fetched, PaginationBudget.ListPageSize).Select(ToItem)],
                    NextCursor(
                        fetched,
                        PaginationBudget.ListPageSize,
                        static plan => plan.CreatedAt.UnixMilliseconds)));
            },
            cancellationToken);
    }

    public Task<Result<DirectDebitMandatePageView>> ListDirectDebitMandatesAsync(
        ListDirectDebitMandatesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                IReadOnlyList<DirectDebitMandate> fetched =
                    unitOfWork.PaymentManagement.ListMandatesForDebtor(
                        query.CustomerAccountId,
                        Cursor(query.Cursor),
                        PaginationBudget.ListPageSize + PaginationBudget.QueryLookAhead);

                return Result<DirectDebitMandatePageView>.Success(new DirectDebitMandatePageView(
                    [.. Page(fetched, PaginationBudget.ListPageSize).Select(ToItem)],
                    NextCursor(
                        fetched,
                        PaginationBudget.ListPageSize,
                        static mandate => mandate.ValidFrom.UnixMilliseconds)));
            },
            cancellationToken);
    }

    public Task<Result<SavedBeneficiaryView>> SaveBeneficiaryAsync(
        SaveBeneficiaryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => SaveBeneficiary(unitOfWork, command), cancellationToken);
    }

    public Task<Result> HideBeneficiaryAsync(
        HideBeneficiaryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return HideAsync(command, cancellationToken);
    }

    public Task<Result<ScheduledPaymentPlanView>> CreateScheduledPaymentAsync(
        CreateScheduledPaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => CreatePlan(unitOfWork, command), cancellationToken);
    }

    public Task<Result<ScheduledPaymentPlanView>> SetScheduledPaymentStateAsync(
        SetScheduledPaymentStateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => SetPlanState(unitOfWork, command), cancellationToken);
    }

    public Task<Result<DirectDebitMandateView>> CreateDirectDebitMandateAsync(
        CreateDirectDebitMandateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => CreateMandate(unitOfWork, command), cancellationToken);
    }

    public Task<Result<DirectDebitMandateView>> SetDirectDebitMandateStateAsync(
        SetDirectDebitMandateStateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => SetMandateState(unitOfWork, command), cancellationToken);
    }

    private Result<SavedBeneficiaryView> SaveBeneficiary(
        IBankingUnitOfWork unitOfWork,
        SaveBeneficiaryCommand command)
    {
        if (unitOfWork.DepositAccounts.Find(command.DestinationDepositAccountId) is not { } destination)
        {
            return Result<SavedBeneficiaryView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (destination.IsClosed || destination.Status != DepositAccountStatus.Active)
        {
            return Result<SavedBeneficiaryView>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.BeneficiaryNotReceivable);
        }

        if (unitOfWork.PaymentManagement.FindActiveBeneficiary(
                command.CustomerAccountId, command.DestinationDepositAccountId) is not null)
        {
            return Result<SavedBeneficiaryView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BeneficiaryAlreadySaved);
        }

        if (unitOfWork.Banks.Find(destination.BankId) is not { } bank)
        {
            return Result<SavedBeneficiaryView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (unitOfWork.Branches.FindCodeById(destination.BranchId) is not { } branchCode)
        {
            return Result<SavedBeneficiaryView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BranchNotFound);
        }

        SavedBeneficiary beneficiary;

        try
        {
            beneficiary = SavedBeneficiary.Save(
                SavedBeneficiaryId.FromValue(idGenerator.NextId()),
                command.CustomerAccountId,
                destination.Id,
                command.DisplayName,
                bank.InstitutionCode.Value,
                branchCode,
                destination.AccountNumber.Value,
                clock.Now());
        }
        catch (InvariantViolationException)
        {
            return Result<SavedBeneficiaryView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.BeneficiaryNameInvalid);
        }

        unitOfWork.PaymentManagement.AddBeneficiary(beneficiary);

        return Result<SavedBeneficiaryView>.Success(ToItem(beneficiary));
    }

    private async Task<Result> HideAsync(
        HideBeneficiaryCommand command,
        CancellationToken cancellationToken) =>
        Void(await writeGateway
            .ExecuteAsync(unitOfWork => HideBeneficiary(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false));

    private static Result Void(Result<bool> outcome) =>
        outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);

    private static Result<bool> HideBeneficiary(
        IBankingUnitOfWork unitOfWork,
        HideBeneficiaryCommand command)
    {
        if (unitOfWork.PaymentManagement.FindBeneficiary(command.SavedBeneficiaryId) is not { } beneficiary
            || beneficiary.CustomerAccountId != command.CustomerAccountId)
        {
            return Result<bool>.Failure(ErrorCategory.NotFound, BankingErrorCodes.BeneficiaryNotFound);
        }

        if (beneficiary.Status != SavedBeneficiaryStatus.Active)
        {
            return Result<bool>.Failure(ErrorCategory.Conflict, BankingErrorCodes.BeneficiaryNotActive);
        }

        beneficiary.Hide();
        unitOfWork.PaymentManagement.UpdateBeneficiary(beneficiary);

        return Result<bool>.Success(true);
    }

    private Result<ScheduledPaymentPlanView> CreatePlan(
        IBankingUnitOfWork unitOfWork,
        CreateScheduledPaymentCommand command)
    {
        if (unitOfWork.GuildEconomies.FindEconomyScope(command.GuildId) is not { } scope)
        {
            return Result<ScheduledPaymentPlanView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        if (unitOfWork.DepositAccounts.Find(command.SourceDepositAccountId) is not { } source
            || source.CustomerAccountId != command.CustomerAccountId)
        {
            return Result<ScheduledPaymentPlanView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (unitOfWork.DepositAccounts.Find(command.DestinationDepositAccountId) is not { } destination)
        {
            return Result<ScheduledPaymentPlanView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (source.CurrencyId != destination.CurrencyId)
        {
            return Result<ScheduledPaymentPlanView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.ScheduledPaymentCurrencyMismatch);
        }

        if (unitOfWork.EconomyCalendars.FindCanonicalTimezone(scope) is not { } timezone)
        {
            return Result<ScheduledPaymentPlanView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        if (!ScheduledPaymentSchedule.TryResolveFirst(
                timezone,
                command.Kind,
                command.AnchorDayOfMonth,
                command.LocalMinuteOfDay,
                clock.Now(),
                out UtcTimestamp dueAt))
        {
            return Result<ScheduledPaymentPlanView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.ScheduledPaymentScheduleInvalid);
        }

        ScheduledPaymentPlan plan;

        try
        {
            plan = ScheduledPaymentPlan.Create(
                ScheduledPaymentPlanId.FromValue(idGenerator.NextId()),
                command.CustomerAccountId,
                source.Id,
                destination.Id,
                command.SavedBeneficiaryId,
                source.CurrencyId,
                command.Kind,
                MoneyMinor.FromMinor(command.AmountMinor),
                command.AnchorDayOfMonth,
                timezone,
                dueAt,
                clock.Now());
        }
        catch (InvariantViolationException)
        {
            return Result<ScheduledPaymentPlanView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.ScheduledPaymentScheduleInvalid);
        }

        unitOfWork.PaymentManagement.AddPlan(plan);
        unitOfWork.PaymentManagement.AddOccurrence(ScheduledPaymentOccurrence.Schedule(
            ScheduledPaymentOccurrenceId.FromValue(idGenerator.NextId()), plan.Id, dueAt));

        return Result<ScheduledPaymentPlanView>.Success(ToItem(plan));
    }

    private Result<ScheduledPaymentPlanView> SetPlanState(
        IBankingUnitOfWork unitOfWork,
        SetScheduledPaymentStateCommand command)
    {
        if (unitOfWork.PaymentManagement.FindPlan(command.ScheduledPaymentPlanId) is not { } plan
            || plan.CustomerAccountId != command.CustomerAccountId)
        {
            return Result<ScheduledPaymentPlanView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.ScheduledPaymentNotFound);
        }

        if (plan.Status == command.DesiredStatus)
        {
            return Result<ScheduledPaymentPlanView>.Success(ToItem(plan));
        }

        try
        {
            switch (command.DesiredStatus)
            {
                case ScheduledPaymentPlanStatus.Paused:
                    plan.Pause();
                    break;
                case ScheduledPaymentPlanStatus.Cancelled:
                    plan.Cancel();
                    break;
                case ScheduledPaymentPlanStatus.Active:
                    if (!ScheduledPaymentSchedule.TryResolveNext(
                            plan.CanonicalTimezone,
                            plan.Kind,
                            plan.AnchorDayOfMonth,
                            plan.NextDueAt ?? clock.Now(),
                            out UtcTimestamp resumed))
                    {
                        return Result<ScheduledPaymentPlanView>.Failure(
                            ErrorCategory.Conflict, BankingErrorCodes.ScheduledPaymentNotResumable);
                    }

                    plan.Resume(resumed);
                    break;
                default:
                    return Result<ScheduledPaymentPlanView>.Failure(
                        ErrorCategory.Conflict, BankingErrorCodes.ScheduledPaymentStateInvalid);
            }
        }
        catch (InvariantViolationException)
        {
            return Result<ScheduledPaymentPlanView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.ScheduledPaymentStateInvalid);
        }

        unitOfWork.PaymentManagement.UpdatePlan(plan);

        return Result<ScheduledPaymentPlanView>.Success(ToItem(plan));
    }

    private Result<DirectDebitMandateView> CreateMandate(
        IBankingUnitOfWork unitOfWork,
        CreateDirectDebitMandateCommand command)
    {
        if (unitOfWork.DepositAccounts.Find(command.DebtorDepositAccountId) is not { } debtor
            || debtor.CustomerAccountId != command.DebtorCustomerAccountId)
        {
            return Result<DirectDebitMandateView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (unitOfWork.DepositAccounts.Find(command.CreditorSettlementAccountId) is not { } creditor)
        {
            return Result<DirectDebitMandateView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (debtor.CurrencyId != creditor.CurrencyId)
        {
            return Result<DirectDebitMandateView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.DirectDebitCurrencyMismatch);
        }

        if (unitOfWork.CustomerAccounts.Find(creditor.CustomerAccountId) is not { } creditorCustomer)
        {
            return Result<DirectDebitMandateView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        DirectDebitMandate mandate;

        try
        {
            mandate = DirectDebitMandate.Request(
                DirectDebitMandateId.FromValue(idGenerator.NextId()),
                creditorCustomer.PartyId,
                creditor.Id,
                command.DebtorCustomerAccountId,
                debtor.Id,
                debtor.CurrencyId,
                MoneyMinor.FromMinor(command.SingleCollectionLimitMinor),
                clock.Now(),
                command.ValidUntil is { } until ? UtcTimestamp.FromUnixMilliseconds(until) : null);
        }
        catch (InvariantViolationException)
        {
            return Result<DirectDebitMandateView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.DirectDebitMandateInvalid);
        }

        unitOfWork.PaymentManagement.AddMandate(mandate);

        return Result<DirectDebitMandateView>.Success(ToItem(mandate));
    }

    private Result<DirectDebitMandateView> SetMandateState(
        IBankingUnitOfWork unitOfWork,
        SetDirectDebitMandateStateCommand command)
    {
        if (unitOfWork.PaymentManagement.FindMandate(command.DirectDebitMandateId) is not { } mandate
            || mandate.DebtorCustomerAccountId != command.DebtorCustomerAccountId)
        {
            return Result<DirectDebitMandateView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DirectDebitMandateNotFound);
        }

        if (mandate.Status == command.DesiredStatus)
        {
            return Result<DirectDebitMandateView>.Success(ToItem(mandate));
        }

        UtcTimestamp now = clock.Now();

        try
        {
            switch (command.DesiredStatus)
            {
                case DirectDebitMandateStatus.Active:
                    mandate.Activate(now);
                    break;
                case DirectDebitMandateStatus.Suspended:
                    mandate.Suspend();
                    break;
                case DirectDebitMandateStatus.Revoked:
                    mandate.Revoke(now);
                    break;
                default:
                    return Result<DirectDebitMandateView>.Failure(
                        ErrorCategory.Conflict, BankingErrorCodes.DirectDebitMandateStateInvalid);
            }
        }
        catch (InvariantViolationException)
        {
            return Result<DirectDebitMandateView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DirectDebitMandateStateInvalid);
        }

        unitOfWork.PaymentManagement.UpdateMandate(mandate);

        return Result<DirectDebitMandateView>.Success(ToItem(mandate));
    }

    private static SavedBeneficiaryView ToItem(SavedBeneficiary beneficiary) =>
        new(
            beneficiary.Id,
            beneficiary.DisplayName,
            beneficiary.InstitutionCodeSnapshot,
            Suffix(beneficiary.AccountNumberSnapshot),
            beneficiary.Status);

    private static ScheduledPaymentPlanView ToItem(ScheduledPaymentPlan plan) =>
        new(plan.Id, plan.Kind, plan.Status, plan.Amount, plan.NextDueAt?.UnixMilliseconds);

    private static DirectDebitMandateView ToItem(DirectDebitMandate mandate) =>
        new(
            mandate.Id,
            mandate.Status,
            mandate.SingleCollectionLimit,
            mandate.ValidFrom.UnixMilliseconds,
            mandate.ValidUntil?.UnixMilliseconds);

    private static string Suffix(string accountNumber) =>
        accountNumber.Length >= AccountNumber.SuffixLength
            ? accountNumber[^AccountNumber.SuffixLength..]
            : accountNumber;

    private static long? Cursor(string? cursor) =>
        long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;

    private static IReadOnlyList<TItem> Page<TItem>(IReadOnlyList<TItem> fetched, int pageSize) =>
        fetched.Count <= pageSize ? fetched : [.. fetched.Take(pageSize)];

    private static string? NextCursor<TItem>(
        IReadOnlyList<TItem> fetched,
        int pageSize,
        Func<TItem, long> key) =>
        fetched.Count > pageSize
            ? key(fetched[pageSize - 1]).ToString(CultureInfo.InvariantCulture)
            : null;
}
