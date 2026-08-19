using Numera.Discord.Abstractions;

namespace Numera.Discord.Endpoints;

[EconomyModalForm("銀行の基本情報")]
internal sealed class BankCreateForm
{
    [EconomyModalField("bank-name", "銀行名", EconomyModalFieldStyle.Short, true, 1, 64, "銀行名を入力してください")]
    public string BankName { get; set; } = string.Empty;

    [EconomyModalField("branch-code", "本店の支店コード", EconomyModalFieldStyle.Short, true, 1, 16, "支店コードを入力してください")]
    public string BranchCode { get; set; } = string.Empty;

    [EconomyModalField("branch-name", "本店の支店名", EconomyModalFieldStyle.Short, true, 1, 64, "支店名を入力してください")]
    public string BranchName { get; set; } = string.Empty;

    [EconomyModalField("product-code", "預金商品コード", EconomyModalFieldStyle.Short, true, 1, 16, "商品コードを入力してください")]
    public string ProductCode { get; set; } = string.Empty;

    [EconomyModalField("product-name", "預金商品名", EconomyModalFieldStyle.Short, true, 1, 64, "商品名を入力してください")]
    public string ProductName { get; set; } = string.Empty;
}
