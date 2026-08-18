using Numera.Application.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Abstractions;

public interface ICustomerIdentityReadRepository
{
    CustomerAccountStatusView? FindByDiscordUser(DiscordUserId discordUserId);
}

public interface IEconomyScopeReadRepository
{
    EconomyScopeId? FindByGuild(ulong guildId);
}

public partial interface IGuildEconomyRepository
{
    EconomyScopeId? FindEconomyScope(ulong guildId);
}

public partial interface IBankingReadContext
{
    ICustomerIdentityReadRepository CustomerIdentities { get; }

    IEconomyScopeReadRepository EconomyScopes { get; }
}
