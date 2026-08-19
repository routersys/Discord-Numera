using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record CommitCreateBankCommand(
    AuthorizationContext Actor,
    string InstitutionCode,
    string BankName,
    string BranchCode,
    string BranchName,
    string ProductCode,
    string ProductName,
    bool OpeningEnabled,
    int MinimumCustomerAccountAgeDays,
    long MinimumInitialFundingMinor,
    bool RequiresManualApproval,
    bool ReopenClosedAccountAllowed,
    bool PublicReceivingEnabledDefault,
    SettlementParticipationMode SettlementMode,
    string? SettlementAgentInstitutionCode,
    AccountingBookId? CentralBankAccountingBookId,
    EconomyScopeId? TargetEconomyScopeId = null);

public sealed record ApproveAccountOpeningCommand(
    AuthorizationContext Actor,
    AccountOpeningApplicationId AccountOpeningApplicationId);

public sealed record RejectAccountOpeningCommand(
    AuthorizationContext Actor,
    AccountOpeningApplicationId AccountOpeningApplicationId,
    string ReasonCode);

public sealed record StartCreateBankCommand(
    AuthorizationContext Actor,
    string InstitutionCode,
    EconomyScopeId? TargetEconomyScopeId = null);

public sealed record UpdateBankPolicyCommand(
    AuthorizationContext Actor,
    string InstitutionCode,
    long ExpectedBankVersion,
    bool OpeningEnabled,
    int MinimumCustomerAccountAgeDays,
    long MinimumInitialFundingMinor,
    bool RequiresManualApproval,
    bool ReopenClosedAccountAllowed,
    bool PublicReceivingEnabledDefault);

public sealed record RetireBankCommand(
    AuthorizationContext Actor,
    string InstitutionCode);

public sealed record BankDraftView(
    EconomyScopeId EconomyScopeId,
    string InstitutionCode,
    CurrencyId CurrencyId,
    IReadOnlyList<string> Steps);

public sealed record BankView(
    BankId Id,
    string InstitutionCode,
    string Name,
    BankStatus Status,
    BankPolicyVersionId PolicyVersionId,
    FeeScheduleVersionId FeeScheduleVersionId);

public sealed record AccountOpeningApplicationView(
    AccountOpeningApplicationId Id,
    BankId BankId,
    CustomerAccountId CustomerAccountId,
    AccountOpeningApplicationStatus Status,
    MoneyMinor RequiredFunding,
    DepositAccountId? DepositAccountId);

public interface IBankAdministrationApplicationService
{
    Task<Result<BankDraftView>> StartCreateBankAsync(
        StartCreateBankCommand command,
        CancellationToken cancellationToken);

    Task<Result<BankView>> UpdateBankPolicyAsync(
        UpdateBankPolicyCommand command,
        CancellationToken cancellationToken);

    Task<Result> RetireBankAsync(
        RetireBankCommand command,
        CancellationToken cancellationToken);

    Task<Result<BankView>> CommitCreateBankAsync(
        CommitCreateBankCommand command,
        CancellationToken cancellationToken);

    Task<Result<AccountOpeningApplicationView>> ApproveAccountOpeningAsync(
        ApproveAccountOpeningCommand command,
        CancellationToken cancellationToken);

    Task<Result<AccountOpeningApplicationView>> RejectAccountOpeningAsync(
        RejectAccountOpeningCommand command,
        CancellationToken cancellationToken);

    Task<Result<BankCapitalView>> ContributeBankCapitalAsync(
        ContributeBankCapitalCommand command,
        CancellationToken cancellationToken);

    Task<Result<BankView>> ActivateBankAsync(
        ActivateBankCommand command,
        CancellationToken cancellationToken);
}

public sealed partial class BankAdministrationApplicationService : IBankAdministrationApplicationService
{
    public const string CreateOperationType = "BANK_CREATE";
    public const string PolicyUpdateOperationType = "BANK_POLICY_UPDATE";
    public const string ApproveOperationType = "ACCOUNT_OPENING_APPROVE";
    public const string RejectOperationType = "ACCOUNT_OPENING_REJECT";
    public const string BankCreatedEventType = "BANK_CREATED";
    public const string ApprovedEventType = "ACCOUNT_OPENING_APPROVED";
    public const string RejectedEventType = "ACCOUNT_OPENING_REJECTED";

    private const string ControlAccountCode = AccountOpeningWorkflow.DemandDepositControlCode;
    private const string ReserveAccountCode = "1100";
    private const string AgentBalanceAccountCode = "1200";
    private const string ClearingReceivableAccountCode = "1400";
    private const string SettlementPayableAccountCode = "2200";
    private const string SuspenseAccountCode = "2300";
    private const string ClearingPayableAccountCode = "2400";
    private const string PaidInCapitalAccountCode = "3000";
    private const string FeeRevenueAccountCode = "4300";
    private const string CentralBankLiabilityPrefix = "2100-";
    private const string ClientBankDepositPrefix = "2500-";
    private const int DefaultDebitCardValidityMonths = 60;
    private const string EstablishmentPeriodKey = "ESTABLISHMENT";

    private static readonly string[] BankCreateWizardSteps =
    [
        "IDENTITY",
        "OPENING_POLICY",
        "ACCOUNT_PRODUCT",
        "FEE_SCHEDULE",
        "TRANSFER_LIMITS",
        "DORMANCY",
        "BANK_CARD",
        "ATM_CASH",
        "SETTLEMENT",
        "BRANDING",
        "REVIEW",
        "COMMIT",
    ];

    private static readonly FeeType[] CatchAllFeeTypes =
    [
        FeeType.AccountOpening,
        FeeType.AccountMaintenance,
        FeeType.AccountClose,
        FeeType.SameBankTransfer,
        FeeType.InterbankTransfer,
        FeeType.DormancyWeekly,
        FeeType.DebitPurchase,
        FeeType.AtmOwnWithdrawal,
        FeeType.AtmPartnerWithdrawal,
        FeeType.AtmOwnDeposit,
        FeeType.AtmPartnerDeposit,
    ];

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public BankAdministrationApplicationService(
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

    public Task<Result<BankDraftView>> StartCreateBankAsync(
        StartCreateBankCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => StartCreateBank(unitOfWork, command), cancellationToken);
    }

    public Task<Result<BankView>> UpdateBankPolicyAsync(
        UpdateBankPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => UpdateBankPolicy(unitOfWork, command), cancellationToken);
    }

    public Task<Result> RetireBankAsync(
        RetireBankCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return RetireAsync(command, cancellationToken);
    }

    private static Result<BankDraftView> StartCreateBank(
        IBankingUnitOfWork unitOfWork,
        StartCreateBankCommand command)
    {
        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(
            unitOfWork, command.Actor, command.TargetEconomyScopeId);

        if (!scope.IsSuccess)
        {
            return Result<BankDraftView>.Failure(scope.Error!);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(unitOfWork, command.Actor, scope.Value);

        if (!authorized.IsSuccess)
        {
            return Result<BankDraftView>.Failure(authorized.Error!);
        }

        if (unitOfWork.BankAdministration.InstitutionCodeExists(command.InstitutionCode))
        {
            return Result<BankDraftView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankAlreadyExists);
        }

        if (unitOfWork.BankAdministration.FindActiveCurrency(scope.Value) is not { } currency)
        {
            return Result<BankDraftView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyNotFound);
        }

        return Result<BankDraftView>.Success(
            new BankDraftView(scope.Value, command.InstitutionCode, currency, BankCreateWizardSteps));
    }

    private Result<BankView> UpdateBankPolicy(
        IBankingUnitOfWork unitOfWork,
        UpdateBankPolicyCommand command)
    {
        Result<Bank> resolved = ResolveManagedBank(unitOfWork, command.Actor, command.InstitutionCode);

        if (!resolved.IsSuccess)
        {
            return Result<BankView>.Failure(resolved.Error!);
        }

        Bank bank = resolved.Value;

        if (bank.Version != command.ExpectedBankVersion)
        {
            return Result<BankView>.Failure(
                ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        if (bank.CurrentPolicyVersionId is not { } currentId
            || unitOfWork.BankAdministration.FindBankPolicyVersion(currentId) is not { } current)
        {
            return Result<BankView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankPolicyVersionNotFound);
        }

        UtcTimestamp now = clock.Now();

        BankPolicyVersion replacement;

        try
        {
            replacement = BankPolicyVersion.Create(
                BankPolicyVersionId.FromValue(idGenerator.NextId()),
                bank.Id,
                command.OpeningEnabled,
                command.MinimumCustomerAccountAgeDays,
                MoneyMinor.FromMinor(command.MinimumInitialFundingMinor),
                command.RequiresManualApproval,
                command.ReopenClosedAccountAllowed,
                command.PublicReceivingEnabledDefault,
                current.CashCardEnabled,
                current.DebitCardEnabled,
                current.IntegratedCashDebitDefault,
                current.AutomaticBankCardIssueMode,
                current.CashAtmEnabled,
                current.CashCardValidityMonths,
                current.DebitCardValidityMonths,
                current.PerTransferLimit,
                current.DailyOutgoingLimit,
                current.MaximumActiveHolds,
                now,
                effectiveTo: null,
                current.Version + 1);
        }
        catch (InvariantViolationException)
        {
            return Result<BankView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.BankPolicyInputInvalid);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            PolicyUpdateOperationType,
            bank.EconomyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            IdempotencyKey.Create(
                PolicyUpdateOperationType,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{bank.Id.Value}:{command.ExpectedBankVersion}")),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        unitOfWork.BankAdministration.AddBankPolicyVersion(replacement);
        bank.ApplyPolicyVersion(replacement.Id);
        unitOfWork.BankAdministration.UpdateBank(bank);

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            operation.Id,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            PolicyUpdateOperationType,
            "bank_policy_version",
            replacement.Id.Value,
            reason: null,
            now);

        return Result<BankView>.Success(ToView(bank));
    }

    private async Task<Result> RetireAsync(
        RetireBankCommand command,
        CancellationToken cancellationToken)
    {
        Result<bool> outcome = await writeGateway
            .ExecuteAsync(unitOfWork => RetireBank(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    private static Result<bool> RetireBank(IBankingUnitOfWork unitOfWork, RetireBankCommand command)
    {
        Result<Bank> resolved = ResolveManagedBank(unitOfWork, command.Actor, command.InstitutionCode);

        if (!resolved.IsSuccess)
        {
            return Result<bool>.Failure(resolved.Error!);
        }

        Bank bank = resolved.Value;

        if (bank.Status is not (BankStatus.Restricted or BankStatus.Resolution))
        {
            return Result<bool>.Failure(ErrorCategory.Conflict, BankingErrorCodes.BankNotRetirable);
        }

        if (unitOfWork.Relationships.CountByBank(bank.Id) > 0)
        {
            return Result<bool>.Failure(ErrorCategory.Conflict, BankingErrorCodes.BankHasCustomers);
        }

        bank.BeginClosing();
        unitOfWork.BankAdministration.UpdateBank(bank);

        return Result<bool>.Success(true);
    }

    private static Result<Bank> ResolveManagedBank(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        string institutionCode)
    {
        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(unitOfWork, actor, requested: null);

        if (!scope.IsSuccess)
        {
            return Result<Bank>.Failure(scope.Error!);
        }

        if (unitOfWork.Banks.FindByInstitutionCode(scope.Value, institutionCode) is not { } bank)
        {
            return Result<Bank>.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(unitOfWork, actor, bank.EconomyScopeId);

        return authorized.IsSuccess
            ? Result<Bank>.Success(bank)
            : Result<Bank>.Failure(authorized.Error!);
    }

    private static BankView ToView(Bank bank) =>
        new(
            bank.Id,
            bank.InstitutionCode.Value,
            bank.Name.Value,
            bank.Status,
            bank.CurrentPolicyVersionId!.Value,
            bank.CurrentFeeScheduleVersionId!.Value);

    public Task<Result<BankView>> CommitCreateBankAsync(
        CommitCreateBankCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => CommitCreate(unitOfWork, command), cancellationToken);
    }

    public Task<Result<AccountOpeningApplicationView>> ApproveAccountOpeningAsync(
        ApproveAccountOpeningCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Approve(unitOfWork, command), cancellationToken);
    }

    public Task<Result<AccountOpeningApplicationView>> RejectAccountOpeningAsync(
        RejectAccountOpeningCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Reject(unitOfWork, command), cancellationToken);
    }

    private Result<BankView> CommitCreate(IBankingUnitOfWork unitOfWork, CommitCreateBankCommand command)
    {
        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(
            unitOfWork, command.Actor, command.TargetEconomyScopeId);

        if (!scope.IsSuccess)
        {
            return Result<BankView>.Failure(scope.Error!);
        }

        EconomyScopeId economyScopeId = scope.Value;

        Result authorized = ManagementAuthorizationPolicy.Ensure(
            unitOfWork, command.Actor, economyScopeId);

        if (!authorized.IsSuccess)
        {
            return Result<BankView>.Failure(authorized.Error!);
        }

        if (!InstitutionCode.TryParse(command.InstitutionCode, out InstitutionCode institutionCode) ||
            !BankName.TryParse(command.BankName, out BankName bankName) ||
            !DisplayName.TryParse(command.BankName, out DisplayName partyName) ||
            !BranchCode.TryParse(command.BranchCode, out BranchCode branchCode) ||
            !DisplayName.TryParse(command.BranchName, out DisplayName branchName) ||
            !DisplayName.TryParse(command.ProductName, out DisplayName productName) ||
            command.MinimumCustomerAccountAgeDays < 0 ||
            command.MinimumInitialFundingMinor < 0 ||
            string.IsNullOrEmpty(command.ProductCode))
        {
            return Result<BankView>.Failure(ErrorCategory.Validation, BankingErrorCodes.BankIdentityInvalid);
        }

        if (unitOfWork.BankAdministration.InstitutionCodeExists(institutionCode.Value))
        {
            return Result<BankView>.Failure(ErrorCategory.Conflict, BankingErrorCodes.BankAlreadyExists);
        }

        if (unitOfWork.BankAdministration.FindActiveCurrency(economyScopeId) is not { } currencyId)
        {
            return Result<BankView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CurrencyUnavailable);
        }

        if (unitOfWork.BankAdministration.FindPublishedPrudentialPolicy(economyScopeId) is null)
        {
            return Result<BankView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.PrudentialPolicyUnavailable);
        }

        if (command.MinimumInitialFundingMinor > 0 &&
            !unitOfWork.BankAdministration.HasOperatingBank(economyScopeId))
        {
            return Result<BankView>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.OpeningFundingSourceUnavailable);
        }

        UtcTimestamp now = clock.Now();

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            CreateOperationType,
            economyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            IdempotencyKey.Create(CreateOperationType, institutionCode.Value),
            now);

        Party party = Party.Create(
            PartyId.FromValue(idGenerator.NextId()), PartyType.Bank, partyName, now);

        AccountingBookId bookId = AccountingBookId.FromValue(idGenerator.NextId());

        Bank bank = Bank.Establish(
            BankId.FromValue(idGenerator.NextId()),
            economyScopeId,
            party.Id,
            institutionCode,
            bankName,
            bookId,
            now);

        unitOfWork.Parties.Add(party);
        unitOfWork.BankAdministration.AddAccountingBook(bookId, party.Id, now);
        unitOfWork.AccountingPeriods.Open(
            AccountingPeriodId.FromValue(idGenerator.NextId()),
            bookId,
            EstablishmentPeriodKey,
            BusinessDate.FromDayNumber(DateOnly.MinValue.DayNumber),
            BusinessDate.FromDayNumber(DateOnly.MaxValue.DayNumber));
        unitOfWork.BankAdministration.AddBank(bank);
        unitOfWork.BankAdministration.AddBranch(
            BranchId.FromValue(idGenerator.NextId()), bank.Id, branchCode.Value, branchName.Value, now);

        AccountProductId productId = AccountProductId.FromValue(idGenerator.NextId());
        unitOfWork.BankAdministration.AddAccountProduct(
            productId, bank.Id, command.ProductCode, productName.Value, now);
        unitOfWork.BankAdministration.AddAccountProductVersion(
            AccountProductVersionId.FromValue(idGenerator.NextId()), productId, MoneyMinor.Zero, now);

        Result<BankPolicyVersion> policy = PublishPolicy(unitOfWork, bank, command, now);
        if (!policy.IsSuccess)
        {
            return Result<BankView>.Failure(policy.Error!);
        }

        FeeScheduleVersionId feeScheduleVersionId = PublishFeeSchedule(unitOfWork, bank, now);

        bank.ApplyPolicyVersion(policy.Value.Id);
        bank.ApplyFeeScheduleVersion(feeScheduleVersionId);
        unitOfWork.BankAdministration.UpdateBank(bank);

        CreateRequiredLedgerAccounts(unitOfWork, bank, currencyId, command.SettlementMode);

        Result participation = EnrollSettlement(unitOfWork, bank, currencyId, command, now);
        if (!participation.IsSuccess)
        {
            return Result<BankView>.Failure(participation.Error!);
        }

        unitOfWork.BusinessOperations.Add(operation);
        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            operation.Id,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            CreateOperationType,
            "bank",
            bank.Id.Value,
            reason: null,
            now);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            BankCreatedEventType,
            $$"""{"bank_id":"{{bank.Id.Value}}","institution_code":"{{institutionCode.Value}}"}""",
            now));

        return Result<BankView>.Success(new BankView(
            bank.Id,
            bank.InstitutionCode.Value,
            bank.Name.Value,
            bank.Status,
            policy.Value.Id,
            feeScheduleVersionId));
    }

    private Result<BankPolicyVersion> PublishPolicy(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CommitCreateBankCommand command,
        UtcTimestamp now)
    {
        BankPolicyVersion policy;

        try
        {
            policy = BankPolicyVersion.Create(
                BankPolicyVersionId.FromValue(idGenerator.NextId()),
                bank.Id,
                command.OpeningEnabled,
                command.MinimumCustomerAccountAgeDays,
                MoneyMinor.FromMinor(command.MinimumInitialFundingMinor),
                command.RequiresManualApproval,
                command.ReopenClosedAccountAllowed,
                command.PublicReceivingEnabledDefault,
                cashCardEnabled: false,
                debitCardEnabled: false,
                integratedCashDebitDefault: false,
                AutomaticBankCardIssueMode.None,
                cashAtmEnabled: false,
                cashCardValidityMonths: null,
                DefaultDebitCardValidityMonths,
                perTransferLimit: null,
                dailyOutgoingLimit: null,
                maximumActiveHolds: null,
                now,
                effectiveTo: null,
                VersionedEntity.InitialVersion);
        }
        catch (InvariantViolationException)
        {
            return Result<BankPolicyVersion>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.BankPolicyInputInvalid);
        }

        unitOfWork.BankAdministration.AddBankPolicyVersion(policy);

        return Result<BankPolicyVersion>.Success(policy);
    }

    private FeeScheduleVersionId PublishFeeSchedule(IBankingUnitOfWork unitOfWork, Bank bank, UtcTimestamp now)
    {
        FeeScheduleVersionId scheduleVersionId = FeeScheduleVersionId.FromValue(idGenerator.NextId());

        unitOfWork.BankAdministration.AddFeeScheduleVersion(
            scheduleVersionId, bank.Id, now, VersionedEntity.InitialVersion);

        foreach (FeeType feeType in CatchAllFeeTypes)
        {
            unitOfWork.BankAdministration.AddFeeRule(FeeRule.Create(
                FeeRuleId.FromValue(idGenerator.NextId()),
                scheduleVersionId,
                feeType,
                priority: 0,
                FeeChannel.Any,
                accountProductId: null,
                atmNetworkId: null,
                counterpartyBankId: null,
                MoneyMinor.Zero,
                amountMaximum: null,
                FeeRuleDayClass.Any,
                localStartMinute: null,
                localEndMinute: null,
                MoneyMinor.Zero,
                basisPoints: 0,
                MoneyMinor.Zero,
                maximumAmount: null,
                waiverCounterKey: null,
                freeOccurrencesPerBusinessMonth: 0));
        }

        return scheduleVersionId;
    }

    private void CreateRequiredLedgerAccounts(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CurrencyId currencyId,
        SettlementParticipationMode mode)
    {
        unitOfWork.LedgerAccounts.Add(LedgerAccount.CreateControl(
            LedgerAccountId.FromValue(idGenerator.NextId()),
            bank.GeneralLedgerBookId,
            parentAccountId: null,
            ControlAccountCode,
            LedgerAccountKind.DemandDepositControl,
            currencyId));

        AddPosting(unitOfWork, bank, currencyId, FeeRevenueAccountCode, LedgerAccountKind.FeeRevenue);
        AddPosting(unitOfWork, bank, currencyId, SettlementPayableAccountCode, LedgerAccountKind.SettlementPayable);
        AddPosting(
            unitOfWork, bank, currencyId, SuspenseAccountCode, LedgerAccountKind.IncomingSettlementSuspense);
        AddPosting(
            unitOfWork, bank, currencyId, ClearingReceivableAccountCode, LedgerAccountKind.ClearingReceivable);
        AddPosting(unitOfWork, bank, currencyId, ClearingPayableAccountCode, LedgerAccountKind.ClearingPayable);
        AddPosting(unitOfWork, bank, currencyId, PaidInCapitalAccountCode, LedgerAccountKind.PaidInCapital);

        if (mode == SettlementParticipationMode.Direct)
        {
            AddPosting(
                unitOfWork, bank, currencyId, ReserveAccountCode, LedgerAccountKind.CentralBankReserveAsset);
            return;
        }

        AddPosting(
            unitOfWork,
            bank,
            currencyId,
            AgentBalanceAccountCode,
            LedgerAccountKind.SettlementAgentBalanceAsset);
    }

    private LedgerAccountId AddPosting(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CurrencyId currencyId,
        string accountCode,
        LedgerAccountKind kind) =>
        AddPosting(unitOfWork, bank.GeneralLedgerBookId, currencyId, accountCode, kind,
            LedgerOwnerReferenceType.None, EntityIdValue.Empty);

    private LedgerAccountId AddPosting(
        IBankingUnitOfWork unitOfWork,
        AccountingBookId bookId,
        CurrencyId currencyId,
        string accountCode,
        LedgerAccountKind kind,
        LedgerOwnerReferenceType ownerReferenceType,
        EntityIdValue ownerReferenceId)
    {
        LedgerAccountId id = LedgerAccountId.FromValue(idGenerator.NextId());

        unitOfWork.LedgerAccounts.Add(LedgerAccount.CreatePosting(
            id, bookId, null, accountCode, kind, currencyId, ownerReferenceType, ownerReferenceId));

        return id;
    }

    private Result EnrollSettlement(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CurrencyId currencyId,
        CommitCreateBankCommand command,
        UtcTimestamp now)
    {
        SettlementParticipationId participationId =
            SettlementParticipationId.FromValue(idGenerator.NextId());

        if (command.SettlementMode == SettlementParticipationMode.Indirect)
        {
            if (command.SettlementAgentInstitutionCode is not { } agentCode ||
                !InstitutionCode.TryParse(agentCode, out InstitutionCode parsedAgent) ||
                unitOfWork.Banks.FindByInstitutionCode(bank.EconomyScopeId, parsedAgent.Value) is not { } agent)
            {
                return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.SettlementAgentBankNotFound);
            }

            AddPosting(
                unitOfWork,
                agent.GeneralLedgerBookId,
                currencyId,
                ClientBankDepositPrefix + bank.InstitutionCode.Value,
                LedgerAccountKind.ClientBankSettlementDeposit,
                LedgerOwnerReferenceType.Bank,
                bank.Id.Value);

            unitOfWork.BankAdministration.AddSettlementParticipation(SettlementParticipation.Enroll(
                participationId,
                bank.Id,
                SettlementParticipationMode.Indirect,
                agent.Id,
                centralBankSettlementAccountId: null,
                now));

            return Result.Success();
        }

        if (command.CentralBankAccountingBookId is not { } centralBankBookId)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CentralBankBookUnavailable);
        }

        LedgerAccountId liabilityId = AddPosting(
            unitOfWork,
            centralBankBookId,
            currencyId,
            CentralBankLiabilityPrefix + bank.InstitutionCode.Value,
            LedgerAccountKind.CentralBankSettlementLiability,
            LedgerOwnerReferenceType.Bank,
            bank.Id.Value);

        CentralBankSettlementAccountId settlementAccountId =
            CentralBankSettlementAccountId.FromValue(idGenerator.NextId());

        unitOfWork.BankAdministration.AddCentralBankSettlementAccount(
            settlementAccountId, bank.Id, currencyId, liabilityId, now);

        unitOfWork.BankAdministration.AddSettlementParticipation(SettlementParticipation.Enroll(
            participationId,
            bank.Id,
            SettlementParticipationMode.Direct,
            settlementAgentBankId: null,
            settlementAccountId,
            now));

        return Result.Success();
    }

    private Result<AccountOpeningApplicationView> Approve(
        IBankingUnitOfWork unitOfWork,
        ApproveAccountOpeningCommand command)
    {
        Result<AccountOpeningApplication> loaded = LoadSubmitted(
            unitOfWork, command.Actor, command.AccountOpeningApplicationId);

        if (!loaded.IsSuccess)
        {
            return Result<AccountOpeningApplicationView>.Failure(loaded.Error!);
        }

        AccountOpeningApplication application = loaded.Value;

        if (unitOfWork.Banks.Find(application.BankId) is not { } bank || !bank.AcceptsAccountOpening)
        {
            return Result<AccountOpeningApplicationView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        if (unitOfWork.CustomerAccounts.Find(application.CustomerAccountId) is not { } customer ||
            customer.Status != CustomerAccountStatus.Active)
        {
            return Result<AccountOpeningApplicationView>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.CustomerAccountNotOperable);
        }

        if (unitOfWork.BankAdministration.FindBankPolicyVersion(application.PolicyVersionId) is not { } policy ||
            !policy.OpeningEnabled)
        {
            return Result<AccountOpeningApplicationView>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.AccountOpeningDisabled);
        }

        if (unitOfWork.AccountProducts.FindDefault(bank.Id) is not { } product ||
            product.ProductVersionId != application.ProductVersionId)
        {
            return Result<AccountOpeningApplicationView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountProductUnavailable);
        }

        if (unitOfWork.LedgerAccounts.FindByCode(bank.GeneralLedgerBookId, ControlAccountCode)
            is not { } control)
        {
            return Result<AccountOpeningApplicationView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        if (unitOfWork.DepositAccounts.FindByCustomer(bank.Id, customer.Id) is not null)
        {
            return Result<AccountOpeningApplicationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositAccountAlreadyExists);
        }

        UtcTimestamp now = clock.Now();
        AccountOpeningContract contract = new(
            policy,
            application.FeeScheduleVersionId,
            application.OpeningFee,
            application.CashCardIssueFee,
            application.DebitCardIssueFee,
            application.RequiredFunding);

        application.Approve(now, command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture));

        Result<AccountOpeningOutcome> advanced = AccountOpeningWorkflow.Advance(
            unitOfWork,
            idGenerator,
            bank,
            customer,
            product,
            control,
            contract,
            application,
            control.CurrencyId,
            now);

        if (!advanced.IsSuccess)
        {
            return Result<AccountOpeningApplicationView>.Failure(advanced.Error!);
        }

        unitOfWork.BankAdministration.UpdateOpeningApplication(application);

        Record(unitOfWork, command.Actor, application, ApproveOperationType, ApprovedEventType, null, now);

        return Result<AccountOpeningApplicationView>.Success(ToView(application));
    }

    private Result<AccountOpeningApplicationView> Reject(
        IBankingUnitOfWork unitOfWork,
        RejectAccountOpeningCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ReasonCode))
        {
            return Result<AccountOpeningApplicationView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.BankIdentityInvalid, nameof(command.ReasonCode));
        }

        Result<AccountOpeningApplication> loaded = LoadSubmitted(
            unitOfWork, command.Actor, command.AccountOpeningApplicationId);

        if (!loaded.IsSuccess)
        {
            return Result<AccountOpeningApplicationView>.Failure(loaded.Error!);
        }

        AccountOpeningApplication application = loaded.Value;
        UtcTimestamp now = clock.Now();

        application.Reject(now, command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture));
        unitOfWork.BankAdministration.UpdateOpeningApplication(application);

        Record(
            unitOfWork,
            command.Actor,
            application,
            RejectOperationType,
            RejectedEventType,
            command.ReasonCode,
            now);

        return Result<AccountOpeningApplicationView>.Success(ToView(application));
    }

    private void Record(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        AccountOpeningApplication application,
        string operationType,
        string eventType,
        string? reason,
        UtcTimestamp now)
    {
        Bank? bank = unitOfWork.Banks.Find(application.BankId);

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            operationType,
            bank!.EconomyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            IdempotencyKey.Create(operationType, application.Id.Value.ToString()),
            now);

        unitOfWork.BusinessOperations.Add(operation);
        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            operation.Id,
            actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            operationType,
            "account_opening_application",
            application.Id.Value,
            reason,
            now);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            eventType,
            $$"""{"account_opening_application_id":"{{application.Id.Value}}"}""",
            now));
    }

    private static Result<AccountOpeningApplication> LoadSubmitted(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        AccountOpeningApplicationId applicationId)
    {
        if (unitOfWork.BankAdministration.FindOpeningApplication(applicationId) is not { } application)
        {
            return Result<AccountOpeningApplication>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AccountOpeningApplicationNotFound);
        }

        if (unitOfWork.Banks.Find(application.BankId) is not { } bank)
        {
            return Result<AccountOpeningApplication>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(unitOfWork, actor, bank.EconomyScopeId);
        if (!authorized.IsSuccess)
        {
            return Result<AccountOpeningApplication>.Failure(authorized.Error!);
        }

        return application.Status == AccountOpeningApplicationStatus.Submitted
            ? Result<AccountOpeningApplication>.Success(application)
            : Result<AccountOpeningApplication>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AccountOpeningApplicationNotSubmitted);
    }

    private static AccountOpeningApplicationView ToView(AccountOpeningApplication application) =>
        new(
            application.Id,
            application.BankId,
            application.CustomerAccountId,
            application.Status,
            application.RequiredFunding,
            application.DepositAccountId);
}
