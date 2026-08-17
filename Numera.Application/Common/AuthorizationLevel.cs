namespace Numera.Application.Common;

public enum AuthorizationLevel
{
    SystemOwner = 1,
    GuildOperator = 2,
    BankOperator = 3,
    MerchantOperator = 4,
    Customer = 5,
    Unregistered = 6,
}

public sealed record AuthorizationContext(
    AuthorizationLevel Level,
    ulong DiscordUserId,
    ulong GuildId)
{
    public bool IsAtLeast(AuthorizationLevel required) => (int)Level <= (int)required;

    public bool IsAdministrative =>
        Level is AuthorizationLevel.SystemOwner
            or AuthorizationLevel.GuildOperator
            or AuthorizationLevel.BankOperator;
}

public static class AuthorizationLevelCatalog
{
    public static string ToToken(this AuthorizationLevel level) => level switch
    {
        AuthorizationLevel.SystemOwner => "SYSTEM_OWNER",
        AuthorizationLevel.GuildOperator => "GUILD_OPERATOR",
        AuthorizationLevel.BankOperator => "BANK_OPERATOR",
        AuthorizationLevel.MerchantOperator => "MERCHANT_OPERATOR",
        AuthorizationLevel.Customer => "CUSTOMER",
        AuthorizationLevel.Unregistered => "UNREGISTERED",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AuthorizationLevel level)
    {
        switch (token)
        {
            case "SYSTEM_OWNER":
                level = AuthorizationLevel.SystemOwner;
                return true;
            case "GUILD_OPERATOR":
                level = AuthorizationLevel.GuildOperator;
                return true;
            case "BANK_OPERATOR":
                level = AuthorizationLevel.BankOperator;
                return true;
            case "MERCHANT_OPERATOR":
                level = AuthorizationLevel.MerchantOperator;
                return true;
            case "CUSTOMER":
                level = AuthorizationLevel.Customer;
                return true;
            case "UNREGISTERED":
                level = AuthorizationLevel.Unregistered;
                return true;
            default:
                level = default;
                return false;
        }
    }
}
