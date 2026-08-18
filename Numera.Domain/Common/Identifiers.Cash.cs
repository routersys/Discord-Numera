namespace Numera.Domain.Common;

public readonly record struct CurrencyDenominationId(EntityIdValue Value) : IEntityId<CurrencyDenominationId>
{
    public static string EntityName => "currency_denomination";

    public static CurrencyDenominationId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CashHolderId(EntityIdValue Value) : IEntityId<CashHolderId>
{
    public static string EntityName => "cash_holder";

    public static CashHolderId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CashWalletId(EntityIdValue Value) : IEntityId<CashWalletId>
{
    public static string EntityName => "cash_wallet";

    public static CashWalletId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct BankCashVaultId(EntityIdValue Value) : IEntityId<BankCashVaultId>
{
    public static string EntityName => "bank_cash_vault";

    public static BankCashVaultId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CashMovementId(EntityIdValue Value) : IEntityId<CashMovementId>
{
    public static string EntityName => "cash_movement";

    public static CashMovementId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AtmTerminalId(EntityIdValue Value) : IEntityId<AtmTerminalId>
{
    public static string EntityName => "atm_terminal";

    public static AtmTerminalId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AtmPlacementAgreementId(EntityIdValue Value) : IEntityId<AtmPlacementAgreementId>
{
    public static string EntityName => "atm_placement_agreement";

    public static AtmPlacementAgreementId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AtmCashCassetteId(EntityIdValue Value) : IEntityId<AtmCashCassetteId>
{
    public static string EntityName => "atm_cash_cassette";

    public static AtmCashCassetteId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AtmTransactionId(EntityIdValue Value) : IEntityId<AtmTransactionId>
{
    public static string EntityName => "atm_transaction";

    public static AtmTransactionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AtmDiscordInstallationId(EntityIdValue Value) : IEntityId<AtmDiscordInstallationId>
{
    public static string EntityName => "atm_discord_installation";

    public static AtmDiscordInstallationId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}
