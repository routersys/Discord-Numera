using System.Globalization;
using System.Security.Cryptography;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record CreateLinkGrantCommand(ulong DiscordUserId);

public sealed record ConsumeLinkGrantCommand(ulong DiscordUserId, string Code);

public sealed record UnlinkDiscordIdentityCommand(ulong ActorDiscordUserId, ulong TargetDiscordUserId);

public sealed record LinkGrantView(string Code, UtcTimestamp ExpiresAt);

public sealed partial class CustomerAccountApplicationService
{
    public const string LinkGrantOperationType = "ACCOUNT_LINK_GRANT";
    public const string LinkConsumeOperationType = "ACCOUNT_LINK_CONSUME";
    public const string UnlinkOperationType = "ACCOUNT_LINK_UNLINK";
    public const int CodeByteLength = 16;

    public Task<Result<LinkGrantView>> CreateLinkGrantAsync(
        CreateLinkGrantCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        string code = Convert.ToHexString(RandomNumberGenerator.GetBytes(CodeByteLength));
        byte[] digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code));

        return writeGateway.ExecuteAsync(
            unitOfWork => IssueGrant(unitOfWork, command.DiscordUserId, code, digest),
            cancellationToken);
    }

    public Task<Result<CustomerAccountView>> ConsumeLinkGrantAsync(
        ConsumeLinkGrantCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrEmpty(command.Code))
        {
            return Task.FromResult(Result<CustomerAccountView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.LinkGrantInvalid, nameof(command.Code)));
        }

        byte[] digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(command.Code));

        return writeGateway.ExecuteAsync(
            unitOfWork => ConsumeGrant(unitOfWork, command.DiscordUserId, digest),
            cancellationToken);
    }

    public Task<Result> UnlinkDiscordIdentityAsync(
        UnlinkDiscordIdentityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return UnlinkCoreAsync(command, cancellationToken);
    }

    private Result<LinkGrantView> IssueGrant(
        IBankingUnitOfWork unitOfWork,
        ulong discordUserId,
        string code,
        byte[] digest)
    {
        DiscordIdentityLink? link = unitOfWork.DiscordIdentityLinks.FindActive(
            DiscordUserId.FromUInt64(discordUserId));

        if (link is null)
        {
            return Result<LinkGrantView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        UtcTimestamp now = clock.Now();

        AccountLinkGrant grant = AccountLinkGrant.Issue(
            AccountLinkGrantId.FromValue(idGenerator.NextId()),
            link.CustomerAccountId,
            digest,
            now);

        unitOfWork.AccountLinkGrants.Add(grant);

        return Result<LinkGrantView>.Success(new LinkGrantView(code, grant.ExpiresAt));
    }

    private Result<CustomerAccountView> ConsumeGrant(
        IBankingUnitOfWork unitOfWork,
        ulong discordUserId,
        byte[] digest)
    {
        AccountLinkGrant? grant = unitOfWork.AccountLinkGrants.FindByDigest(digest);

        if (grant is null || grant.Status != AccountLinkGrantStatus.Issued)
        {
            return Result<CustomerAccountView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.LinkGrantInvalid);
        }

        UtcTimestamp now = clock.Now();

        if (grant.IsExpiredAt(now))
        {
            return Result<CustomerAccountView>.Failure(
                ErrorCategory.OperationExpired, BankingErrorCodes.LinkGrantExpired);
        }

        DiscordUserId actor = DiscordUserId.FromUInt64(discordUserId);

        if (unitOfWork.DiscordIdentityLinks.FindActive(actor) is not null)
        {
            return Result<CustomerAccountView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.IdentityAlreadyLinked);
        }

        CustomerAccount? account = unitOfWork.CustomerAccounts.Find(grant.CustomerAccountId);

        if (account is null)
        {
            return Result<CustomerAccountView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        unitOfWork.DiscordIdentityLinks.Add(DiscordIdentityLink.Link(
            DiscordIdentityLinkId.FromValue(idGenerator.NextId()),
            grant.CustomerAccountId,
            actor,
            isPrimary: false,
            now));

        grant.Consume(actor, now);
        unitOfWork.AccountLinkGrants.Update(grant);

        return Result<CustomerAccountView>.Success(ToView(account));
    }

    private async Task<Result> UnlinkCoreAsync(
        UnlinkDiscordIdentityCommand command,
        CancellationToken cancellationToken)
    {
        Result<bool> outcome = await writeGateway
            .ExecuteAsync(unitOfWork => Unlink(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    private Result<bool> Unlink(IBankingUnitOfWork unitOfWork, UnlinkDiscordIdentityCommand command)
    {
        DiscordIdentityLink? actorLink = unitOfWork.DiscordIdentityLinks.FindActive(
            DiscordUserId.FromUInt64(command.ActorDiscordUserId));

        if (actorLink is null)
        {
            return Result<bool>.Failure(ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        IReadOnlyList<DiscordIdentityLink> active =
            unitOfWork.AccountLinkGrants.ListActiveLinks(actorLink.CustomerAccountId);

        string target = command.TargetDiscordUserId.ToString(CultureInfo.InvariantCulture);

        DiscordIdentityLink? victim = active.FirstOrDefault(
            candidate => string.Equals(
                candidate.DiscordUserId.Value.ToString(CultureInfo.InvariantCulture),
                target,
                StringComparison.Ordinal));

        if (victim is null)
        {
            return Result<bool>.Failure(ErrorCategory.NotFound, BankingErrorCodes.LinkNotFound);
        }

        if (active.Count <= 1)
        {
            return Result<bool>.Failure(ErrorCategory.Conflict, BankingErrorCodes.LastLinkCannotBeRemoved);
        }

        UtcTimestamp now = clock.Now();

        bool wasPrimary = victim.IsPrimary;

        if (wasPrimary)
        {
            victim.DemoteFromPrimary();
        }

        victim.Unlink(now);
        unitOfWork.DiscordIdentityLinks.Update(victim);

        if (wasPrimary)
        {
            DiscordIdentityLink promoted = active.First(candidate => candidate.Id != victim.Id);
            promoted.PromoteToPrimary();
            unitOfWork.DiscordIdentityLinks.Update(promoted);
        }

        return Result<bool>.Success(true);
    }
}
