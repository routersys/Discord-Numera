using NodaTime;
using Numera.Application.Abstractions;
using Numera.Domain.Common;

namespace Numera.Application.Common;

public readonly record struct BusinessTimePoint(
    BusinessDate LocalDate,
    int LocalMinuteOfDay,
    BusinessDayClass DayClass,
    UtcTimestamp DayStart,
    UtcTimestamp DayEnd)
{
    public int BusinessMonth => LocalDate.BusinessMonth;
}

public static class EconomyBusinessCalendar
{
    public static BusinessTimePoint? Resolve(
        IEconomyCalendarRepository repository,
        EconomyScopeId economyScopeId,
        UtcTimestamp at)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (repository.FindCanonicalTimezone(economyScopeId) is not { } timezone)
        {
            return null;
        }

        if (DateTimeZoneProviders.Tzdb.GetZoneOrNull(timezone) is not { } zone)
        {
            return null;
        }

        ZonedDateTime local = Instant.FromUnixTimeMilliseconds(at.UnixMilliseconds).InZone(zone);
        BusinessDate localDate = BusinessDate.FromParts(local.Year, local.Month, local.Day);

        return new BusinessTimePoint(
            localDate,
            (local.Hour * 60) + local.Minute,
            repository.FindDayClassOverride(economyScopeId, localDate)
                ?? BusinessDayClassCatalog.FromWeekday(localDate),
            StartOfDay(zone, local.Date),
            StartOfDay(zone, local.Date.PlusDays(1)));
    }

    private static UtcTimestamp StartOfDay(DateTimeZone zone, LocalDate date) =>
        UtcTimestamp.FromUnixMilliseconds(date.AtStartOfDayInZone(zone).ToInstant().ToUnixTimeMilliseconds());
}
