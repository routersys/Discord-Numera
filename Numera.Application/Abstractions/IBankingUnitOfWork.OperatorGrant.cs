using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Abstractions;

public interface IBankOperatorGrantRepository
{
    void Add(BankOperatorGrant grant);

    void Update(BankOperatorGrant grant);

    BankOperatorGrant? FindActive(BankId bankId, DiscordUserId discordUserId);

    BankOperatorGrant? Find(BankOperatorGrantId id);
}

public partial interface IBankingUnitOfWork
{
    IBankOperatorGrantRepository BankOperatorGrants { get; }
}
