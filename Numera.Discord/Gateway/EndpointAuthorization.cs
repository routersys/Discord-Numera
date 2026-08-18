using Numera.Application.Common;

namespace Numera.Discord.Gateway;

public static class EndpointAuthorization
{
    public static Abstractions.AuthorizationLevel ToContract(AuthorizationLevel level) => level switch
    {
        AuthorizationLevel.SystemOwner => Abstractions.AuthorizationLevel.SystemOwner,
        AuthorizationLevel.GuildOperator => Abstractions.AuthorizationLevel.GuildOperator,
        AuthorizationLevel.BankOperator => Abstractions.AuthorizationLevel.BankOperator,
        AuthorizationLevel.MerchantOperator => Abstractions.AuthorizationLevel.MerchantOperator,
        AuthorizationLevel.Customer => Abstractions.AuthorizationLevel.Customer,
        AuthorizationLevel.Unregistered => Abstractions.AuthorizationLevel.Unregistered,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    public static AuthorizationLevel ToApplication(Abstractions.AuthorizationLevel level) => level switch
    {
        Abstractions.AuthorizationLevel.SystemOwner => AuthorizationLevel.SystemOwner,
        Abstractions.AuthorizationLevel.GuildOperator => AuthorizationLevel.GuildOperator,
        Abstractions.AuthorizationLevel.BankOperator => AuthorizationLevel.BankOperator,
        Abstractions.AuthorizationLevel.MerchantOperator => AuthorizationLevel.MerchantOperator,
        Abstractions.AuthorizationLevel.Customer => AuthorizationLevel.Customer,
        Abstractions.AuthorizationLevel.Unregistered => AuthorizationLevel.Unregistered,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    public static AuthorizationContext ToActor(Abstractions.DiscordEndpointContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new AuthorizationContext(ToApplication(context.Level), context.UserId, context.GuildId);
    }
}
