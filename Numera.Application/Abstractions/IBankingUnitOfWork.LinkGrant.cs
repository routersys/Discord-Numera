using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Abstractions;

public interface IAccountLinkGrantRepository
{
    void Add(AccountLinkGrant grant);

    void Update(AccountLinkGrant grant);

    AccountLinkGrant? FindByDigest(ReadOnlyMemory<byte> codeDigest);

    IReadOnlyList<DiscordIdentityLink> ListActiveLinks(CustomerAccountId customerAccountId);

    DiscordIdentityLink? FindLink(DiscordIdentityLinkId id);
}

public partial interface IBankingUnitOfWork
{
    IAccountLinkGrantRepository AccountLinkGrants { get; }
}
