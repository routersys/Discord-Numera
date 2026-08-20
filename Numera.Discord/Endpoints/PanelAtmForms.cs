using Numera.Discord.Abstractions;

namespace Numera.Discord.Endpoints;

[EconomyModalForm("ATM網")]
internal sealed class PanelAtmNetworkForm
{
    [EconomyModalField("name", "ATM網の名称", EconomyModalFieldStyle.Short, true, 1, 64, "既存の名称なら状態を変えます")]
    public string Name { get; set; } = string.Empty;

    [EconomyModalField("state", "状態", EconomyModalFieldStyle.Short, false, 0, 16, "ACTIVE / SUSPENDED / RETIRED")]
    public string DesiredState { get; set; } = string.Empty;
}

[EconomyModalForm("ATM端末")]
internal sealed class PanelAtmTerminalForm
{
    [EconomyModalField("terminal", "端末の表示名", EconomyModalFieldStyle.Short, true, 1, 64, "設置する端末の名前")]
    public string TerminalName { get; set; } = string.Empty;

    [EconomyModalField("institution", "所有銀行コード", EconomyModalFieldStyle.Short, true, 1, 16, "例 0009")]
    public string InstitutionCode { get; set; } = string.Empty;

    [EconomyModalField("network", "所属ATM網", EconomyModalFieldStyle.Short, false, 0, 64, "空欄なら単独設置")]
    public string NetworkName { get; set; } = string.Empty;
}

[EconomyModalForm("端末の通貨サービス")]
internal sealed class PanelAtmServiceForm
{
    [EconomyModalField("terminal", "端末の表示名", EconomyModalFieldStyle.Short, true, 1, 64, "既存の端末")]
    public string TerminalName { get; set; } = string.Empty;

    [EconomyModalField("flags", "払戻,預入,他通貨払戻", EconomyModalFieldStyle.Short, true, 5, 20, "1または0を3つ")]
    public string Flags { get; set; } = string.Empty;

    [EconomyModalField("state", "状態", EconomyModalFieldStyle.Short, true, 1, 16, "ACTIVE / SUSPENDED")]
    public string DesiredState { get; set; } = string.Empty;
}

[EconomyModalForm("現金カセット")]
internal sealed class PanelAtmCassetteForm
{
    [EconomyModalField("terminal", "端末の表示名", EconomyModalFieldStyle.Short, true, 1, 64, "既存の端末")]
    public string TerminalName { get; set; } = string.Empty;

    [EconomyModalField("value", "金種の額面", EconomyModalFieldStyle.Short, true, 1, 20, "最小単位")]
    public string DenominationValue { get; set; } = string.Empty;

    [EconomyModalField("role", "カセット種別", EconomyModalFieldStyle.Short, true, 1, 24, "DISPENSE / DEPOSIT / RECYCLE")]
    public string CassetteRole { get; set; } = string.Empty;

    [EconomyModalField("slot", "優先度と収容数", EconomyModalFieldStyle.Short, true, 3, 24, "優先度,収容数")]
    public string Slot { get; set; } = string.Empty;
}

[EconomyModalForm("金種")]
internal sealed class PanelDenominationForm
{
    [EconomyModalField("value", "額面", EconomyModalFieldStyle.Short, true, 1, 20, "最小単位")]
    public string ValueMinor { get; set; } = string.Empty;

    [EconomyModalField("kind", "種別", EconomyModalFieldStyle.Short, true, 1, 16, "NOTE または COIN")]
    public string Kind { get; set; } = string.Empty;

    [EconomyModalField("flags", "払出可,預入可", EconomyModalFieldStyle.Short, true, 3, 12, "1または0を2つ")]
    public string Flags { get; set; } = string.Empty;
}

[EconomyModalForm("現金と準備預金の交換")]
internal sealed class PanelCashConversionForm
{
    [EconomyModalField("institution", "銀行コード", EconomyModalFieldStyle.Short, true, 1, 16, "例 0009")]
    public string InstitutionCode { get; set; } = string.Empty;

    [EconomyModalField("value", "金種の額面", EconomyModalFieldStyle.Short, true, 1, 20, "最小単位")]
    public string DenominationValue { get; set; } = string.Empty;

    [EconomyModalField("quantity", "枚数", EconomyModalFieldStyle.Short, true, 1, 12, "正の整数")]
    public string Quantity { get; set; } = string.Empty;

    [EconomyModalField("direction", "向き", EconomyModalFieldStyle.Short, true, 1, 16, "TO_CASH または TO_RESERVE")]
    public string Direction { get; set; } = string.Empty;
}
