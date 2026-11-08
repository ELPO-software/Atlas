using Atlas.App.Resources;

namespace Atlas.App.Text;

/// <summary>Describes dates in the local time of the user.</summary>
internal static class DateText
{
    public static string Modified(DateTimeOffset value, TimeProvider time)
    {
        var local = TimeZoneInfo.ConvertTime(value, time.LocalTimeZone);
        var today = TimeZoneInfo.ConvertTime(time.GetUtcNow(), time.LocalTimeZone).Date;

        if (local.Date == today)
            return UiText.Format(Strings.ModifiedToday, Hour(local));

        return local.Date == today.AddDays(-1)
            ? UiText.Format(Strings.ModifiedYesterday, Hour(local))
            : UiText.Format(Strings.ModifiedOn, Day(local));
    }

    /// <summary>"Aujourd'hui à 14:32", "Hier à 09:05" or "4 novembre 2026 à 10:05".</summary>
    public static string At(DateTimeOffset value, TimeProvider time)
    {
        var local = TimeZoneInfo.ConvertTime(value, time.LocalTimeZone);
        var today = TimeZoneInfo.ConvertTime(time.GetUtcNow(), time.LocalTimeZone).Date;

        if (local.Date == today)
            return UiText.Format(Strings.DateAtToday, Hour(local));

        return local.Date == today.AddDays(-1)
            ? UiText.Format(Strings.DateAtYesterday, Hour(local))
            : UiText.Format(Strings.DateAt, Day(local), Hour(local));
    }

    /// <summary>"8 novembre 2026 à 11:39", for a name that must stay meaningful later.</summary>
    public static string Full(DateTimeOffset value, TimeProvider time)
    {
        var local = TimeZoneInfo.ConvertTime(value, time.LocalTimeZone);
        return UiText.Format(Strings.DateAt, Day(local), Hour(local));
    }

    public static string Created(DateTimeOffset value, TimeProvider time) =>
        UiText.Format(Strings.CreatedOn, Day(TimeZoneInfo.ConvertTime(value, time.LocalTimeZone)));

    private static string Hour(DateTimeOffset local) => local.ToString("HH:mm", UiText.Culture);

    // French writes the first day of a month "1er".
    private static string Day(DateTimeOffset local) =>
        (local.Day == 1 ? "1er" : local.Day.ToString(UiText.Culture)) + local.ToString(" MMMM yyyy", UiText.Culture);
}
