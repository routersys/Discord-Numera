using Numera.Discord.Abstractions;

namespace Numera.Discord.Endpoints;

[EconomyModalForm("銀行運営権限")]
internal sealed class PanelOperatorGrantForm
{
    [EconomyModalField("institution", "銀行コード", EconomyModalFieldStyle.Short, true, 1, 16, "例 0009")]
    public string InstitutionCode { get; set; } = string.Empty;

    [EconomyModalField("user", "対象のDiscordユーザーID", EconomyModalFieldStyle.Short, true, 1, 24, "数字のみ")]
    public string TargetUserId { get; set; } = string.Empty;

    [EconomyModalField("state", "付与か取消か", EconomyModalFieldStyle.Short, true, 1, 16, "GRANT または REVOKE")]
    public string DesiredState { get; set; } = string.Empty;
}

[EconomyModalForm("手数料規則")]
internal sealed class PanelFeeRuleForm
{
    [EconomyModalField("institution", "銀行コード", EconomyModalFieldStyle.Short, true, 1, 16, "例 0009")]
    public string InstitutionCode { get; set; } = string.Empty;

    [EconomyModalField("type", "手数料の種別", EconomyModalFieldStyle.Short, true, 1, 40, "例 INTERNAL_TRANSFER")]
    public string FeeType { get; set; } = string.Empty;

    [EconomyModalField("amounts", "定額と料率", EconomyModalFieldStyle.Short, true, 3, 32, "定額,bps")]
    public string Amounts { get; set; } = string.Empty;

    [EconomyModalField("bounds", "下限と上限", EconomyModalFieldStyle.Short, true, 1, 40, "下限,上限。上限なしは下限のみ")]
    public string Bounds { get; set; } = string.Empty;

    [EconomyModalField("free", "月内の無料回数", EconomyModalFieldStyle.Short, true, 1, 6, "0以上")]
    public string FreeOccurrences { get; set; } = string.Empty;
}

[EconomyModalForm("口座開設の審査")]
internal sealed class PanelAccountReviewForm
{
    [EconomyModalField("institution", "銀行コード", EconomyModalFieldStyle.Short, true, 1, 16, "例 0009")]
    public string InstitutionCode { get; set; } = string.Empty;

    [EconomyModalField("user", "申請者のDiscordユーザーID", EconomyModalFieldStyle.Short, true, 1, 24, "数字のみ")]
    public string ApplicantUserId { get; set; } = string.Empty;

    [EconomyModalField("state", "承認か却下か", EconomyModalFieldStyle.Short, true, 1, 16, "APPROVE または REJECT")]
    public string Decision { get; set; } = string.Empty;

    [EconomyModalField("reason", "却下の理由コード", EconomyModalFieldStyle.Short, false, 0, 40, "却下のときだけ使います")]
    public string ReasonCode { get; set; } = string.Empty;
}

[EconomyModalForm("銀行のデザイン")]
internal sealed class PanelBankDesignForm
{
    [EconomyModalField("institution", "銀行コード", EconomyModalFieldStyle.Short, true, 1, 16, "例 0009")]
    public string InstitutionCode { get; set; } = string.Empty;

    [EconomyModalField("information", "情報色", EconomyModalFieldStyle.Short, true, 6, 7, "RRGGBB")]
    public string Information { get; set; } = string.Empty;

    [EconomyModalField("success", "成功色", EconomyModalFieldStyle.Short, true, 6, 7, "RRGGBB")]
    public string Success { get; set; } = string.Empty;

    [EconomyModalField("warning", "注意色", EconomyModalFieldStyle.Short, true, 6, 7, "RRGGBB")]
    public string Warning { get; set; } = string.Empty;

    [EconomyModalField("error", "失敗色", EconomyModalFieldStyle.Short, true, 6, 7, "RRGGBB")]
    public string Error { get; set; } = string.Empty;
}
