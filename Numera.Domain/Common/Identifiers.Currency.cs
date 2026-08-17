namespace Numera.Domain.Common;

public readonly record struct CurrencySupplyOperationId(EntityIdValue Value)
    : IEntityId<CurrencySupplyOperationId>
{
    public static string EntityName => "currency_supply_operation";

    public static CurrencySupplyOperationId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}
