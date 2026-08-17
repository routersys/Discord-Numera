namespace Numera.Domain.Banking;

public enum PaymentNetworkStatus
{
    Draft = 1,
    Active = 2,
    Suspended = 3,
    Retired = 4,
}

public readonly record struct PaymentNetworkPolicyVersion(
    PaymentNetworkPolicyVersionId Id,
    PaymentNetworkId PaymentNetworkId,
    SettlementMode SettlementMode,
    BeneficiaryPostingPolicy BeneficiaryPostingPolicy,
    MoneyMinor? RtgsThreshold,
    int? ClearingCycleIntervalSeconds,
    bool PrecreditEnabled,
    int PrecreditPrefundRatioBasisPoints,
    MoneyMinor PerBankPrecreditExposureLimit,
    UtcTimestamp CreatedAt,
    long Version)
{
    public const int MinimumPrefundRatioBasisPoints = 10000;
    public const int MinimumClearingCycleIntervalSeconds = 60;
    public const int MaximumClearingCycleIntervalSeconds = 86400;

    public static PaymentNetworkPolicyVersion Create(
        PaymentNetworkPolicyVersionId id,
        PaymentNetworkId paymentNetworkId,
        SettlementMode settlementMode,
        BeneficiaryPostingPolicy beneficiaryPostingPolicy,
        MoneyMinor? rtgsThreshold,
        int? clearingCycleIntervalSeconds,
        bool precreditEnabled,
        int precreditPrefundRatioBasisPoints,
        MoneyMinor perBankPrecreditExposureLimit,
        UtcTimestamp createdAt,
        long version)
    {
        EnsureSettlementModeSupported(settlementMode);
        EnsurePostingPolicyConsistent(settlementMode, beneficiaryPostingPolicy, precreditEnabled);
        EnsureIntervalValid(settlementMode, clearingCycleIntervalSeconds);

        if (precreditPrefundRatioBasisPoints < MinimumPrefundRatioBasisPoints)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkPolicyPrefundRatioInvalid);
        }

        if (rtgsThreshold is { IsNegative: true } || perBankPrecreditExposureLimit.IsNegative || version < 1)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkPolicyInconsistent);
        }

        return new PaymentNetworkPolicyVersion(
            id,
            paymentNetworkId,
            settlementMode,
            beneficiaryPostingPolicy,
            rtgsThreshold,
            clearingCycleIntervalSeconds,
            precreditEnabled,
            precreditPrefundRatioBasisPoints,
            perBankPrecreditExposureLimit,
            createdAt,
            version);
    }

    public SettlementMode ResolveSettlementMode(MoneyMinor amount) =>
        SettlementMode == SettlementMode.Clearing && RtgsThreshold is { } threshold && amount.Value >= threshold.Value
            ? SettlementMode.Rtgs
            : SettlementMode;

    public MoneyMinor RequiredPrefund(MoneyMinor amount)
    {
        Int128 scaled = checked(amount.Intermediate * PrecreditPrefundRatioBasisPoints);
        Int128 divisor = MinimumPrefundRatioBasisPoints;
        Int128 quotient = scaled / divisor;

        return MoneyMinor.FromIntermediate(scaled % divisor == Int128.Zero ? quotient : checked(quotient + 1));
    }

    private static void EnsureSettlementModeSupported(SettlementMode settlementMode)
    {
        if (settlementMode is not (SettlementMode.Rtgs or SettlementMode.Clearing))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkPolicyInconsistent);
        }
    }

    private static void EnsurePostingPolicyConsistent(
        SettlementMode settlementMode,
        BeneficiaryPostingPolicy beneficiaryPostingPolicy,
        bool precreditEnabled)
    {
        bool guaranteed = beneficiaryPostingPolicy == BeneficiaryPostingPolicy.GuaranteedPreCredit &&
            settlementMode == SettlementMode.Clearing &&
            precreditEnabled;

        bool afterFinal = beneficiaryPostingPolicy == BeneficiaryPostingPolicy.AfterFinalSettlement &&
            !precreditEnabled;

        if (!guaranteed && !afterFinal)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkPolicyInconsistent);
        }
    }

    private static void EnsureIntervalValid(SettlementMode settlementMode, int? clearingCycleIntervalSeconds)
    {
        if (settlementMode == SettlementMode.Clearing && clearingCycleIntervalSeconds is null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkPolicyIntervalInvalid);
        }

        if (clearingCycleIntervalSeconds is { } interval &&
            interval is < MinimumClearingCycleIntervalSeconds or > MaximumClearingCycleIntervalSeconds)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkPolicyIntervalInvalid);
        }
    }
}

public readonly record struct PaymentNetworkPrefund(
    PaymentNetworkPrefundId Id,
    PaymentNetworkId PaymentNetworkId,
    BankId BankId,
    CurrencyId CurrencyId,
    LedgerAccountId PrefundLiabilityLedgerAccountId,
    UtcTimestamp CreatedAt,
    long Version)
{
    public static PaymentNetworkPrefund Create(
        PaymentNetworkPrefundId id,
        PaymentNetworkId paymentNetworkId,
        BankId bankId,
        CurrencyId currencyId,
        LedgerAccountId prefundLiabilityLedgerAccountId,
        UtcTimestamp createdAt,
        long version) =>
        version < 1
            ? throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkPolicyInconsistent)
            : new PaymentNetworkPrefund(
                id, paymentNetworkId, bankId, currencyId, prefundLiabilityLedgerAccountId, createdAt, version);

    public MoneyMinor AvailableAmount(MoneyMinor postedBalance, MoneyMinor unfinalisedExposure) =>
        postedBalance.Subtract(unfinalisedExposure) is { IsNegative: false } available
            ? available
            : MoneyMinor.Zero;
}

public sealed class PaymentNetwork : VersionedEntity
{
    public const int MaximumNetworkCodeLength = 32;

    private static readonly StateTransitionTable<PaymentNetworkStatus> Transitions =
        StateTransitionTable<PaymentNetworkStatus>
            .Create(InvariantViolationCode.PaymentNetworkTransitionInvalid)
            .AllowCreation(PaymentNetworkStatus.Draft)
            .Allow(PaymentNetworkStatus.Draft, PaymentNetworkStatus.Active, PaymentNetworkStatus.Retired)
            .Allow(PaymentNetworkStatus.Active, PaymentNetworkStatus.Suspended, PaymentNetworkStatus.Retired)
            .Allow(PaymentNetworkStatus.Suspended, PaymentNetworkStatus.Active, PaymentNetworkStatus.Retired)
            .Build();

    private PaymentNetwork(
        PaymentNetworkId id,
        EconomyScopeId economyScopeId,
        string networkCode,
        PartyId operatorPartyId,
        AccountingBookId accountingBookId,
        LedgerAccountId liquidAssetLedgerAccountId,
        PaymentNetworkStatus status,
        PaymentNetworkPolicyVersionId? currentPolicyVersionId,
        long version)
        : base(version)
    {
        Id = id;
        EconomyScopeId = economyScopeId;
        NetworkCode = networkCode;
        OperatorPartyId = operatorPartyId;
        AccountingBookId = accountingBookId;
        LiquidAssetLedgerAccountId = liquidAssetLedgerAccountId;
        Status = status;
        CurrentPolicyVersionId = currentPolicyVersionId;
    }

    public PaymentNetworkId Id { get; }

    public EconomyScopeId EconomyScopeId { get; }

    public string NetworkCode { get; }

    public PartyId OperatorPartyId { get; }

    public AccountingBookId AccountingBookId { get; }

    public LedgerAccountId LiquidAssetLedgerAccountId { get; }

    public PaymentNetworkStatus Status { get; private set; }

    public PaymentNetworkPolicyVersionId? CurrentPolicyVersionId { get; private set; }

    public bool RoutesPayments => Status == PaymentNetworkStatus.Active && CurrentPolicyVersionId is not null;

    public static PaymentNetwork Draft(
        PaymentNetworkId id,
        EconomyScopeId economyScopeId,
        string networkCode,
        PartyId operatorPartyId,
        AccountingBookId accountingBookId,
        LedgerAccountId liquidAssetLedgerAccountId)
    {
        Transitions.EnsureCreatable(PaymentNetworkStatus.Draft);
        EnsureNetworkCodeValid(networkCode);

        return new PaymentNetwork(
            id,
            economyScopeId,
            networkCode,
            operatorPartyId,
            accountingBookId,
            liquidAssetLedgerAccountId,
            PaymentNetworkStatus.Draft,
            currentPolicyVersionId: null,
            InitialVersion);
    }

    public static PaymentNetwork Rehydrate(
        PaymentNetworkId id,
        EconomyScopeId economyScopeId,
        string networkCode,
        PartyId operatorPartyId,
        AccountingBookId accountingBookId,
        LedgerAccountId liquidAssetLedgerAccountId,
        PaymentNetworkStatus status,
        PaymentNetworkPolicyVersionId? currentPolicyVersionId,
        long version)
    {
        EnsureNetworkCodeValid(networkCode);
        EnsurePolicyReferenceConsistent(status, currentPolicyVersionId);

        return new PaymentNetwork(
            id,
            economyScopeId,
            networkCode,
            operatorPartyId,
            accountingBookId,
            liquidAssetLedgerAccountId,
            status,
            currentPolicyVersionId,
            version);
    }

    public void PublishPolicy(PaymentNetworkPolicyVersionId policyVersionId)
    {
        if (Status is PaymentNetworkStatus.Retired)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkTransitionInvalid);
        }

        CurrentPolicyVersionId = policyVersionId;

        if (Status == PaymentNetworkStatus.Draft)
        {
            Advance(PaymentNetworkStatus.Active);
            return;
        }

        AdvanceVersion();
    }

    public void Suspend() => Advance(PaymentNetworkStatus.Suspended);

    public void Resume() => Advance(PaymentNetworkStatus.Active);

    public void Retire() => Advance(PaymentNetworkStatus.Retired);

    private void Advance(PaymentNetworkStatus target)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        AdvanceVersion();
    }

    private static void EnsureNetworkCodeValid(string networkCode)
    {
        if (string.IsNullOrEmpty(networkCode) || networkCode.Length > MaximumNetworkCodeLength)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkCodeInvalid);
        }

        foreach (char character in networkCode)
        {
            if (character is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_'))
            {
                throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkCodeInvalid);
            }
        }
    }

    private static void EnsurePolicyReferenceConsistent(
        PaymentNetworkStatus status,
        PaymentNetworkPolicyVersionId? currentPolicyVersionId)
    {
        bool requiresPolicy = status is PaymentNetworkStatus.Active or PaymentNetworkStatus.Suspended;

        if (status == PaymentNetworkStatus.Draft && currentPolicyVersionId is not null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkPolicyInconsistent);
        }

        if (requiresPolicy && currentPolicyVersionId is null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkPolicyInconsistent);
        }
    }
}

public static class PaymentNetworkCatalog
{
    public static string ToToken(this PaymentNetworkStatus status) => status switch
    {
        PaymentNetworkStatus.Draft => "DRAFT",
        PaymentNetworkStatus.Active => "ACTIVE",
        PaymentNetworkStatus.Suspended => "SUSPENDED",
        PaymentNetworkStatus.Retired => "RETIRED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkStatusUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out PaymentNetworkStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = PaymentNetworkStatus.Draft;
                return true;
            case "ACTIVE":
                status = PaymentNetworkStatus.Active;
                return true;
            case "SUSPENDED":
                status = PaymentNetworkStatus.Suspended;
                return true;
            case "RETIRED":
                status = PaymentNetworkStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static PaymentNetworkStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out PaymentNetworkStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.PaymentNetworkStatusUnknown);
}
