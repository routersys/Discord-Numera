namespace Numera.Domain.Common;

public readonly struct Rate : IEquatable<Rate>, IComparable<Rate>
{
    public const long OnePercent = 10_000_000_000L;
    public const long OneHundredPercent = 1_000_000_000_000L;

    private Rate(long partsPerTrillion) => PartsPerTrillion = partsPerTrillion;

    public static Rate Zero => default;

    public long PartsPerTrillion { get; }

    public bool IsZero => PartsPerTrillion == 0;

    public static Rate FromPartsPerTrillion(long partsPerTrillion) =>
        partsPerTrillion >= 0
            ? new Rate(partsPerTrillion)
            : throw InvariantViolationException.Create(InvariantViolationCode.RateOutOfRange);

    public Int128 ApplyToIntermediate(Int128 principal) =>
        checked(principal * PartsPerTrillion / OneHundredPercent);

    public Int128 ApplyToIntermediate(Int128 principal, Int128 numerator, Int128 denominator)
    {
        if (denominator <= Int128.Zero || numerator < Int128.Zero)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.RateOutOfRange);
        }

        return checked(principal * PartsPerTrillion * numerator / (denominator * OneHundredPercent));
    }

    public bool Equals(Rate other) => PartsPerTrillion == other.PartsPerTrillion;

    public override bool Equals(object? obj) => obj is Rate other && Equals(other);

    public override int GetHashCode() => PartsPerTrillion.GetHashCode();

    public int CompareTo(Rate other) => PartsPerTrillion.CompareTo(other.PartsPerTrillion);

    public override string ToString() => PartsPerTrillion.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static bool operator ==(Rate left, Rate right) => left.Equals(right);

    public static bool operator !=(Rate left, Rate right) => !left.Equals(right);

    public static bool operator <(Rate left, Rate right) => left.CompareTo(right) < 0;

    public static bool operator <=(Rate left, Rate right) => left.CompareTo(right) <= 0;

    public static bool operator >(Rate left, Rate right) => left.CompareTo(right) > 0;

    public static bool operator >=(Rate left, Rate right) => left.CompareTo(right) >= 0;
}
