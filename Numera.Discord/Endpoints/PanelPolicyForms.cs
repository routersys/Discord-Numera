using Numera.Discord.Abstractions;

namespace Numera.Discord.Endpoints;

[EconomyModalForm("表示配色")]
internal sealed class PanelPresentationForm
{
    [EconomyModalField("information", "情報色", EconomyModalFieldStyle.Short, true, 6, 7, "RRGGBB")]
    public string Information { get; set; } = string.Empty;

    [EconomyModalField("success", "成功色", EconomyModalFieldStyle.Short, true, 6, 7, "RRGGBB")]
    public string Success { get; set; } = string.Empty;

    [EconomyModalField("warning", "注意色", EconomyModalFieldStyle.Short, true, 6, 7, "RRGGBB")]
    public string Warning { get; set; } = string.Empty;

    [EconomyModalField("error", "失敗色", EconomyModalFieldStyle.Short, true, 6, 7, "RRGGBB")]
    public string Error { get; set; } = string.Empty;

    [EconomyModalField("neutral", "中立色", EconomyModalFieldStyle.Short, true, 6, 7, "RRGGBB")]
    public string Neutral { get; set; } = string.Empty;
}

[EconomyModalForm("預金保険の保護区分")]
internal sealed class PanelInsuranceSchemeForm
{
    [EconomyModalField("class", "保護区分コード", EconomyModalFieldStyle.Short, true, 1, 32, "例 GENERAL")]
    public string ProtectionClass { get; set; } = string.Empty;

    [EconomyModalField("coverage", "保護上限", EconomyModalFieldStyle.Short, true, 1, 20, "最小単位で入力します")]
    public string CoverageLimit { get; set; } = string.Empty;

    [EconomyModalField("fee", "加入手数料", EconomyModalFieldStyle.Short, true, 1, 20, "最小単位で入力します。0も可")]
    public string EnrollmentFee { get; set; } = string.Empty;
}

[EconomyModalForm("保護区分の状態")]
internal sealed class PanelInsuranceStateForm
{
    [EconomyModalField("class", "保護区分コード", EconomyModalFieldStyle.Short, true, 1, 32, "既存の区分を入力します")]
    public string ProtectionClass { get; set; } = string.Empty;

    [EconomyModalField("state", "変更後の状態", EconomyModalFieldStyle.Short, true, 1, 16, "SUSPENDED / ACTIVE / RETIRED")]
    public string DesiredState { get; set; } = string.Empty;
}

[EconomyModalForm("為替介入の権限")]
internal sealed class PanelInterventionForm
{
    [EconomyModalField("pair", "通貨ペア", EconomyModalFieldStyle.Short, true, 3, 24, "例 NMR/YUU")]
    public string Pair { get; set; } = string.Empty;

    [EconomyModalField("side", "許可する方向", EconomyModalFieldStyle.Short, true, 1, 16, "BUY_BASE / SELL_BASE / BOTH")]
    public string AllowedSide { get; set; } = string.Empty;

    [EconomyModalField("limits", "1回上限と総額上限", EconomyModalFieldStyle.Short, true, 3, 44, "1回上限,総額上限")]
    public string Limits { get; set; } = string.Empty;

    [EconomyModalField("slippage", "許容スリッページ", EconomyModalFieldStyle.Short, true, 1, 6, "bps")]
    public string Slippage { get; set; } = string.Empty;

    [EconomyModalField("until", "有効期限", EconomyModalFieldStyle.Short, true, 1, 20, "Unix秒")]
    public string ValidUntil { get; set; } = string.Empty;
}
