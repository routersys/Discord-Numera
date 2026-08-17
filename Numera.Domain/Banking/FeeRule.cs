namespace Numera.Domain.Banking;

public readonly record struct FeeMatchContext(
    FeeChannel Channel,
    AccountProductId? AccountProductId,
    AtmNetworkId? AtmNetworkId,
    BankId? CounterpartyBankId,
    MoneyMinor Amount,
    BusinessDayClass DayClass,
    int LocalMinuteOfDay);

public readonly record struct FeeQuote(
    FeeScheduleVersionId ScheduleVersionId,
    FeeRuleId RuleId,
    FeeType Type,
    MoneyMinor Amount,
    string? WaiverCounterKey,
    bool WaiverApplied);

public sealed class FeeRule
{
    public const int MaximumPriority = 65535;
    public const int MaximumBasisPoints = 100000;
    public const int MaximumFreeOccurrences = 1000;
    public const int MinutesPerDay = 1440;

    private FeeRule(
        FeeRuleId id,
        FeeScheduleVersionId scheduleVersionId,
        FeeType type,
        int priority,
        FeeChannel channel,
        AccountProductId? accountProductId,
        AtmNetworkId? atmNetworkId,
        BankId? counterpartyBankId,
        MoneyMinor amountMinimum,
        MoneyMinor? amountMaximum,
        FeeRuleDayClass dayClass,
        int? localStartMinute,
        int? localEndMinute,
        MoneyMinor fixedAmount,
        int basisPoints,
        MoneyMinor minimumAmount,
        MoneyMinor? maximumAmount,
        string? waiverCounterKey,
        int freeOccurrencesPerBusinessMonth)
    {
        Id = id;
        ScheduleVersionId = scheduleVersionId;
        Type = type;
        Priority = priority;
        Channel = channel;
        AccountProductId = accountProductId;
        AtmNetworkId = atmNetworkId;
        CounterpartyBankId = counterpartyBankId;
        AmountMinimum = amountMinimum;
        AmountMaximum = amountMaximum;
        DayClass = dayClass;
        LocalStartMinute = localStartMinute;
        LocalEndMinute = localEndMinute;
        FixedAmount = fixedAmount;
        BasisPoints = basisPoints;
        MinimumAmount = minimumAmount;
        MaximumAmount = maximumAmount;
        WaiverCounterKey = waiverCounterKey;
        FreeOccurrencesPerBusinessMonth = freeOccurrencesPerBusinessMonth;
    }

    public FeeRuleId Id { get; }

    public FeeScheduleVersionId ScheduleVersionId { get; }

    public FeeType Type { get; }

    public int Priority { get; }

    public FeeChannel Channel { get; }

    public AccountProductId? AccountProductId { get; }

    public AtmNetworkId? AtmNetworkId { get; }

    public BankId? CounterpartyBankId { get; }

    public MoneyMinor AmountMinimum { get; }

    public MoneyMinor? AmountMaximum { get; }

    public FeeRuleDayClass DayClass { get; }

    public int? LocalStartMinute { get; }

    public int? LocalEndMinute { get; }

    public MoneyMinor FixedAmount { get; }

    public int BasisPoints { get; }

    public MoneyMinor MinimumAmount { get; }

    public MoneyMinor? MaximumAmount { get; }

    public string? WaiverCounterKey { get; }

    public int FreeOccurrencesPerBusinessMonth { get; }

    public bool IsCatchAll =>
        Channel == FeeChannel.Any &&
        AccountProductId is null &&
        AtmNetworkId is null &&
        CounterpartyBankId is null &&
        AmountMinimum.IsZero &&
        AmountMaximum is null &&
        DayClass == FeeRuleDayClass.Any &&
        LocalStartMinute is null;

    public static FeeRule Create(
        FeeRuleId id,
        FeeScheduleVersionId scheduleVersionId,
        FeeType type,
        int priority,
        FeeChannel channel,
        AccountProductId? accountProductId,
        AtmNetworkId? atmNetworkId,
        BankId? counterpartyBankId,
        MoneyMinor amountMinimum,
        MoneyMinor? amountMaximum,
        FeeRuleDayClass dayClass,
        int? localStartMinute,
        int? localEndMinute,
        MoneyMinor fixedAmount,
        int basisPoints,
        MoneyMinor minimumAmount,
        MoneyMinor? maximumAmount,
        string? waiverCounterKey,
        int freeOccurrencesPerBusinessMonth)
    {
        if (priority is < 0 or > MaximumPriority)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FeeRulePriorityInvalid);
        }

        if (amountMinimum.IsNegative ||
            (amountMaximum is { } maximumAmountBound && maximumAmountBound <= amountMinimum))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FeeRuleAmountRangeInvalid);
        }

        if (localStartMinute.HasValue != localEndMinute.HasValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FeeRuleTimeWindowInvalid);
        }

        if (localStartMinute is { } start && localEndMinute is { } end &&
            (start < 0 || start >= MinutesPerDay || end < 1 || end > MinutesPerDay || start >= end))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FeeRuleTimeWindowInvalid);
        }

        if (fixedAmount.IsNegative ||
            basisPoints is < 0 or > MaximumBasisPoints ||
            minimumAmount.IsNegative ||
            (maximumAmount is { } cap && cap < minimumAmount))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FeeRuleFormulaInvalid);
        }

        if (freeOccurrencesPerBusinessMonth is < 0 or > MaximumFreeOccurrences ||
            (freeOccurrencesPerBusinessMonth > 0 && string.IsNullOrEmpty(waiverCounterKey)))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FeeRuleWaiverInvalid);
        }

        return new FeeRule(
            id,
            scheduleVersionId,
            type,
            priority,
            channel,
            accountProductId,
            atmNetworkId,
            counterpartyBankId,
            amountMinimum,
            amountMaximum,
            dayClass,
            localStartMinute,
            localEndMinute,
            fixedAmount,
            basisPoints,
            minimumAmount,
            maximumAmount,
            waiverCounterKey,
            freeOccurrencesPerBusinessMonth);
    }

    public bool Matches(in FeeMatchContext context)
    {
        if (Channel != FeeChannel.Any && Channel != context.Channel)
        {
            return false;
        }

        if (AccountProductId is { } product && product != context.AccountProductId)
        {
            return false;
        }

        if (AtmNetworkId is { } network && network != context.AtmNetworkId)
        {
            return false;
        }

        if (CounterpartyBankId is { } counterparty && counterparty != context.CounterpartyBankId)
        {
            return false;
        }

        if (context.Amount < AmountMinimum ||
            (AmountMaximum is { } bound && context.Amount >= bound))
        {
            return false;
        }

        if (!DayClass.Accepts(context.DayClass))
        {
            return false;
        }

        return LocalStartMinute is not { } start ||
            LocalEndMinute is not { } end ||
            (context.LocalMinuteOfDay >= start && context.LocalMinuteOfDay < end);
    }

    public MoneyMinor Calculate(MoneyMinor amount)
    {
        if (amount.IsNegative)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FeeRuleAmountRangeInvalid);
        }

        Int128 raw = checked(FixedAmount.Intermediate + (amount.Intermediate * BasisPoints / 10000));

        if (raw < MinimumAmount.Intermediate)
        {
            raw = MinimumAmount.Intermediate;
        }

        if (MaximumAmount is { } cap && raw > cap.Intermediate)
        {
            raw = cap.Intermediate;
        }

        return MoneyMinor.FromIntermediate(raw);
    }
}

public static class FeeRuleSelection
{
    public static FeeRule? Select(IReadOnlyList<FeeRule> rules, in FeeMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(rules);

        FeeRule? selected = null;

        foreach (FeeRule rule in rules)
        {
            if (!rule.Matches(context))
            {
                continue;
            }

            if (selected is null || Precedes(rule, selected))
            {
                selected = rule;
            }
        }

        return selected;
    }

    private static bool Precedes(FeeRule candidate, FeeRule incumbent) =>
        candidate.Priority != incumbent.Priority
            ? candidate.Priority < incumbent.Priority
            : candidate.Id.Value.CompareTo(incumbent.Id.Value) < 0;
}
