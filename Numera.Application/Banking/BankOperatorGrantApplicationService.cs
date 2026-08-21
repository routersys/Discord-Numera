using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record GrantBankOperatorCommand(
    AuthorizationContext Actor,
    string InstitutionCode,
    ulong TargetDiscordUserId);

public sealed record RevokeBankOperatorCommand(
    AuthorizationContext Actor,
    string InstitutionCode,
    ulong TargetDiscordUserId);

public sealed record BankOperatorGrantView(
    BankOperatorGrantId Id,
    string InstitutionCode,
    ulong DiscordUserId,
    string Status);

public sealed record GetBankOperatorGrantQuery(
    AuthorizationContext Actor,
    string InstitutionCode,
    ulong TargetDiscordUserId);

public sealed record BankOperatorGrantStatusView(
    string InstitutionCode,
    ulong TargetDiscordUserId,
    bool HasActiveGrant);

public interface IBankOperatorGrantApplicationService
{
    Task<Result<BankOperatorGrantStatusView>> GetGrantStatusAsync(
        GetBankOperatorGrantQuery query,
        CancellationToken cancellationToken);

    Task<Result<BankOperatorGrantView>> GrantAsync(
        GrantBankOperatorCommand command,
        CancellationToken cancellationToken);

    Task<Result> RevokeAsync(
        RevokeBankOperatorCommand command,
        CancellationToken cancellationToken);
}

public sealed class BankOperatorGrantApplicationService : IBankOperatorGrantApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public BankOperatorGrantApplicationService(
        IBankingWriteGateway writeGateway,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public Task<Result<BankOperatorGrantStatusView>> GetGrantStatusAsync(
        GetBankOperatorGrantQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork => GrantStatus(unitOfWork, query), cancellationToken);
    }

    private static Result<BankOperatorGrantStatusView> GrantStatus(
        IBankingUnitOfWork unitOfWork,
        GetBankOperatorGrantQuery query)
    {
        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(
            unitOfWork, query.Actor, requested: null);

        if (!scope.IsSuccess)
        {
            return Result<BankOperatorGrantStatusView>.Failure(scope.Error!);
        }

        if (!InstitutionCode.TryParse(query.InstitutionCode, out InstitutionCode institutionCode) ||
            unitOfWork.Banks.FindByInstitutionCode(scope.Value, institutionCode.Value)
                is not { } bank)
        {
            return Result<BankOperatorGrantStatusView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(
            unitOfWork, query.Actor, bank.EconomyScopeId);

        if (!authorized.IsSuccess)
        {
            return Result<BankOperatorGrantStatusView>.Failure(authorized.Error!);
        }

        BankOperatorGrant? grant = unitOfWork.BankOperatorGrants.FindActive(
            bank.Id, DiscordUserId.FromUInt64(query.TargetDiscordUserId));

        return Result<BankOperatorGrantStatusView>.Success(new BankOperatorGrantStatusView(
            institutionCode.Value, query.TargetDiscordUserId, grant is not null));
    }

    public Task<Result<BankOperatorGrantView>> GrantAsync(
        GrantBankOperatorCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Grant(unitOfWork, command), cancellationToken);
    }

    public Task<Result> RevokeAsync(
        RevokeBankOperatorCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return RevokeInternalAsync(command, cancellationToken);
    }

    private Result<BankOperatorGrantView> Grant(
        IBankingUnitOfWork unitOfWork,
        GrantBankOperatorCommand command)
    {
        Result<Bank> resolved = ResolveBank(unitOfWork, command.Actor, command.InstitutionCode);

        if (!resolved.IsSuccess)
        {
            return Result<BankOperatorGrantView>.Failure(resolved.Error!);
        }

        Bank bank = resolved.Value;
        DiscordUserId target = DiscordUserId.FromUInt64(command.TargetDiscordUserId);

        if (unitOfWork.BankOperatorGrants.FindActive(bank.Id, target) is not null)
        {
            return Result<BankOperatorGrantView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.BankOperatorGrantAlreadyActive);
        }

        if (command.TargetDiscordUserId == command.Actor.DiscordUserId)
        {
            return Result<BankOperatorGrantView>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.BankOperatorGrantSelfService);
        }

        BankOperatorGrant grant = BankOperatorGrant.Grant(
            BankOperatorGrantId.FromValue(idGenerator.NextId()),
            bank.Id,
            target,
            DiscordUserId.FromUInt64(command.Actor.DiscordUserId),
            clock.Now());

        unitOfWork.BankOperatorGrants.Add(grant);

        return Result<BankOperatorGrantView>.Success(
            new BankOperatorGrantView(
                grant.Id,
                command.InstitutionCode,
                command.TargetDiscordUserId,
                grant.Status.ToToken()));
    }

    private async Task<Result> RevokeInternalAsync(
        RevokeBankOperatorCommand command,
        CancellationToken cancellationToken)
    {
        Result<bool> outcome = await writeGateway
            .ExecuteAsync(unitOfWork => Revoke(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    private Result<bool> Revoke(IBankingUnitOfWork unitOfWork, RevokeBankOperatorCommand command)
    {
        Result<Bank> resolved = ResolveBank(unitOfWork, command.Actor, command.InstitutionCode);

        if (!resolved.IsSuccess)
        {
            return Result<bool>.Failure(resolved.Error!);
        }

        Bank bank = resolved.Value;
        DiscordUserId target = DiscordUserId.FromUInt64(command.TargetDiscordUserId);

        if (unitOfWork.BankOperatorGrants.FindActive(bank.Id, target) is not { } grant)
        {
            return Result<bool>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankOperatorGrantNotFound);
        }

        grant.Revoke(clock.Now());
        unitOfWork.BankOperatorGrants.Update(grant);

        return Result<bool>.Success(true);
    }

    private static Result<Bank> ResolveBank(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        string institutionCode)
    {
        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(unitOfWork, actor, requested: null);

        if (!scope.IsSuccess)
        {
            return Result<Bank>.Failure(scope.Error!);
        }

        if (unitOfWork.Banks.FindByInstitutionCode(scope.Value, institutionCode) is not { } bank)
        {
            return Result<Bank>.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(unitOfWork, actor, bank.EconomyScopeId);

        return authorized.IsSuccess
            ? Result<Bank>.Success(bank)
            : Result<Bank>.Failure(authorized.Error!);
    }
}
