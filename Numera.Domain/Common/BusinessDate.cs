using System.Globalization;

namespace Numera.Domain.Common;

public readonly struct BusinessDate : IEquatable<BusinessDate>, IComparable<BusinessDate>
{
    public const int TextLength = 10;

    private readonly DateOnly value;

    private BusinessDate(DateOnly value) => this.value = value;

    public int Year => value.Year;

    public int Month => value.Month;

    public int Day => value.Day;

    public int DayNumber => value.DayNumber;

    public static BusinessDate FromParts(int year, int month, int day)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BusinessDateInvalid);
        }

        if (day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BusinessDateInvalid);
        }

        return new BusinessDate(new DateOnly(year, month, day));
    }

    public static BusinessDate FromDayNumber(int dayNumber) =>
        dayNumber is >= 0 and <= 3652058
            ? new BusinessDate(DateOnly.FromDayNumber(dayNumber))
            : throw InvariantViolationException.Create(InvariantViolationCode.BusinessDateInvalid);

    public static bool TryParse(ReadOnlySpan<char> source, out BusinessDate result)
    {
        result = default;
        if (source.Length != TextLength || source[4] != '-' || source[7] != '-')
        {
            return false;
        }

        if (!TryReadDigits(source[..4], out int year) ||
            !TryReadDigits(source.Slice(5, 2), out int month) ||
            !TryReadDigits(source.Slice(8, 2), out int day))
        {
            return false;
        }

        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        result = new BusinessDate(new DateOnly(year, month, day));
        return true;
    }

    public static BusinessDate Parse(ReadOnlySpan<char> source) =>
        TryParse(source, out BusinessDate result)
            ? result
            : throw InvariantViolationException.Create(InvariantViolationCode.BusinessDateInvalid);

    public BusinessDate AddDays(int days)
    {
        long shifted = (long)DayNumber + days;
        return shifted is >= 0 and <= 3652058
            ? new BusinessDate(DateOnly.FromDayNumber((int)shifted))
            : throw InvariantViolationException.Create(InvariantViolationCode.BusinessDateInvalid);
    }

    public bool Equals(BusinessDate other) => value == other.value;

    public override bool Equals(object? obj) => obj is BusinessDate other && Equals(other);

    public override int GetHashCode() => value.GetHashCode();

    public int CompareTo(BusinessDate other) => value.CompareTo(other.value);

    public override string ToString() => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool operator ==(BusinessDate left, BusinessDate right) => left.Equals(right);

    public static bool operator !=(BusinessDate left, BusinessDate right) => !left.Equals(right);

    public static bool operator <(BusinessDate left, BusinessDate right) => left.CompareTo(right) < 0;

    public static bool operator <=(BusinessDate left, BusinessDate right) => left.CompareTo(right) <= 0;

    public static bool operator >(BusinessDate left, BusinessDate right) => left.CompareTo(right) > 0;

    public static bool operator >=(BusinessDate left, BusinessDate right) => left.CompareTo(right) >= 0;

    private static bool TryReadDigits(ReadOnlySpan<char> source, out int result)
    {
        result = 0;
        foreach (char character in source)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }

            result = (result * 10) + (character - '0');
        }

        return true;
    }
}
