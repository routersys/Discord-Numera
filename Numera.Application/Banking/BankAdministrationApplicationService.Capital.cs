using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record ContributeBankCapitalCommand(
    AuthorizationContext Actor,
    string InstitutionCode,
    string? SourceInstitutionCode,
    long AmountMinor,
    string IdempotencyToken);

public sealed record ActivateBankCommand(
    AuthorizationContext Actor,
    string InstitutionCode,
    string IdempotencyToken);

public sealed record BankCapitalView(
    BankId BankId,
    string InstitutionCode,
    MoneyMinor ContributedAmount,
    MoneyMinor PaidInCapital,
    MoneyMinor MinimumInitialCapital,
    BankStatus Status);

public sealed partial class BankAdministrationApplicationService
{
    public const string CapitalOperationType = "BANK_CAPITAL_CONTRIBUTION";
    public const string ActivateOperationType = "BANK_ACTIVATE";
    public const string CapitalContributedEventType = "BANK_CAPITAL_CONTRIBUTED";
    public const string BankActivatedEventType = "BANK_ACTIVATED";

    private const string CapitalDescriptionCode = "BANK_CAPITAL";

    private readonly record struct CapitalLegs(
        SettlementParticipation Participation,
        LedgerAccount Reserve,
        LedgerAccount CentralBankLiability,
        AccountingBookId CentralBankBookId);

    public Task<Result<BankCapitalView>> ContributeBankCapitalAsync(
        ContributeBankCapitalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => ContributeCapital(unitOfWork, command), cancellationToken);
    }

    public Task<Result<BankView>> ActivateBankAsync(
        ActivateBankCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Activate(unitOfWork, command), cancellationToken);
    }

    private Result<BankCapitalView> ContributeCapital(
        IBankingUnitOfWork unitOfWork,
        ContributeBankCapitalCommand command)
    {
        if (!IdempotencyKey.TryCreate(
            CapitalOperationType, command.IdempotencyToken, out IdempotencyKey idempotencyKey))
        {
            return Result<BankCapitalView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.BankIdentityInvalid,
                nameof(command.IdempotencyToken));
        }

        Result<Bank> resolved = ResolveManagedBank(unitOfWork, command.Actor, command.InstitutionCode);

        if (!resolved.IsSuccess)
        {
            return Result<BankCapitalView>.Failure(resolved.Error!);
        }

        Bank bank = resolved.Value;

        if (bank.Status != BankStatus.PendingActivation)
        {
            return Result<BankCapitalView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankNotPendingActivation);
        }

        if (command.AmountMinor <= 0)
        {
            return Result<BankCapitalView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.AmountInvalid, nameof(command.AmountMinor));
        }

        if (unitOfWork.BankAdministration.FindPublishedPrudentialPolicy(bank.EconomyScopeId)
            is not { } policy)
        {
            return Result<BankCapitalView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.PrudentialPolicyUnavailable);
        }

        if (unitOfWork.BankAdministration.FindActiveCurrency(bank.EconomyScopeId) is not { } currencyId)
        {
            return Result<BankCapitalView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CurrencyUnavailable);
        }

        Result<CapitalLegs> destination = ResolveDirectLegs(unitOfWork, bank, currencyId);

        if (!destination.IsSuccess)
        {
            return Result<BankCapitalView>.Failure(destination.Error!);
        }

        if (unitOfWork.LedgerAccounts.FindPostingByKind(
            bank.GeneralLedgerBookId, LedgerAccountKind.PaidInCapital, currencyId) is not { } paidInAccount)
        {
            return Result<BankCapitalView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankCapitalAccountUnavailable);
        }

        if (unitOfWork.BusinessOperations.Find(idempotencyKey) is not null)
        {
            return Result<BankCapitalView>.Success(new BankCapitalView(
                bank.Id,
                bank.InstitutionCode.Value,
                MoneyMinor.Zero,
                Posted(unitOfWork, paidInAccount.Id),
                policy.MinimumInitialBankCapital,
                bank.Status));
        }

        MoneyMinor amount = MoneyMinor.FromMinor(command.AmountMinor);
        UtcTimestamp now = clock.Now();
        BusinessDate businessDate = CapitalBusinessDateOf(now);

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            CapitalOperationType,
            bank.EconomyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            idempotencyKey,
            now);

        unitOfWork.BusinessOperations.Add(operation);

        Result funded = string.IsNullOrWhiteSpace(command.SourceInstitutionCode)
            ? PostIssuerFunding(
                unitOfWork, operation, destination.Value, currencyId, amount, businessDate, now)
            : PostContributorFunding(
                unitOfWork, operation, command, bank, destination.Value, currencyId, amount, businessDate,
                now);

        if (!funded.IsSuccess)
        {
            return Result<BankCapitalView>.Failure(funded.Error!);
        }

        LedgerPostingBuilder receiving = new();
        receiving.Add(PostingLine.Institutional(destination.Value.Reserve, EntrySide.Debit, amount));
        receiving.Add(PostingLine.Institutional(paidInAccount, EntrySide.Credit, amount));

        Result posted = PostCapital(
            unitOfWork, operation, bank.GeneralLedgerBookId, currencyId, receiving, businessDate, now);

        if (!posted.IsSuccess)
        {
            return Result<BankCapitalView>.Failure(posted.Error!);
        }

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            operation.Id,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            CapitalOperationType,
            "bank",
            bank.Id.Value,
            command.SourceInstitutionCode,
            now);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            CapitalContributedEventType,
            $$"""{"bank_id":"{{bank.Id.Value}}","amount_minor":{{amount.Value}}}""",
            now));

        return Result<BankCapitalView>.Success(new BankCapitalView(
            bank.Id,
            bank.InstitutionCode.Value,
            amount,
            Posted(unitOfWork, paidInAccount.Id),
            policy.MinimumInitialBankCapital,
            bank.Status));
    }

    private Result PostIssuerFunding(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        CapitalLegs destination,
        CurrencyId currencyId,
        MoneyMinor amount,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        if (unitOfWork.Currencies.FindIssuanceLiabilityAccount(currencyId) is not { } issuance ||
            issuance.BookId != destination.CentralBankBookId)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CentralBankBookUnavailable);
        }

        LedgerBalance available = unitOfWork.LedgerAccounts.FindProjection(issuance.Id) ?? LedgerBalance.Empty;

        if (!available.CanReserve(amount))
        {
            return Result.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        LedgerPostingBuilder central = new();
        central.Add(PostingLine.Institutional(issuance, EntrySide.Debit, amount));
        central.Add(PostingLine.Institutional(destination.CentralBankLiability, EntrySide.Credit, amount));

        return PostCapital(
            unitOfWork, operation, destination.CentralBankBookId, currencyId, central, businessDate, now);
    }

    private Result PostContributorFunding(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        ContributeBankCapitalCommand command,
        Bank bank,
        CapitalLegs destination,
        CurrencyId currencyId,
        MoneyMinor amount,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        if (!DiscordUserId.TryParse(
                command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
                out DiscordUserId discordUserId) ||
            unitOfWork.DiscordIdentityLinks.FindActive(discordUserId) is not { } link)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (unitOfWork.Banks.FindByInstitutionCode(
                bank.EconomyScopeId, command.SourceInstitutionCode!) is not { } contributorBank ||
            contributorBank.Id == bank.Id)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (contributorBank.Status != BankStatus.Operating)
        {
            return Result.Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        Result<CapitalLegs> contributorLegs = ResolveDirectLegs(unitOfWork, contributorBank, currencyId);

        if (!contributorLegs.IsSuccess)
        {
            return Result.Failure(contributorLegs.Error!);
        }

        if (!contributorLegs.Value.Participation.SettlesDirectly)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementParticipationUnavailable);
        }

        if (unitOfWork.DepositAccounts.FindByCustomer(contributorBank.Id, link.CustomerAccountId)
            is not { } source)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (source.CurrencyId != currencyId ||
            source.Permits(AccountOperation.OutgoingTransfer) != StatusPermission.Allowed)
        {
            return Result.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DepositAccountNotOperable);
        }

        if (unitOfWork.LedgerAccounts.Find(source.LedgerAccountId) is not { } sourceLedger)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankCapitalAccountUnavailable);
        }

        LedgerBalance available =
            unitOfWork.LedgerAccounts.FindProjection(source.LedgerAccountId) ?? LedgerBalance.Empty;

        if (!available.CanReserve(amount))
        {
            return Result.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        LedgerPostingBuilder contributor = new();
        contributor.Add(PostingLine.Deposit(sourceLedger, EntrySide.Debit, amount));
        contributor.Add(PostingLine.Institutional(
            contributorLegs.Value.Reserve, EntrySide.Credit, amount));

        Result posted = PostCapital(
            unitOfWork,
            operation,
            contributorBank.GeneralLedgerBookId,
            currencyId,
            contributor,
            businessDate,
            now);

        if (!posted.IsSuccess)
        {
            return posted;
        }

        LedgerPostingBuilder central = new();
        central.Add(PostingLine.Institutional(
            contributorLegs.Value.CentralBankLiability, EntrySide.Debit, amount));
        central.Add(PostingLine.Institutional(destination.CentralBankLiability, EntrySide.Credit, amount));

        return PostCapital(
            unitOfWork, operation, destination.CentralBankBookId, currencyId, central, businessDate, now);
    }

    private Result<BankView> Activate(IBankingUnitOfWork unitOfWork, ActivateBankCommand command)
    {
        if (!IdempotencyKey.TryCreate(
            ActivateOperationType, command.IdempotencyToken, out IdempotencyKey idempotencyKey))
        {
            return Result<BankView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.BankIdentityInvalid,
                nameof(command.IdempotencyToken));
        }

        Result<Bank> resolved = ResolveManagedBank(unitOfWork, command.Actor, command.InstitutionCode);

        if (!resolved.IsSuccess)
        {
            return Result<BankView>.Failure(resolved.Error!);
        }

        Bank bank = resolved.Value;

        if (bank.Status != BankStatus.PendingActivation)
        {
            return Result<BankView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankNotPendingActivation);
        }

        if (bank.CurrentPolicyVersionId is not { } policyVersionId ||
            bank.CurrentFeeScheduleVersionId is not { } feeScheduleVersionId)
        {
            return Result<BankView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankPolicyVersionNotFound);
        }

        if (unitOfWork.BankAdministration.FindPublishedPrudentialPolicy(bank.EconomyScopeId)
            is not { } policy)
        {
            return Result<BankView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.PrudentialPolicyUnavailable);
        }

        if (unitOfWork.BankAdministration.FindActiveCurrency(bank.EconomyScopeId) is not { } currencyId)
        {
            return Result<BankView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CurrencyUnavailable);
        }

        Result<CapitalLegs> legs = ResolveDirectLegs(unitOfWork, bank, currencyId);

        if (!legs.IsSuccess)
        {
            return Result<BankView>.Failure(legs.Error!);
        }

        if (unitOfWork.LedgerAccounts.FindPostingByKind(
            bank.GeneralLedgerBookId, LedgerAccountKind.PaidInCapital, currencyId) is not { } paidInAccount)
        {
            return Result<BankView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankCapitalAccountUnavailable);
        }

        if (Posted(unitOfWork, legs.Value.Reserve.Id) !=
            Posted(unitOfWork, legs.Value.CentralBankLiability.Id) ||
            unitOfWork.Reconciliation.CountUnresolvedIssues(bank.EconomyScopeId) > 0)
        {
            return Result<BankView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankOpeningBalanceMismatch);
        }

        MoneyMinor paidIn = Posted(unitOfWork, paidInAccount.Id);

        if (paidIn < policy.MinimumInitialBankCapital)
        {
            return Result<BankView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankCapitalInsufficient);
        }

        if (!PrudentialFloor.AdmitsActivation(unitOfWork, bank, policy))
        {
            return Result<BankView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankPrudentialFloorUnmet);
        }

        UtcTimestamp now = clock.Now();

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            ActivateOperationType,
            bank.EconomyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            idempotencyKey,
            now);

        unitOfWork.BusinessOperations.Add(operation);

        bank.Activate(policyVersionId, feeScheduleVersionId, paidIn, policy.MinimumInitialBankCapital);
        unitOfWork.BankAdministration.UpdateBank(bank);

        SettlementParticipation participation = legs.Value.Participation;

        if (participation.Status == SettlementParticipationStatus.Pending)
        {
            participation.Activate();
            unitOfWork.SettlementParticipations.Update(participation);
        }

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            operation.Id,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            ActivateOperationType,
            "bank",
            bank.Id.Value,
            reason: null,
            now);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            BankActivatedEventType,
            $$"""{"bank_id":"{{bank.Id.Value}}","paid_in_capital_minor":{{paidIn.Value}}}""",
            now));

        return Result<BankView>.Success(ToView(bank));
    }

    private static Result<CapitalLegs> ResolveDirectLegs(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CurrencyId currencyId)
    {
        if (unitOfWork.SettlementParticipations.FindLive(bank.Id) is not { } participation)
        {
            return Result<CapitalLegs>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementParticipationUnavailable);
        }

        if (participation.Mode != SettlementParticipationMode.Direct ||
            participation.CentralBankSettlementAccountId is not { } settlementAccountId)
        {
            return Result<CapitalLegs>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankSettlementModeUnsupported);
        }

        if (unitOfWork.CentralBankSettlementAccounts.Find(settlementAccountId) is not { } settlementAccount ||
            settlementAccount.Status != CentralBankSettlementAccountStatus.Active ||
            settlementAccount.CurrencyId != currencyId ||
            unitOfWork.LedgerAccounts.Find(settlementAccount.CentralBankLedgerAccountId) is not { } liability)
        {
            return Result<CapitalLegs>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CentralBankAccountUnavailable);
        }

        return unitOfWork.LedgerAccounts.FindPostingByKind(
                bank.GeneralLedgerBookId, LedgerAccountKind.CentralBankReserveAsset, currencyId)
            is { } reserve
            ? Result<CapitalLegs>.Success(
                new CapitalLegs(participation, reserve, liability, liability.BookId))
            : Result<CapitalLegs>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAccountUnavailable);
    }

    private Result PostCapital(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        AccountingBookId bookId,
        CurrencyId currencyId,
        LedgerPostingBuilder posting,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        if (unitOfWork.AccountingPeriods.FindOpen(bookId, businessDate) is not { } periodId)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bookId,
                operation.Id,
                currencyId,
                businessDate,
                now,
                now,
                CapitalOperationType,
                CapitalDescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            periodId);

        posting.ApplyProjections(unitOfWork, ordered, now);

        return Result.Success();
    }

    private static MoneyMinor Posted(IBankingUnitOfWork unitOfWork, LedgerAccountId id) =>
        (unitOfWork.LedgerAccounts.FindProjection(id) ?? LedgerBalance.Empty).PostedBalance;

    private static BusinessDate CapitalBusinessDateOf(UtcTimestamp at) => BusinessDate.FromDayNumber(
        DateOnly.FromDateTime(
            DateTimeOffset.FromUnixTimeMilliseconds(at.UnixMilliseconds).UtcDateTime).DayNumber);
}
