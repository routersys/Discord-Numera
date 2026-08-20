using Numera.Discord.Abstractions;

namespace Numera.Discord.Endpoints;

[EconomyModalForm("営業日区分の設定")]
internal sealed class PanelCalendarSetForm
{
    [EconomyModalField("date", "対象日", EconomyModalFieldStyle.Short, true, 10, 10, "YYYY-MM-DD")]
    public string LocalDate { get; set; } = string.Empty;

    [EconomyModalField("class", "営業日区分", EconomyModalFieldStyle.Short, true, 1, 20, "BUSINESS_DAY または NON_BUSINESS_DAY")]
    public string DayClass { get; set; } = string.Empty;

    [EconomyModalField("reason", "理由", EconomyModalFieldStyle.Paragraph, false, 0, 200, "任意です")]
    public string Description { get; set; } = string.Empty;
}

[EconomyModalForm("上書きの解除")]
internal sealed class PanelCalendarClearForm
{
    [EconomyModalField("date", "対象日", EconomyModalFieldStyle.Short, true, 10, 10, "YYYY-MM-DD")]
    public string LocalDate { get; set; } = string.Empty;
}
