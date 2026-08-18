using Numera.Application.Banking;
using Numera.Domain.Identity;

namespace Numera.Application.Abstractions;

public interface ICustomerIdentityReadRepository
{
    CustomerAccountStatusView? FindByDiscordUser(DiscordUserId discordUserId);
}

public partial interface IBankingReadContext
{
    ICustomerIdentityReadRepository CustomerIdentities { get; }
}
