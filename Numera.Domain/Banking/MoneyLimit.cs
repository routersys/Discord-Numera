namespace Numera.Domain.Banking;

public enum LimitOutcome
{
    Allowed = 1,
    Exceeded = 2,
    Disabled = 3,
}

public readonly record struct MoneyLimit
{
    private MoneyLimit(MoneyMinor? ceiling) => Ceiling = ceiling;

    public static MoneyLimit Unlimited => default;

    public MoneyMinor? Ceiling { get; }

    public bool IsDisabled => Ceiling is { } ceiling && ceiling.IsZero;

    public static MoneyLimit Of(MoneyMinor? ceiling)
    {
        if (ceiling is { } value && value.IsNegative)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.LimitValueNegative);
        }

        return new MoneyLimit(ceiling);
    }

    public static MoneyLimit Resolve(MoneyMinor? bankCeiling, MoneyMinor? customerPreference)
    {
        MoneyLimit bank = Of(bankCeiling);
        MoneyLimit customer = Of(customerPreference);

        if (bank.Ceiling is not { } bankValue)
        {
            return customer;
        }

        if (customer.Ceiling is not { } customerValue)
        {
            return bank;
        }

        return new MoneyLimit(customerValue < bankValue ? customerValue : bankValue);
    }

    public LimitOutcome Evaluate(MoneyMinor alreadyUsed, MoneyMinor amount)
    {
        if (alreadyUsed.IsNegative || amount.IsNegative)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.LimitUsageInvalid);
        }

        if (Ceiling is not { } ceiling)
        {
            return LimitOutcome.Allowed;
        }

        if (ceiling.IsZero)
        {
            return LimitOutcome.Disabled;
        }

        return alreadyUsed.Add(amount) > ceiling ? LimitOutcome.Exceeded : LimitOutcome.Allowed;
    }
}
