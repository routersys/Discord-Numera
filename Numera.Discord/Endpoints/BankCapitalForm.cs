using Numera.Discord.Abstractions;

namespace Numera.Discord.Endpoints;

[EconomyModalForm("資本払込の入力")]
internal sealed class BankCapitalForm
{
    [EconomyModalField("amount", "払込額", EconomyModalFieldStyle.Short, true, 1, 20, "最小単位で入力してください")]
    public string Amount { get; set; } = string.Empty;

    [EconomyModalField("source", "払込元の金融機関コード", EconomyModalFieldStyle.Short, false, 0, 16, "空欄なら中央銀行が払い込みます")]
    public string SourceInstitutionCode { get; set; } = string.Empty;
}
