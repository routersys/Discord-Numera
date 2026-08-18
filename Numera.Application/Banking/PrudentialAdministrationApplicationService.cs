using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record PrudentialPolicyInput(
    int MinimumCet1Bps,
    int LendingCet1Bps,
    int MinimumLeverageBps,
    int ConfiguredWarningLeverageBps,
    int MinimumLiquidityBps,
    long MinimumInitialBankCapitalMinor);

public sealed record StartPrudentialPolicyDraftCommand(
    AuthorizationContext Actor,
    PrudentialPolicyInput Policy,
    EconomyScopeId? TargetEconomyScopeId = null);

public sealed record UpdatePrudentialPolicyDraftCommand(
    AuthorizationContext Actor,
    PrudentialPolicyVersionId PrudentialPolicyVersionId,
    PrudentialPolicyInput Policy);

public sealed record PublishPrudentialPolicyCommand(
    AuthorizationContext Actor,
    PrudentialPolicyVersionId PrudentialPolicyVersionId);

public sealed record PrudentialPolicyDraftView(PrudentialPolicyVersionId Id, long Version);

public sealed record PrudentialPolicyVersionView(
    PrudentialPolicyVersionId Id,
    MoneyMinor MinimumInitialBankCapital);

public interface IPrudentialAdministrationApplicationService
{
    Task<Result<PrudentialPolicyDraftView>> StartDraftAsync(
        StartPrudentialPolicyDraftCommand command,
        CancellationToken cancellationToken);

    Task<Result<PrudentialPolicyDraftView>> UpdateDraftAsync(
        UpdatePrudentialPolicyDraftCommand command,
        CancellationToken cancellationToken);

    Task<Result<PrudentialPolicyVersionView>> PublishAsync(
        PublishPrudentialPolicyCommand command,
        CancellationToken cancellationToken);
}

public sealed class PrudentialAdministrationApplicationService : IPrudentialAdministrationApplicationService
{
    public const string DraftStatus = "DRAFT";

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public PrudentialAdministrationApplicationService(
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

    public Task<Result<PrudentialPolicyDraftView>> StartDraftAsync(
        StartPrudentialPolicyDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => StartDraft(unitOfWork, command), cancellationToken);
    }

    public Task<Result<PrudentialPolicyDraftView>> UpdateDraftAsync(
        UpdatePrudentialPolicyDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => UpdateDraft(unitOfWork, command), cancellationToken);
    }

    public Task<Result<PrudentialPolicyVersionView>> PublishAsync(
        PublishPrudentialPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Publish(unitOfWork, command), cancellationToken);
    }

    private Result<PrudentialPolicyDraftView> StartDraft(
        IBankingUnitOfWork unitOfWork,
        StartPrudentialPolicyDraftCommand command)
    {
        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(
            unitOfWork, command.Actor, command.TargetEconomyScopeId);

        if (!scope.IsSuccess)
        {
            return Result<PrudentialPolicyDraftView>.Failure(scope.Error!);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(unitOfWork, command.Actor, scope.Value);

        if (!authorized.IsSuccess)
        {
            return Result<PrudentialPolicyDraftView>.Failure(authorized.Error!);
        }

        if (!TryCreate(
                PrudentialPolicyVersionId.FromValue(idGenerator.NextId()),
                scope.Value,
                command.Policy,
                unitOfWork.PrudentialPolicies.NextVersion(scope.Value),
                out PrudentialPolicyVersion draft))
        {
            return Result<PrudentialPolicyDraftView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.PrudentialPolicyInvalid);
        }

        unitOfWork.PrudentialPolicies.AddDraft(draft, clock.Now());

        return Result<PrudentialPolicyDraftView>.Success(
            new PrudentialPolicyDraftView(draft.Id, draft.Version));
    }

    private Result<PrudentialPolicyDraftView> UpdateDraft(
        IBankingUnitOfWork unitOfWork,
        UpdatePrudentialPolicyDraftCommand command)
    {
        if (unitOfWork.PrudentialPolicies.Find(command.PrudentialPolicyVersionId) is not { } existing)
        {
            return Result<PrudentialPolicyDraftView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.PrudentialPolicyUnavailable);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(
            unitOfWork, command.Actor, existing.EconomyScopeId);

        if (!authorized.IsSuccess)
        {
            return Result<PrudentialPolicyDraftView>.Failure(authorized.Error!);
        }

        if (!IsDraft(unitOfWork, existing.Id))
        {
            return Result<PrudentialPolicyDraftView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.PrudentialPolicyNotDraft);
        }

        if (!TryCreate(
                existing.Id,
                existing.EconomyScopeId,
                command.Policy,
                existing.Version,
                out PrudentialPolicyVersion replacement))
        {
            return Result<PrudentialPolicyDraftView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.PrudentialPolicyInvalid);
        }

        unitOfWork.PrudentialPolicies.ReplaceDraft(replacement, existing.Version);

        return Result<PrudentialPolicyDraftView>.Success(
            new PrudentialPolicyDraftView(replacement.Id, replacement.Version));
    }

    private Result<PrudentialPolicyVersionView> Publish(
        IBankingUnitOfWork unitOfWork,
        PublishPrudentialPolicyCommand command)
    {
        if (unitOfWork.PrudentialPolicies.Find(command.PrudentialPolicyVersionId) is not { } draft)
        {
            return Result<PrudentialPolicyVersionView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.PrudentialPolicyUnavailable);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(
            unitOfWork, command.Actor, draft.EconomyScopeId);

        if (!authorized.IsSuccess)
        {
            return Result<PrudentialPolicyVersionView>.Failure(authorized.Error!);
        }

        if (!IsDraft(unitOfWork, draft.Id))
        {
            return Result<PrudentialPolicyVersionView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.PrudentialPolicyNotDraft);
        }

        UtcTimestamp now = clock.Now();

        if (unitOfWork.PrudentialPolicies.FindPublished(draft.EconomyScopeId) is { } current)
        {
            unitOfWork.PrudentialPolicies.Retire(current.Id, now);
        }

        unitOfWork.PrudentialPolicies.Publish(draft.Id, now);

        return Result<PrudentialPolicyVersionView>.Success(
            new PrudentialPolicyVersionView(draft.Id, draft.MinimumInitialBankCapital));
    }

    private static bool IsDraft(IBankingUnitOfWork unitOfWork, PrudentialPolicyVersionId id) =>
        string.Equals(unitOfWork.PrudentialPolicies.FindStatus(id), DraftStatus, StringComparison.Ordinal);

    private static bool TryCreate(
        PrudentialPolicyVersionId id,
        EconomyScopeId economyScopeId,
        PrudentialPolicyInput policy,
        long version,
        out PrudentialPolicyVersion created)
    {
        try
        {
            created = PrudentialPolicyVersion.Create(
                id,
                economyScopeId,
                policy.MinimumCet1Bps,
                policy.LendingCet1Bps,
                policy.MinimumLeverageBps,
                policy.ConfiguredWarningLeverageBps,
                policy.MinimumLiquidityBps,
                MoneyMinor.FromMinor(policy.MinimumInitialBankCapitalMinor),
                version);

            return true;
        }
        catch (InvariantViolationException)
        {
            created = default;
            return false;
        }
    }
}
