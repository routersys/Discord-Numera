using Numera.Discord.Abstractions;

namespace Numera.Discord.Endpoints;

[EconomyModalForm("商品の登録")]
internal sealed class PanelMerchantProductForm
{
    [EconomyModalField("sku", "商品コード", EconomyModalFieldStyle.Short, true, 1, 32, "店舗内で一意です")]
    public string Sku { get; set; } = string.Empty;

    [EconomyModalField("name", "商品名", EconomyModalFieldStyle.Short, true, 1, 64, "表示される名前")]
    public string DisplayName { get; set; } = string.Empty;

    [EconomyModalField("description", "説明", EconomyModalFieldStyle.Paragraph, false, 0, 200, "任意です")]
    public string Description { get; set; } = string.Empty;

    [EconomyModalField("inventory", "在庫方式", EconomyModalFieldStyle.Short, true, 1, 24, "TRACKED または UNLIMITED")]
    public string InventoryMode { get; set; } = string.Empty;

    [EconomyModalField("scope", "販売範囲", EconomyModalFieldStyle.Short, true, 1, 24, "HOME_GUILD または CROSS_GUILD")]
    public string SaleScope { get; set; } = string.Empty;
}

[EconomyModalForm("商品の価格")]
internal sealed class PanelMerchantPriceForm
{
    [EconomyModalField("sku", "商品コード", EconomyModalFieldStyle.Short, true, 1, 32, "既存の商品")]
    public string Sku { get; set; } = string.Empty;

    [EconomyModalField("price", "単価", EconomyModalFieldStyle.Short, true, 1, 20, "最小単位")]
    public string UnitPrice { get; set; } = string.Empty;
}

[EconomyModalForm("在庫と商品の状態")]
internal sealed class PanelMerchantStockForm
{
    [EconomyModalField("sku", "商品コード", EconomyModalFieldStyle.Short, true, 1, 32, "既存の商品")]
    public string Sku { get; set; } = string.Empty;

    [EconomyModalField("delta", "在庫の増減", EconomyModalFieldStyle.Short, false, 0, 12, "増やすなら正、減らすなら負")]
    public string QuantityDelta { get; set; } = string.Empty;

    [EconomyModalField("state", "商品の状態", EconomyModalFieldStyle.Short, false, 0, 24, "ACTIVE / SUSPENDED / RETIRED")]
    public string DesiredState { get; set; } = string.Empty;
}
