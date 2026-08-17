namespace Numera.Domain.Common;

public readonly struct UtcTimestamp : IEquatable<UtcTimestamp>, IComparable<UtcTimestamp>
{
    private UtcTimestamp(long unixMilliseconds) => UnixMilliseconds = unixMilliseconds;

    public static UtcTimestamp Epoch => default;

    public long UnixMilliseconds { get; }

    public static UtcTimestamp FromUnixMilliseconds(long unixMilliseconds) =>
        unixMilliseconds >= 0
            ? new UtcTimestamp(unixMilliseconds)
            : throw InvariantViolationException.Create(InvariantViolationCode.TimestampOutOfRange);

    public static UtcTimestamp FromDateTimeOffset(DateTimeOffset value) =>
        FromUnixMilliseconds(value.ToUnixTimeMilliseconds());

    public DateTimeOffset ToDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds(UnixMilliseconds);

    public UtcTimestamp AddMilliseconds(long milliseconds)
    {
        Int128 shifted = checked((Int128)UnixMilliseconds + milliseconds);
        if (shifted < Int128.Zero || shifted > long.MaxValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.TimestampOutOfRange);
        }

        return new UtcTimestamp((long)shifted);
    }

    public UtcTimestamp Add(TimeSpan value) => AddMilliseconds((long)value.TotalMilliseconds);

    public bool Equals(UtcTimestamp other) => UnixMilliseconds == other.UnixMilliseconds;

    public override bool Equals(object? obj) => obj is UtcTimestamp other && Equals(other);

    public override int GetHashCode() => UnixMilliseconds.GetHashCode();

    public int CompareTo(UtcTimestamp other) => UnixMilliseconds.CompareTo(other.UnixMilliseconds);

    public override string ToString() =>
        ToDateTimeOffset().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    public static bool operator ==(UtcTimestamp left, UtcTimestamp right) => left.Equals(right);

    public static bool operator !=(UtcTimestamp left, UtcTimestamp right) => !left.Equals(right);

    public static bool operator <(UtcTimestamp left, UtcTimestamp right) => left.CompareTo(right) < 0;

    public static bool operator <=(UtcTimestamp left, UtcTimestamp right) => left.CompareTo(right) <= 0;

    public static bool operator >(UtcTimestamp left, UtcTimestamp right) => left.CompareTo(right) > 0;

    public static bool operator >=(UtcTimestamp left, UtcTimestamp right) => left.CompareTo(right) >= 0;
}
