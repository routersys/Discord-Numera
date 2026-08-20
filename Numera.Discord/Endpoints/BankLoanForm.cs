using Numera.Discord.Abstractions;

namespace Numera.Discord.Endpoints;

[EconomyModalForm("融資の申込")]
internal sealed class BankLoanForm
{
    [EconomyModalField("principal", "借入額", EconomyModalFieldStyle.Short, true, 1, 20, "最小単位で入力してください")]
    public string Principal { get; set; } = string.Empty;

    [EconomyModalField("product", "商品コード", EconomyModalFieldStyle.Short, true, 1, 16, "銀行詳細に表示された商品コード")]
    public string ProductCode { get; set; } = string.Empty;
}
