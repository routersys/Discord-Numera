namespace Numera.Domain.Common;

public readonly struct MinorUnitDigits : IEquatable<MinorUnitDigits>, IComparable<MinorUnitDigits>
{
    public const int Minimum = 0;
    public const int Maximum = 6;

    private static readonly long[] ScaleFactors =
    [
        1L,
        10L,
        100L,
        1_000L,
        10_000L,
        100_000L,
        1_000_000L,
    ];

    private MinorUnitDigits(int value) => Value = value;

    public int Value { get; }

    public long ScaleFactor => ScaleFactors[Value];

    public static MinorUnitDigits FromInt32(int value) =>
        value is >= Minimum and <= Maximum
            ? new MinorUnitDigits(value)
            : throw InvariantViolationException.Create(InvariantViolationCode.MinorUnitDigitsOutOfRange);

    public bool Equals(MinorUnitDigits other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is MinorUnitDigits other && Equals(other);

    public override int GetHashCode() => Value;

    public int CompareTo(MinorUnitDigits other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static bool operator ==(MinorUnitDigits left, MinorUnitDigits right) => left.Equals(right);

    public static bool operator !=(MinorUnitDigits left, MinorUnitDigits right) => !left.Equals(right);

    public static bool operator <(MinorUnitDigits left, MinorUnitDigits right) => left.CompareTo(right) < 0;

    public static bool operator <=(MinorUnitDigits left, MinorUnitDigits right) => left.CompareTo(right) <= 0;

    public static bool operator >(MinorUnitDigits left, MinorUnitDigits right) => left.CompareTo(right) > 0;

    public static bool operator >=(MinorUnitDigits left, MinorUnitDigits right) => left.CompareTo(right) >= 0;
}
