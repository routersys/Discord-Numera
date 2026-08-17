namespace Numera.Domain.Common;

public enum BusinessDayClass
{
    BusinessDay = 1,
    NonBusinessDay = 2,
}

public static class BusinessDayClassCatalog
{
    public static string ToToken(this BusinessDayClass dayClass) => dayClass switch
    {
        BusinessDayClass.BusinessDay => "BUSINESS_DAY",
        BusinessDayClass.NonBusinessDay => "NON_BUSINESS_DAY",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.BusinessDayClassUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out BusinessDayClass dayClass)
    {
        switch (token)
        {
            case "BUSINESS_DAY":
                dayClass = BusinessDayClass.BusinessDay;
                return true;
            case "NON_BUSINESS_DAY":
                dayClass = BusinessDayClass.NonBusinessDay;
                return true;
            default:
                dayClass = default;
                return false;
        }
    }

    public static BusinessDayClass ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out BusinessDayClass dayClass)
            ? dayClass
            : throw InvariantViolationException.Create(InvariantViolationCode.BusinessDayClassUnknown);

    public static BusinessDayClass FromWeekday(BusinessDate date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? BusinessDayClass.NonBusinessDay
            : BusinessDayClass.BusinessDay;
}
