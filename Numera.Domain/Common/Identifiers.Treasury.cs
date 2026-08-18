namespace Numera.Domain.Common;

public readonly record struct BankTreasuryFxAccountId(EntityIdValue Value)
    : IEntityId<BankTreasuryFxAccountId>
{
    public static string EntityName => "bank_treasury_fx_account";

    public static BankTreasuryFxAccountId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}
