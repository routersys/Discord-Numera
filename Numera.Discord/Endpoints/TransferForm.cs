using Numera.Discord.Abstractions;

namespace Numera.Discord.Endpoints;

[EconomyModalForm("振込内容の入力")]
internal sealed class TransferForm
{
    [EconomyModalField("bank-code", "金融機関コード", EconomyModalFieldStyle.Short, true, 1, 16, "金融機関コードを入力してください")]
    public string BankCode { get; set; } = string.Empty;

    [EconomyModalField("branch-code", "支店コード", EconomyModalFieldStyle.Short, true, 1, 16, "支店コードを入力してください")]
    public string BranchCode { get; set; } = string.Empty;

    [EconomyModalField("account-number", "口座番号", EconomyModalFieldStyle.Short, true, 1, 32, "口座番号を入力してください")]
    public string AccountNumber { get; set; } = string.Empty;

    [EconomyModalField("amount", "振込金額", EconomyModalFieldStyle.Short, true, 1, 20, "振込金額を入力してください")]
    public string Amount { get; set; } = string.Empty;

    [EconomyModalField("memo", "メモ", EconomyModalFieldStyle.Paragraph, false, 0, 100, "必要な場合だけ入力してください")]
    public string Memo { get; set; } = string.Empty;
}
