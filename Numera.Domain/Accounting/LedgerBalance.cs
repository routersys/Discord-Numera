namespace Numera.Domain.Accounting;

public readonly struct LedgerBalance : IEquatable<LedgerBalance>
{
    private LedgerBalance(MoneyMinor postedBalance, MoneyMinor heldAmount)
    {
        PostedBalance = postedBalance;
        HeldAmount = heldAmount;
    }

    public static LedgerBalance Empty => default;

    public MoneyMinor PostedBalance { get; }

    public MoneyMinor HeldAmount { get; }

    public MoneyMinor AvailableBalance => PostedBalance.Subtract(HeldAmount);

    public static LedgerBalance Create(MoneyMinor postedBalance, MoneyMinor heldAmount) =>
        heldAmount.IsNegative
            ? throw InvariantViolationException.Create(InvariantViolationCode.HeldAmountNegative)
            : new LedgerBalance(postedBalance, heldAmount);

    public LedgerBalance ApplyPosting(EntrySide side, EntrySide normalSide, MoneyMinor amount)
    {
        if (amount.Value < 1)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.JournalEntryAmountInvalid);
        }

        MoneyMinor delta = side == normalSide ? amount : amount.Negate();
        return new LedgerBalance(PostedBalance.Add(delta), HeldAmount);
    }

    public LedgerBalance IncreaseHold(MoneyMinor amount) =>
        amount.Value < 1
            ? throw InvariantViolationException.Create(InvariantViolationCode.HoldAmountInvalid)
            : new LedgerBalance(PostedBalance, HeldAmount.Add(amount));

    public LedgerBalance DecreaseHold(MoneyMinor amount)
    {
        if (amount.Value < 1)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.HoldAmountInvalid);
        }

        MoneyMinor reduced = HeldAmount.Subtract(amount);
        return reduced.IsNegative
            ? throw InvariantViolationException.Create(InvariantViolationCode.HeldAmountNegative)
            : new LedgerBalance(PostedBalance, reduced);
    }

    public bool CanReserve(MoneyMinor amount) => amount.Value >= 1 && AvailableBalance >= amount;

    public LedgerBalance EnsureDepositAccountInvariants()
    {
        if (PostedBalance.IsNegative)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PostedBalanceNegative);
        }

        if (AvailableBalance.IsNegative)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.AvailableBalanceNegative);
        }

        return this;
    }

    public bool Equals(LedgerBalance other) =>
        PostedBalance == other.PostedBalance && HeldAmount == other.HeldAmount;

    public override bool Equals(object? obj) => obj is LedgerBalance other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(PostedBalance, HeldAmount);

    public override string ToString() => $"{PostedBalance}/{HeldAmount}";

    public static bool operator ==(LedgerBalance left, LedgerBalance right) => left.Equals(right);

    public static bool operator !=(LedgerBalance left, LedgerBalance right) => !left.Equals(right);
}
