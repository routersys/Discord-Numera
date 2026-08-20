using Numera.Discord.Abstractions;

namespace Numera.Discord.Endpoints;

[EconomyModalForm("通貨信頼性の基準")]
internal sealed class PanelTrustPolicyForm
{
    [EconomyModalField("established", "Established の基準", EconomyModalFieldStyle.Short, true, 3, 40, "経過秒,取引日数,取引相手数")]
    public string Established { get; set; } = string.Empty;

    [EconomyModalField("trusted", "Trusted の基準", EconomyModalFieldStyle.Short, true, 3, 40, "経過秒,取引日数,取引相手数")]
    public string Trusted { get; set; } = string.Empty;

    [EconomyModalField("reserve", "ReserveEligible の基準", EconomyModalFieldStyle.Short, true, 3, 40, "経過秒,取引日数,取引相手数")]
    public string Reserve { get; set; } = string.Empty;
}

[EconomyModalForm("決済ネットワークの方針")]
internal sealed class PanelNetworkPolicyForm
{
    [EconomyModalField("network", "ネットワークコード", EconomyModalFieldStyle.Short, true, 1, 32, "既存のコードを入力します")]
    public string NetworkCode { get; set; } = string.Empty;

    [EconomyModalField("mode", "決済方式", EconomyModalFieldStyle.Short, true, 1, 24, "RTGS または DEFERRED_NET")]
    public string SettlementMode { get; set; } = string.Empty;

    [EconomyModalField("posting", "受取人記帳方針", EconomyModalFieldStyle.Short, true, 1, 32, "ON_SETTLEMENT または ON_ACCEPTANCE")]
    public string PostingPolicy { get; set; } = string.Empty;

    [EconomyModalField("interval", "清算間隔（秒）", EconomyModalFieldStyle.Short, false, 0, 10, "DEFERRED_NET のとき必須です")]
    public string ClearingInterval { get; set; } = string.Empty;

    [EconomyModalField("exposure", "銀行あたり与信上限", EconomyModalFieldStyle.Short, true, 1, 20, "最小単位で入力します")]
    public string ExposureLimit { get; set; } = string.Empty;
}

[EconomyModalForm("ネットワークの停止と再開")]
internal sealed class PanelNetworkStateForm
{
    [EconomyModalField("network", "ネットワークコード", EconomyModalFieldStyle.Short, true, 1, 32, "既存のコードを入力します")]
    public string NetworkCode { get; set; } = string.Empty;

    [EconomyModalField("state", "変更後の状態", EconomyModalFieldStyle.Short, true, 1, 16, "SUSPENDED または ACTIVE")]
    public string DesiredState { get; set; } = string.Empty;
}

[EconomyModalForm("健全性基準")]
internal sealed class PanelPrudentialPolicyForm
{
    [EconomyModalField("cet1", "CET1 の下限と貸出下限", EconomyModalFieldStyle.Short, true, 3, 24, "最低bps,貸出bps")]
    public string Cet1 { get; set; } = string.Empty;

    [EconomyModalField("leverage", "レバレッジの下限と警告", EconomyModalFieldStyle.Short, true, 3, 24, "最低bps,警告bps")]
    public string Leverage { get; set; } = string.Empty;

    [EconomyModalField("liquidity", "流動性の下限", EconomyModalFieldStyle.Short, true, 4, 12, "bps。10000以上です")]
    public string Liquidity { get; set; } = string.Empty;

    [EconomyModalField("capital", "銀行設立の最低資本", EconomyModalFieldStyle.Short, true, 1, 20, "最小単位で入力します")]
    public string MinimumCapital { get; set; } = string.Empty;
}
