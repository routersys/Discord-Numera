namespace Numera.Domain.Banking;

public enum AccountOpeningApplicationStatus
{
    Submitted = 1,
    Approved = 2,
    AwaitingFunding = 3,
    ReadyToActivate = 4,
    Completed = 5,
    Rejected = 6,
    Cancelled = 7,
    Failed = 8,
}

public enum AccountOpeningDecisionMode
{
    Automatic = 1,
    Manual = 2,
}

public enum AutomaticBankCardIssueMode
{
    None = 1,
    CashOnly = 2,
    IntegratedCashDebit = 3,
}

public sealed class AccountOpeningApplication : VersionedEntity
{
    private static readonly StateTransitionTable<AccountOpeningApplicationStatus> Transitions =
        StateTransitionTable<AccountOpeningApplicationStatus>
            .Create(InvariantViolationCode.AccountOpeningApplicationTransitionInvalid)
            .AllowCreation(AccountOpeningApplicationStatus.Submitted)
            .Allow(
                AccountOpeningApplicationStatus.Submitted,
                AccountOpeningApplicationStatus.Approved,
                AccountOpeningApplicationStatus.Rejected,
                AccountOpeningApplicationStatus.Cancelled)
            .Allow(
                AccountOpeningApplicationStatus.Approved,
                AccountOpeningApplicationStatus.AwaitingFunding,
                AccountOpeningApplicationStatus.ReadyToActivate,
                AccountOpeningApplicationStatus.Cancelled)
            .Allow(
                AccountOpeningApplicationStatus.AwaitingFunding,
                AccountOpeningApplicationStatus.ReadyToActivate,
                AccountOpeningApplicationStatus.Failed,
                AccountOpeningApplicationStatus.Cancelled)
            .Allow(
                AccountOpeningApplicationStatus.ReadyToActivate,
                AccountOpeningApplicationStatus.Completed)
            .Build();

    private AccountOpeningApplication(
        AccountOpeningApplicationId id,
        BankId bankId,
        CustomerAccountId customerAccountId,
        AccountProductVersionId productVersionId,
        BankPolicyVersionId policyVersionId,
        FeeScheduleVersionId feeScheduleVersionId,
        DepositAccountId? depositAccountId,
        DepositAccountId? fundingSourceDepositAccountId,
        PaymentOrderId? fundingPaymentOrderId,
        MoneyMinor minimumInitialFunding,
        MoneyMinor openingFee,
        MoneyMinor cashCardIssueFee,
        MoneyMinor debitCardIssueFee,
        MoneyMinor requiredFunding,
        AutomaticBankCardIssueMode automaticBankCardIssueMode,
        AccountOpeningDecisionMode decisionMode,
        AccountOpeningApplicationStatus status,
        UtcTimestamp submittedAt,
        UtcTimestamp? decidedAt,
        string? decidedByDiscordUserId,
        UtcTimestamp? completedAt,
        long version)
        : base(version)
    {
        Id = id;
        BankId = bankId;
        CustomerAccountId = customerAccountId;
        ProductVersionId = productVersionId;
        PolicyVersionId = policyVersionId;
        FeeScheduleVersionId = feeScheduleVersionId;
        DepositAccountId = depositAccountId;
        FundingSourceDepositAccountId = fundingSourceDepositAccountId;
        FundingPaymentOrderId = fundingPaymentOrderId;
        MinimumInitialFunding = minimumInitialFunding;
        OpeningFee = openingFee;
        CashCardIssueFee = cashCardIssueFee;
        DebitCardIssueFee = debitCardIssueFee;
        RequiredFunding = requiredFunding;
        AutomaticBankCardIssueMode = automaticBankCardIssueMode;
        DecisionMode = decisionMode;
        Status = status;
        SubmittedAt = submittedAt;
        DecidedAt = decidedAt;
        DecidedByDiscordUserId = decidedByDiscordUserId;
        CompletedAt = completedAt;
    }

    public AccountOpeningApplicationId Id { get; }

    public BankId BankId { get; }

    public CustomerAccountId CustomerAccountId { get; }

    public AccountProductVersionId ProductVersionId { get; }

    public BankPolicyVersionId PolicyVersionId { get; }

    public FeeScheduleVersionId FeeScheduleVersionId { get; }

    public DepositAccountId? DepositAccountId { get; private set; }

    public DepositAccountId? FundingSourceDepositAccountId { get; private set; }

    public PaymentOrderId? FundingPaymentOrderId { get; private set; }

    public MoneyMinor MinimumInitialFunding { get; }

    public MoneyMinor OpeningFee { get; }

    public MoneyMinor CashCardIssueFee { get; }

    public MoneyMinor DebitCardIssueFee { get; }

    public MoneyMinor RequiredFunding { get; }

    public AutomaticBankCardIssueMode AutomaticBankCardIssueMode { get; }

    public AccountOpeningDecisionMode DecisionMode { get; }

    public AccountOpeningApplicationStatus Status { get; private set; }

    public UtcTimestamp SubmittedAt { get; }

    public UtcTimestamp? DecidedAt { get; private set; }

    public string? DecidedByDiscordUserId { get; private set; }

    public UtcTimestamp? CompletedAt { get; private set; }

    public bool IsPending => Status is AccountOpeningApplicationStatus.Submitted
        or AccountOpeningApplicationStatus.Approved
        or AccountOpeningApplicationStatus.AwaitingFunding
        or AccountOpeningApplicationStatus.ReadyToActivate;

    public static MoneyMinor CalculateRequiredFunding(
        MoneyMinor minimumInitialFunding,
        MoneyMinor openingFee,
        MoneyMinor cashCardIssueFee,
        MoneyMinor debitCardIssueFee)
    {
        if (minimumInitialFunding.IsNegative ||
            openingFee.IsNegative ||
            cashCardIssueFee.IsNegative ||
            debitCardIssueFee.IsNegative)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.AccountOpeningFundingInconsistent);
        }

        Int128 total = checked(
            minimumInitialFunding.Intermediate +
            openingFee.Intermediate +
            cashCardIssueFee.Intermediate +
            debitCardIssueFee.Intermediate);

        return MoneyMinor.FromIntermediate(total);
    }

    public static AccountOpeningApplication Submit(
        AccountOpeningApplicationId id,
        BankId bankId,
        CustomerAccountId customerAccountId,
        AccountProductVersionId productVersionId,
        BankPolicyVersionId policyVersionId,
        FeeScheduleVersionId feeScheduleVersionId,
        MoneyMinor minimumInitialFunding,
        MoneyMinor openingFee,
        MoneyMinor cashCardIssueFee,
        MoneyMinor debitCardIssueFee,
        AutomaticBankCardIssueMode automaticBankCardIssueMode,
        AccountOpeningDecisionMode decisionMode,
        UtcTimestamp submittedAt)
    {
        Transitions.EnsureCreatable(AccountOpeningApplicationStatus.Submitted);
        EnsureCardFeesConsistent(automaticBankCardIssueMode, cashCardIssueFee, debitCardIssueFee);

        return new AccountOpeningApplication(
            id,
            bankId,
            customerAccountId,
            productVersionId,
            policyVersionId,
            feeScheduleVersionId,
            depositAccountId: null,
            fundingSourceDepositAccountId: null,
            fundingPaymentOrderId: null,
            minimumInitialFunding,
            openingFee,
            cashCardIssueFee,
            debitCardIssueFee,
            CalculateRequiredFunding(minimumInitialFunding, openingFee, cashCardIssueFee, debitCardIssueFee),
            automaticBankCardIssueMode,
            decisionMode,
            AccountOpeningApplicationStatus.Submitted,
            submittedAt,
            decidedAt: null,
            decidedByDiscordUserId: null,
            completedAt: null,
            InitialVersion);
    }

    public static AccountOpeningApplication Rehydrate(
        AccountOpeningApplicationId id,
        BankId bankId,
        CustomerAccountId customerAccountId,
        AccountProductVersionId productVersionId,
        BankPolicyVersionId policyVersionId,
        FeeScheduleVersionId feeScheduleVersionId,
        DepositAccountId? depositAccountId,
        DepositAccountId? fundingSourceDepositAccountId,
        PaymentOrderId? fundingPaymentOrderId,
        MoneyMinor minimumInitialFunding,
        MoneyMinor openingFee,
        MoneyMinor cashCardIssueFee,
        MoneyMinor debitCardIssueFee,
        MoneyMinor requiredFunding,
        AutomaticBankCardIssueMode automaticBankCardIssueMode,
        AccountOpeningDecisionMode decisionMode,
        AccountOpeningApplicationStatus status,
        UtcTimestamp submittedAt,
        UtcTimestamp? decidedAt,
        string? decidedByDiscordUserId,
        UtcTimestamp? completedAt,
        long version)
    {
        EnsureCardFeesConsistent(automaticBankCardIssueMode, cashCardIssueFee, debitCardIssueFee);

        if (requiredFunding != CalculateRequiredFunding(
                minimumInitialFunding, openingFee, cashCardIssueFee, debitCardIssueFee))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.AccountOpeningFundingInconsistent);
        }

        return new AccountOpeningApplication(
            id,
            bankId,
            customerAccountId,
            productVersionId,
            policyVersionId,
            feeScheduleVersionId,
            depositAccountId,
            fundingSourceDepositAccountId,
            fundingPaymentOrderId,
            minimumInitialFunding,
            openingFee,
            cashCardIssueFee,
            debitCardIssueFee,
            requiredFunding,
            automaticBankCardIssueMode,
            decisionMode,
            status,
            submittedAt,
            decidedAt,
            decidedByDiscordUserId,
            completedAt,
            version);
    }

    public void Approve(UtcTimestamp decidedAt, string? decidedByDiscordUserId)
    {
        Advance(AccountOpeningApplicationStatus.Approved);
        DecidedAt = decidedAt;
        DecidedByDiscordUserId = decidedByDiscordUserId;
    }

    public void Reject(UtcTimestamp decidedAt, string? decidedByDiscordUserId)
    {
        Advance(AccountOpeningApplicationStatus.Rejected);
        DecidedAt = decidedAt;
        DecidedByDiscordUserId = decidedByDiscordUserId;
    }

    public void Cancel(bool fundingPosted)
    {
        if (fundingPosted)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.AccountOpeningApplicationTransitionInvalid);
        }

        Advance(AccountOpeningApplicationStatus.Cancelled);
    }

    public void AwaitFunding(DepositAccountId depositAccountId, DepositAccountId fundingSourceDepositAccountId)
    {
        DepositAccountId = depositAccountId;
        FundingSourceDepositAccountId = fundingSourceDepositAccountId;
        Advance(AccountOpeningApplicationStatus.AwaitingFunding);
    }

    public void AttachFundingPayment(PaymentOrderId fundingPaymentOrderId)
    {
        if (Status != AccountOpeningApplicationStatus.AwaitingFunding || FundingPaymentOrderId is not null)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.AccountOpeningApplicationTransitionInvalid);
        }

        FundingPaymentOrderId = fundingPaymentOrderId;
    }

    public void MarkFunded()
    {
        if (FundingPaymentOrderId is null)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.AccountOpeningFundingInconsistent);
        }

        Advance(AccountOpeningApplicationStatus.ReadyToActivate);
    }

    public void MarkReadyToActivate(DepositAccountId depositAccountId)
    {
        DepositAccountId = depositAccountId;
        Advance(AccountOpeningApplicationStatus.ReadyToActivate);
    }

    public void Fail() => Advance(AccountOpeningApplicationStatus.Failed);

    public void Complete(UtcTimestamp completedAt)
    {
        if (DepositAccountId is null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.AccountOpeningDepositAccountMissing);
        }

        Advance(AccountOpeningApplicationStatus.Completed);
        CompletedAt = completedAt;
    }

    private void Advance(AccountOpeningApplicationStatus target)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        AdvanceVersion();
    }

    private static void EnsureCardFeesConsistent(
        AutomaticBankCardIssueMode mode,
        MoneyMinor cashCardIssueFee,
        MoneyMinor debitCardIssueFee)
    {
        bool consistent = mode switch
        {
            AutomaticBankCardIssueMode.None => cashCardIssueFee.IsZero && debitCardIssueFee.IsZero,
            AutomaticBankCardIssueMode.CashOnly => debitCardIssueFee.IsZero,
            AutomaticBankCardIssueMode.IntegratedCashDebit => true,
            _ => throw InvariantViolationException.Create(InvariantViolationCode.AutomaticBankCardIssueModeUnknown),
        };

        if (!consistent)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.AccountOpeningCardFeeInconsistent);
        }
    }
}

public static class AccountOpeningApplicationCatalog
{
    public static string ToToken(this AccountOpeningApplicationStatus status) => status switch
    {
        AccountOpeningApplicationStatus.Submitted => "SUBMITTED",
        AccountOpeningApplicationStatus.Approved => "APPROVED",
        AccountOpeningApplicationStatus.AwaitingFunding => "AWAITING_FUNDING",
        AccountOpeningApplicationStatus.ReadyToActivate => "READY_TO_ACTIVATE",
        AccountOpeningApplicationStatus.Completed => "COMPLETED",
        AccountOpeningApplicationStatus.Rejected => "REJECTED",
        AccountOpeningApplicationStatus.Cancelled => "CANCELLED",
        AccountOpeningApplicationStatus.Failed => "FAILED",
        _ => throw InvariantViolationException.Create(
            InvariantViolationCode.AccountOpeningApplicationStatusUnknown),
    };

    public static bool TryParseStatusToken(ReadOnlySpan<char> token, out AccountOpeningApplicationStatus status)
    {
        switch (token)
        {
            case "SUBMITTED":
                status = AccountOpeningApplicationStatus.Submitted;
                return true;
            case "APPROVED":
                status = AccountOpeningApplicationStatus.Approved;
                return true;
            case "AWAITING_FUNDING":
                status = AccountOpeningApplicationStatus.AwaitingFunding;
                return true;
            case "READY_TO_ACTIVATE":
                status = AccountOpeningApplicationStatus.ReadyToActivate;
                return true;
            case "COMPLETED":
                status = AccountOpeningApplicationStatus.Completed;
                return true;
            case "REJECTED":
                status = AccountOpeningApplicationStatus.Rejected;
                return true;
            case "CANCELLED":
                status = AccountOpeningApplicationStatus.Cancelled;
                return true;
            case "FAILED":
                status = AccountOpeningApplicationStatus.Failed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AccountOpeningApplicationStatus ParseStatusToken(ReadOnlySpan<char> token) =>
        TryParseStatusToken(token, out AccountOpeningApplicationStatus status)
            ? status
            : throw InvariantViolationException.Create(
                InvariantViolationCode.AccountOpeningApplicationStatusUnknown);

    public static string ToToken(this AccountOpeningDecisionMode mode) => mode switch
    {
        AccountOpeningDecisionMode.Automatic => "AUTOMATIC",
        AccountOpeningDecisionMode.Manual => "MANUAL",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.AccountOpeningDecisionModeUnknown),
    };

    public static bool TryParseDecisionModeToken(ReadOnlySpan<char> token, out AccountOpeningDecisionMode mode)
    {
        switch (token)
        {
            case "AUTOMATIC":
                mode = AccountOpeningDecisionMode.Automatic;
                return true;
            case "MANUAL":
                mode = AccountOpeningDecisionMode.Manual;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static AccountOpeningDecisionMode ParseDecisionModeToken(ReadOnlySpan<char> token) =>
        TryParseDecisionModeToken(token, out AccountOpeningDecisionMode mode)
            ? mode
            : throw InvariantViolationException.Create(InvariantViolationCode.AccountOpeningDecisionModeUnknown);

    public static string ToToken(this AutomaticBankCardIssueMode mode) => mode switch
    {
        AutomaticBankCardIssueMode.None => "NONE",
        AutomaticBankCardIssueMode.CashOnly => "CASH_ONLY",
        AutomaticBankCardIssueMode.IntegratedCashDebit => "INTEGRATED_CASH_DEBIT",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.AutomaticBankCardIssueModeUnknown),
    };

    public static bool TryParseCardIssueModeToken(ReadOnlySpan<char> token, out AutomaticBankCardIssueMode mode)
    {
        switch (token)
        {
            case "NONE":
                mode = AutomaticBankCardIssueMode.None;
                return true;
            case "CASH_ONLY":
                mode = AutomaticBankCardIssueMode.CashOnly;
                return true;
            case "INTEGRATED_CASH_DEBIT":
                mode = AutomaticBankCardIssueMode.IntegratedCashDebit;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static AutomaticBankCardIssueMode ParseCardIssueModeToken(ReadOnlySpan<char> token) =>
        TryParseCardIssueModeToken(token, out AutomaticBankCardIssueMode mode)
            ? mode
            : throw InvariantViolationException.Create(InvariantViolationCode.AutomaticBankCardIssueModeUnknown);
}
