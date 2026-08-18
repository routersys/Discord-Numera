using Discord;
using Numera.Application.Banking;
using Numera.Application.Common;

namespace Numera.Discord.Gateway;

public interface IAuthorizationResolver
{
    Task<AuthorizationContext> ResolveAsync(
        ulong discordUserId,
        ulong guildId,
        IGuildUser? member,
        CancellationToken cancellationToken);
}

internal sealed class AuthorizationResolver : IAuthorizationResolver
{
    private readonly ICustomerAccountApplicationService accounts;

    public AuthorizationResolver(ICustomerAccountApplicationService accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        this.accounts = accounts;
    }

    internal static bool IsGuildOperator(IGuildUser? member) =>
        member is not null
        && (member.Guild is not null && member.Guild.OwnerId == member.Id
            || member.GuildPermissions.ManageGuild
            || member.GuildPermissions.Administrator);

    public async Task<AuthorizationContext> ResolveAsync(
        ulong discordUserId,
        ulong guildId,
        IGuildUser? member,
        CancellationToken cancellationToken)
    {
        if (IsGuildOperator(member))
        {
            return new AuthorizationContext(AuthorizationLevel.GuildOperator, discordUserId, guildId);
        }

        Result<CustomerAccountStatusView> status = await accounts
            .GetCustomerAccountStatusAsync(new GetCustomerAccountStatusQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);

        return new AuthorizationContext(
            status.IsSuccess ? AuthorizationLevel.Customer : AuthorizationLevel.Unregistered,
            discordUserId,
            guildId);
    }
}
