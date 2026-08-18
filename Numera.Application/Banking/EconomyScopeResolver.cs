using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal static class EconomyScopeResolver
{
    internal static Result<EconomyScopeId> Resolve(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        EconomyScopeId? requested)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(actor);

        if (requested is not { } target)
        {
            return unitOfWork.GuildEconomies.FindEconomyScope(actor.GuildId) is { } derived
                ? Result<EconomyScopeId>.Success(derived)
                : Result<EconomyScopeId>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        if (unitOfWork.GuildEconomies.FindGuildId(target) is null)
        {
            return Result<EconomyScopeId>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        string discordUserId = actor.DiscordUserId.ToString(CultureInfo.InvariantCulture);

        if (unitOfWork.SystemOwners.Contains(discordUserId))
        {
            return Result<EconomyScopeId>.Success(target);
        }

        return unitOfWork.GuildEconomies.FindEconomyScope(actor.GuildId) == target
            ? Result<EconomyScopeId>.Success(target)
            : Result<EconomyScopeId>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.ManagementAuthorityMissing);
    }
}
