namespace Numera.Domain.Banking;

public sealed record BankPolicyVersion(
    BankPolicyVersionId Id,
    BankId BankId,
    bool OpeningEnabled,
    int MinimumCustomerAccountAgeDays,
    MoneyMinor MinimumInitialFunding,
    bool RequiresManualApproval,
    bool ReopenClosedAccountAllowed,
    bool PublicReceivingEnabledDefault,
    bool CashCardEnabled,
    bool DebitCardEnabled,
    bool IntegratedCashDebitDefault,
    AutomaticBankCardIssueMode AutomaticBankCardIssueMode,
    bool CashAtmEnabled,
    int? CashCardValidityMonths,
    int DebitCardValidityMonths,
    MoneyMinor? PerTransferLimit,
    MoneyMinor? DailyOutgoingLimit,
    MoneyMinor? MaximumActiveHolds,
    UtcTimestamp EffectiveFrom,
    UtcTimestamp? EffectiveTo,
    long Version)
{
    public const int MinimumValidityMonths = 1;
    public const int MaximumValidityMonths = 120;

    public static BankPolicyVersion Create(
        BankPolicyVersionId id,
        BankId bankId,
        bool openingEnabled,
        int minimumCustomerAccountAgeDays,
        MoneyMinor minimumInitialFunding,
        bool requiresManualApproval,
        bool reopenClosedAccountAllowed,
        bool publicReceivingEnabledDefault,
        bool cashCardEnabled,
        bool debitCardEnabled,
        bool integratedCashDebitDefault,
        AutomaticBankCardIssueMode automaticBankCardIssueMode,
        bool cashAtmEnabled,
        int? cashCardValidityMonths,
        int debitCardValidityMonths,
        MoneyMinor? perTransferLimit,
        MoneyMinor? dailyOutgoingLimit,
        MoneyMinor? maximumActiveHolds,
        UtcTimestamp effectiveFrom,
        UtcTimestamp? effectiveTo,
        long version)
    {
        if (minimumCustomerAccountAgeDays < 0 || minimumInitialFunding.IsNegative || version < 1)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BankPolicyVersionInconsistent);
        }

        EnsureValidityMonths(cashCardValidityMonths);
        EnsureValidityMonths(debitCardValidityMonths);
        EnsureCardModeSupported(automaticBankCardIssueMode, cashCardEnabled, debitCardEnabled);
        EnsureLimitNotNegative(perTransferLimit);
        EnsureLimitNotNegative(dailyOutgoingLimit);
        EnsureLimitNotNegative(maximumActiveHolds);

        if (effectiveTo is { } bound && bound <= effectiveFrom)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BankPolicyVersionInconsistent);
        }

        return new BankPolicyVersion(
            id,
            bankId,
            openingEnabled,
            minimumCustomerAccountAgeDays,
            minimumInitialFunding,
            requiresManualApproval,
            reopenClosedAccountAllowed,
            publicReceivingEnabledDefault,
            cashCardEnabled,
            debitCardEnabled,
            integratedCashDebitDefault,
            automaticBankCardIssueMode,
            cashAtmEnabled,
            cashCardValidityMonths,
            debitCardValidityMonths,
            perTransferLimit,
            dailyOutgoingLimit,
            maximumActiveHolds,
            effectiveFrom,
            effectiveTo,
            version);
    }

    public AccountOpeningDecisionMode DecisionMode => RequiresManualApproval
        ? AccountOpeningDecisionMode.Manual
        : AccountOpeningDecisionMode.Automatic;

    public bool IssuesCashCard => AutomaticBankCardIssueMode is AutomaticBankCardIssueMode.CashOnly
        or AutomaticBankCardIssueMode.IntegratedCashDebit;

    public bool IssuesDebitCard =>
        AutomaticBankCardIssueMode == AutomaticBankCardIssueMode.IntegratedCashDebit;

    private static void EnsureValidityMonths(int? months)
    {
        if (months is { } value && value is < MinimumValidityMonths or > MaximumValidityMonths)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BankPolicyVersionInconsistent);
        }
    }

    private static void EnsureLimitNotNegative(MoneyMinor? limit)
    {
        if (limit is { IsNegative: true })
        {
            throw InvariantViolationException.Create(InvariantViolationCode.LimitValueNegative);
        }
    }

    private static void EnsureCardModeSupported(
        AutomaticBankCardIssueMode mode,
        bool cashCardEnabled,
        bool debitCardEnabled)
    {
        bool supported = mode switch
        {
            AutomaticBankCardIssueMode.None => true,
            AutomaticBankCardIssueMode.CashOnly => cashCardEnabled,
            AutomaticBankCardIssueMode.IntegratedCashDebit => cashCardEnabled && debitCardEnabled,
            _ => throw InvariantViolationException.Create(InvariantViolationCode.AutomaticBankCardIssueModeUnknown),
        };

        if (!supported)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BankPolicyVersionInconsistent);
        }
    }
}

public readonly record struct PrudentialPolicyVersion(
    PrudentialPolicyVersionId Id,
    EconomyScopeId EconomyScopeId,
    int MinimumCet1BasisPoints,
    int LendingCet1BasisPoints,
    int MinimumLeverageBasisPoints,
    int ConfiguredWarningLeverageBasisPoints,
    int MinimumLiquidityBasisPoints,
    MoneyMinor MinimumInitialBankCapital,
    long Version)
{
    public const int MinimumCet1Floor = 450;
    public const int LendingCet1Floor = 700;
    public const int LeverageFloor = 300;
    public const int LiquidityFloor = 10000;

    public static PrudentialPolicyVersion Create(
        PrudentialPolicyVersionId id,
        EconomyScopeId economyScopeId,
        int minimumCet1BasisPoints,
        int lendingCet1BasisPoints,
        int minimumLeverageBasisPoints,
        int configuredWarningLeverageBasisPoints,
        int minimumLiquidityBasisPoints,
        MoneyMinor minimumInitialBankCapital,
        long version)
    {
        bool valid = minimumCet1BasisPoints >= MinimumCet1Floor &&
            lendingCet1BasisPoints >= LendingCet1Floor &&
            lendingCet1BasisPoints >= minimumCet1BasisPoints &&
            minimumLeverageBasisPoints >= LeverageFloor &&
            configuredWarningLeverageBasisPoints >= minimumLeverageBasisPoints &&
            minimumLiquidityBasisPoints >= LiquidityFloor &&
            minimumInitialBankCapital.IsPositive &&
            version >= 1;

        return valid
            ? new PrudentialPolicyVersion(
                id,
                economyScopeId,
                minimumCet1BasisPoints,
                lendingCet1BasisPoints,
                minimumLeverageBasisPoints,
                configuredWarningLeverageBasisPoints,
                minimumLiquidityBasisPoints,
                minimumInitialBankCapital,
                version)
            : throw InvariantViolationException.Create(InvariantViolationCode.PrudentialPolicyInconsistent);
    }
}
