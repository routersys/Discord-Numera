namespace Numera.Domain.Common;

public readonly struct MoneyMinor : IEquatable<MoneyMinor>, IComparable<MoneyMinor>
{
    private MoneyMinor(long value) => Value = value;

    public static MoneyMinor Zero => default;

    public long Value { get; }

    public bool IsZero => Value == 0;

    public bool IsPositive => Value > 0;

    public bool IsNegative => Value < 0;

    public Int128 Intermediate => Value;

    public static MoneyMinor FromMinor(long value) => new(value);

    public static MoneyMinor FromPositiveMinor(long value) =>
        value > 0 ? new MoneyMinor(value) : throw InvariantViolationException.Create(InvariantViolationCode.MoneyNotPositive);

    public static MoneyMinor FromIntermediate(Int128 value)
    {
        if (value < long.MinValue || value > long.MaxValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.MoneyOutOfRange);
        }

        return new MoneyMinor((long)value);
    }

    public static MoneyMinor Sum(ReadOnlySpan<MoneyMinor> values)
    {
        Int128 total = Int128.Zero;
        foreach (MoneyMinor value in values)
        {
            total = checked(total + value.Value);
        }

        return FromIntermediate(total);
    }

    public MoneyMinor Add(MoneyMinor other) => FromIntermediate(checked(Intermediate + other.Intermediate));

    public MoneyMinor Subtract(MoneyMinor other) => FromIntermediate(checked(Intermediate - other.Intermediate));

    public MoneyMinor Negate() => FromIntermediate(checked(-Intermediate));

    public bool Equals(MoneyMinor other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is MoneyMinor other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public int CompareTo(MoneyMinor other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static MoneyMinor operator +(MoneyMinor left, MoneyMinor right) => left.Add(right);

    public static MoneyMinor operator -(MoneyMinor left, MoneyMinor right) => left.Subtract(right);

    public static MoneyMinor operator -(MoneyMinor value) => value.Negate();

    public static bool operator ==(MoneyMinor left, MoneyMinor right) => left.Equals(right);

    public static bool operator !=(MoneyMinor left, MoneyMinor right) => !left.Equals(right);

    public static bool operator <(MoneyMinor left, MoneyMinor right) => left.CompareTo(right) < 0;

    public static bool operator <=(MoneyMinor left, MoneyMinor right) => left.CompareTo(right) <= 0;

    public static bool operator >(MoneyMinor left, MoneyMinor right) => left.CompareTo(right) > 0;

    public static bool operator >=(MoneyMinor left, MoneyMinor right) => left.CompareTo(right) >= 0;
}
