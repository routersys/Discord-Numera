using NodaTime;
using NodaTime.TimeZones;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Common;

internal static class ScheduledPaymentSchedule
{
    public static bool TryResolveFirst(
        string canonicalTimezone,
        ScheduledPaymentKind kind,
        int? anchorDayOfMonth,
        int localMinuteOfDay,
        UtcTimestamp notBefore,
        out UtcTimestamp dueAt)
    {
        dueAt = default;

        if (DateTimeZoneProviders.Tzdb.GetZoneOrNull(canonicalTimezone) is not { } zone)
        {
            return false;
        }

        if (localMinuteOfDay is < 0 or >= 1440)
        {
            return false;
        }

        LocalDate start = Instant
            .FromUnixTimeMilliseconds(notBefore.UnixMilliseconds)
            .InZone(zone)
            .Date;

        for (int offset = 0; offset <= 62; offset++)
        {
            LocalDate candidate = Candidate(kind, anchorDayOfMonth, start, offset);

            if (candidate < start)
            {
                continue;
            }

            Instant instant = Resolve(zone, candidate, localMinuteOfDay);

            if (instant.ToUnixTimeMilliseconds() <= notBefore.UnixMilliseconds)
            {
                continue;
            }

            dueAt = UtcTimestamp.FromUnixMilliseconds(instant.ToUnixTimeMilliseconds());
            return true;
        }

        return false;
    }

    public static bool TryResolveNext(
        string canonicalTimezone,
        ScheduledPaymentKind kind,
        int? anchorDayOfMonth,
        UtcTimestamp previousDueAt,
        out UtcTimestamp dueAt)
    {
        dueAt = default;

        if (kind == ScheduledPaymentKind.Once)
        {
            return false;
        }

        if (DateTimeZoneProviders.Tzdb.GetZoneOrNull(canonicalTimezone) is not { } zone)
        {
            return false;
        }

        ZonedDateTime previous = Instant
            .FromUnixTimeMilliseconds(previousDueAt.UnixMilliseconds)
            .InZone(zone);

        int localMinuteOfDay = (previous.Hour * 60) + previous.Minute;

        LocalDate next = kind == ScheduledPaymentKind.Weekly
            ? previous.Date.PlusDays(7)
            : ClampToMonth(previous.Date.PlusMonths(1), anchorDayOfMonth ?? previous.Day);

        Instant instant = Resolve(zone, next, localMinuteOfDay);
        dueAt = UtcTimestamp.FromUnixMilliseconds(instant.ToUnixTimeMilliseconds());

        return true;
    }

    private static LocalDate Candidate(
        ScheduledPaymentKind kind,
        int? anchorDayOfMonth,
        LocalDate start,
        int offset) => kind == ScheduledPaymentKind.Monthly
        ? ClampToMonth(start.PlusMonths(offset), anchorDayOfMonth ?? start.Day)
        : start.PlusDays(offset);

    private static LocalDate ClampToMonth(LocalDate month, int anchorDayOfMonth)
    {
        int lastDay = CalendarSystem.Iso.GetDaysInMonth(month.Year, month.Month);

        return new LocalDate(month.Year, month.Month, Math.Min(anchorDayOfMonth, lastDay));
    }

    private static Instant Resolve(DateTimeZone zone, LocalDate date, int localMinuteOfDay)
    {
        LocalDateTime local = date.AtMidnight().PlusMinutes(localMinuteOfDay);
        ZoneLocalMapping mapping = zone.MapLocal(local);

        return mapping.Count switch
        {
            0 => mapping.LateInterval.Start,
            _ => mapping.First().ToInstant(),
        };
    }
}
