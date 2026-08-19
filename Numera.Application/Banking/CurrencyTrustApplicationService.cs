using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record CurrencyTrustTierThresholds(
    long MinimumAgeSeconds,
    int MinimumTradeDays,
    int MinimumCounterparties);

public sealed record CurrencyTrustPolicyInput(
    CurrencyTrustTierThresholds Established,
    CurrencyTrustTierThresholds Trusted,
    CurrencyTrustTierThresholds ReserveEligible);

public sealed record StartCurrencyTrustPolicyDraftCommand(
    AuthorizationContext Actor,
    CurrencyTrustPolicyInput Policy);

public sealed record UpdateCurrencyTrustPolicyDraftCommand(
    AuthorizationContext Actor,
    CurrencyTrustPolicyVersionId CurrencyTrustPolicyVersionId,
    CurrencyTrustPolicyInput Policy);

public sealed record PublishCurrencyTrustPolicyCommand(
    AuthorizationContext Actor,
    CurrencyTrustPolicyVersionId CurrencyTrustPolicyVersionId);

public sealed record AssessCurrencyTrustQuery(AuthorizationContext Actor, CurrencyId CurrencyId);

public sealed record PublishCurrencyTrustDesignationCommand(
    AuthorizationContext Actor,
    CurrencyId CurrencyId,
    CurrencyTrustTier Tier);

public sealed record SetCurrencyTrustDesignationStateCommand(
    AuthorizationContext Actor,
    CurrencyTrustDesignationId CurrencyTrustDesignationId,
    CurrencyTrustDesignationStatus DesiredStatus);

public sealed record CurrencyTrustPolicyDraftView(
    CurrencyTrustPolicyVersionId Id,
    CurrencyTrustPolicyVersionStatus Status,
    long Version);

public sealed record CurrencyTrustPolicyVersionView(
    CurrencyTrustPolicyVersionId Id,
    EconomyScopeId EconomyScopeId,
    CurrencyTrustPolicyVersionStatus Status);

public sealed record CurrencyTrustAssessmentView(
    CurrencyId CurrencyId,
    CurrencyTrustTier QualifiedTier,
    CurrencyTrustTier? CurrentTier);

public sealed record CurrencyTrustDesignationView(
    CurrencyTrustDesignationId Id,
    CurrencyId CurrencyId,
    CurrencyTrustTier Tier,
    CurrencyTrustDesignationStatus Status);

public interface ICurrencyTrustAdministrationApplicationService
{
    Task<Result<CurrencyTrustPolicyDraftView>> StartPolicyDraftAsync(
        StartCurrencyTrustPolicyDraftCommand command,
        CancellationToken cancellationToken);

    Task<Result<CurrencyTrustPolicyDraftView>> UpdatePolicyDraftAsync(
        UpdateCurrencyTrustPolicyDraftCommand command,
        CancellationToken cancellationToken);

    Task<Result<CurrencyTrustPolicyVersionView>> PublishPolicyAsync(
        PublishCurrencyTrustPolicyCommand command,
        CancellationToken cancellationToken);

    Task<Result<CurrencyTrustAssessmentView>> AssessAsync(
        AssessCurrencyTrustQuery query,
        CancellationToken cancellationToken);

    Task<Result<CurrencyTrustDesignationView>> PublishDesignationAsync(
        PublishCurrencyTrustDesignationCommand command,
        CancellationToken cancellationToken);

    Task<Result<CurrencyTrustDesignationView>> SetDesignationStateAsync(
        SetCurrencyTrustDesignationStateCommand command,
        CancellationToken cancellationToken);
}

public sealed class CurrencyTrustAdministrationApplicationService
    : ICurrencyTrustAdministrationApplicationService
{
    public const long EstablishedFloorSeconds = 604_800;
    public const long TrustedFloorSeconds = 2_592_000;
    public const long ReserveFloorSeconds = 7_776_000;

    public const string DesignationOperationType = "CURRENCY_TRUST_DESIGNATION";

    public const string DesignationTargetType = "CURRENCY_TRUST_DESIGNATION";

    public const string DesignationEventType = "CURRENCY_TRUST_DESIGNATED";

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public CurrencyTrustAdministrationApplicationService(
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

    public Task<Result<CurrencyTrustPolicyDraftView>> StartPolicyDraftAsync(
        StartCurrencyTrustPolicyDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => StartDraft(unitOfWork, command), cancellationToken);
    }

    public Task<Result<CurrencyTrustPolicyDraftView>> UpdatePolicyDraftAsync(
        UpdateCurrencyTrustPolicyDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => UpdateDraft(unitOfWork, command), cancellationToken);
    }

    public Task<Result<CurrencyTrustPolicyVersionView>> PublishPolicyAsync(
        PublishCurrencyTrustPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => PublishPolicy(unitOfWork, command), cancellationToken);
    }

    public Task<Result<CurrencyTrustAssessmentView>> AssessAsync(
        AssessCurrencyTrustQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(unitOfWork => Assess(unitOfWork, query), cancellationToken);
    }

    public Task<Result<CurrencyTrustDesignationView>> PublishDesignationAsync(
        PublishCurrencyTrustDesignationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => PublishDesignation(unitOfWork, command), cancellationToken);
    }

    public Task<Result<CurrencyTrustDesignationView>> SetDesignationStateAsync(
        SetCurrencyTrustDesignationStateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => SetDesignationState(unitOfWork, command), cancellationToken);
    }

    internal static CurrencyTrustTier Qualify(
        CurrencyTrustPolicyRecord policy,
        long ageSeconds,
        int tradeDays,
        int counterparties)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (ageSeconds >= policy.ReserveMinAgeSeconds
            && tradeDays >= policy.ReserveMinTradeDays
            && counterparties >= policy.ReserveMinCounterparties)
        {
            return CurrencyTrustTier.ReserveEligible;
        }

        if (ageSeconds >= policy.TrustedMinAgeSeconds
            && tradeDays >= policy.TrustedMinTradeDays
            && counterparties >= policy.TrustedMinCounterparties)
        {
            return CurrencyTrustTier.Trusted;
        }

        if (ageSeconds >= policy.EstablishedMinAgeSeconds
            && tradeDays >= policy.EstablishedMinTradeDays
            && counterparties >= policy.EstablishedMinCounterparties)
        {
            return CurrencyTrustTier.Established;
        }

        return CurrencyTrustTier.Experimental;
    }

    internal static bool IsWithinCanonicalFloors(CurrencyTrustPolicyInput policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return policy.Established.MinimumAgeSeconds >= EstablishedFloorSeconds
            && policy.Established.MinimumTradeDays >= 3
            && policy.Established.MinimumCounterparties >= 2
            && policy.Trusted.MinimumAgeSeconds >= TrustedFloorSeconds
            && policy.Trusted.MinimumTradeDays >= 10
            && policy.Trusted.MinimumCounterparties >= 3
            && policy.ReserveEligible.MinimumAgeSeconds >= ReserveFloorSeconds
            && policy.ReserveEligible.MinimumTradeDays >= 30
            && policy.ReserveEligible.MinimumCounterparties >= 5;
    }

    private Result<CurrencyTrustPolicyDraftView> StartDraft(
        IBankingUnitOfWork unitOfWork,
        StartCurrencyTrustPolicyDraftCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<CurrencyTrustPolicyDraftView>.Failure(scope.Error!);
        }

        if (!IsWithinCanonicalFloors(command.Policy))
        {
            return Result<CurrencyTrustPolicyDraftView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyTrustPolicyInvalid);
        }

        CurrencyTrustPolicyRecord draft = Compose(
            CurrencyTrustPolicyVersionId.FromValue(idGenerator.NextId()),
            scope.Value,
            command.Policy,
            CurrencyTrustPolicyVersionStatus.Draft,
            unitOfWork.Governance.NextTrustPolicyVersion(scope.Value));

        CurrencyTrustPolicyStatusCatalog.EnsureCreatable(draft.Status);
        unitOfWork.Governance.AddTrustPolicy(draft);

        return Result<CurrencyTrustPolicyDraftView>.Success(
            new CurrencyTrustPolicyDraftView(draft.Id, draft.Status, draft.Version));
    }

    private static Result<CurrencyTrustPolicyDraftView> UpdateDraft(
        IBankingUnitOfWork unitOfWork,
        UpdateCurrencyTrustPolicyDraftCommand command)
    {
        Result<CurrencyTrustPolicyRecord> resolved =
            ResolveDraft(unitOfWork, command.Actor, command.CurrencyTrustPolicyVersionId);

        if (!resolved.IsSuccess)
        {
            return Result<CurrencyTrustPolicyDraftView>.Failure(resolved.Error!);
        }

        if (!IsWithinCanonicalFloors(command.Policy))
        {
            return Result<CurrencyTrustPolicyDraftView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyTrustPolicyInvalid);
        }

        CurrencyTrustPolicyRecord updated = Compose(
            resolved.Value.Id,
            resolved.Value.EconomyScopeId,
            command.Policy,
            CurrencyTrustPolicyVersionStatus.Draft,
            resolved.Value.Version);

        unitOfWork.Governance.UpdateTrustPolicy(updated);

        return Result<CurrencyTrustPolicyDraftView>.Success(
            new CurrencyTrustPolicyDraftView(updated.Id, updated.Status, updated.Version));
    }

    private static Result<CurrencyTrustPolicyVersionView> PublishPolicy(
        IBankingUnitOfWork unitOfWork,
        PublishCurrencyTrustPolicyCommand command)
    {
        Result<CurrencyTrustPolicyRecord> resolved =
            ResolveDraft(unitOfWork, command.Actor, command.CurrencyTrustPolicyVersionId);

        if (!resolved.IsSuccess)
        {
            return Result<CurrencyTrustPolicyVersionView>.Failure(resolved.Error!);
        }

        CurrencyTrustPolicyRecord draft = resolved.Value;

        if (unitOfWork.Governance.FindPublishedTrustPolicy(draft.EconomyScopeId) is { } current)
        {
            CurrencyTrustPolicyStatusCatalog.EnsureTransition(
                current.Status, CurrencyTrustPolicyVersionStatus.Retired);

            unitOfWork.Governance.UpdateTrustPolicy(current with
            {
                Status = CurrencyTrustPolicyVersionStatus.Retired,
            });
        }

        CurrencyTrustPolicyStatusCatalog.EnsureTransition(
            draft.Status, CurrencyTrustPolicyVersionStatus.Published);

        CurrencyTrustPolicyRecord published = draft with
        {
            Status = CurrencyTrustPolicyVersionStatus.Published,
        };

        unitOfWork.Governance.UpdateTrustPolicy(published);

        return Result<CurrencyTrustPolicyVersionView>.Success(new CurrencyTrustPolicyVersionView(
            published.Id, published.EconomyScopeId, published.Status));
    }

    private Result<CurrencyTrustAssessmentView> Assess(
        IBankingUnitOfWork unitOfWork,
        AssessCurrencyTrustQuery query)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, query.Actor);

        if (!scope.IsSuccess)
        {
            return Result<CurrencyTrustAssessmentView>.Failure(scope.Error!);
        }

        if (unitOfWork.Governance.FindPublishedTrustPolicy(scope.Value) is not { } policy)
        {
            return Result<CurrencyTrustAssessmentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyTrustPolicyNotPublished);
        }

        if (Observe(unitOfWork, scope.Value, query.CurrencyId) is not { } observation)
        {
            return Result<CurrencyTrustAssessmentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyNotFound);
        }

        return Result<CurrencyTrustAssessmentView>.Success(new CurrencyTrustAssessmentView(
            query.CurrencyId,
            Qualify(
                policy,
                observation.AgeSeconds,
                observation.TradeDays,
                observation.Counterparties),
            unitOfWork.Governance.FindCurrentTrustDesignation(query.CurrencyId)?.Tier));
    }

    private Result<CurrencyTrustDesignationView> PublishDesignation(
        IBankingUnitOfWork unitOfWork,
        PublishCurrencyTrustDesignationCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<CurrencyTrustDesignationView>.Failure(scope.Error!);
        }

        if (command.Tier.RequiresSystemOwnerApproval()
            && !GovernanceAuthorization.IsSystemOwner(unitOfWork, command.Actor))
        {
            return Result<CurrencyTrustDesignationView>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.CurrencyTrustApprovalRequired);
        }

        if (unitOfWork.Governance.FindPublishedTrustPolicy(scope.Value) is not { } policy)
        {
            return Result<CurrencyTrustDesignationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyTrustPolicyNotPublished);
        }

        UtcTimestamp now = clock.Now();

        if (Observe(unitOfWork, scope.Value, command.CurrencyId) is not { } observation)
        {
            return Result<CurrencyTrustDesignationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyNotFound);
        }

        if (observation.UnresolvedIssues > 0)
        {
            return Result<CurrencyTrustDesignationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CurrencyTrustIntegrityBlocked);
        }

        CurrencyTrustTier qualified = Qualify(
            policy, observation.AgeSeconds, observation.TradeDays, observation.Counterparties);

        if (command.Tier > qualified)
        {
            return Result<CurrencyTrustDesignationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CurrencyTrustTierNotQualified);
        }

        if (unitOfWork.Governance.FindCurrentTrustDesignation(command.CurrencyId) is { } current)
        {
            CurrencyTrustDesignationStatusCatalog.EnsureTransition(
                current.Status, CurrencyTrustDesignationStatus.Superseded);

            unitOfWork.Governance.UpdateTrustDesignation(current with
            {
                Status = CurrencyTrustDesignationStatus.Superseded,
                Version = current.Version + 1,
            });
        }

        CurrencyTrustDesignationId designationId =
            CurrencyTrustDesignationId.FromValue(idGenerator.NextId());

        AuthorizationDecisionId decisionId = AuthorizationDecisionId.FromValue(idGenerator.NextId());

        unitOfWork.AuthorizationDecisions.Add(new AuthorizationDecisionRecord(
            decisionId,
            DesignationTargetType,
            designationId.Value,
            command.Actor.GuildId.ToString(CultureInfo.InvariantCulture),
            GovernanceAuthorization.IsSystemOwner(unitOfWork, command.Actor)
                ? FxAdministrationApplicationService.SystemOwnerAuthority
                : FxAdministrationApplicationService.GuildOperatorAuthority,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            ActorCustomerAccountId: null,
            FxAdministrationApplicationService.ApproveDecision,
            ReasonCode: null,
            now));

        CurrencyTrustDesignationRecord designation = new(
            designationId,
            command.CurrencyId,
            policy.Id,
            command.Tier,
            CurrencyTrustDesignationStatus.Active,
            observation.AgeSeconds,
            observation.TradeDays,
            observation.Counterparties,
            command.Tier == CurrencyTrustTier.Experimental ? null : decisionId,
            now,
            1);

        CurrencyTrustDesignationStatusCatalog.EnsureCreatable(designation.Status);
        unitOfWork.Governance.AddTrustDesignation(designation);

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            DesignationOperationType,
            scope.Value,
            actorPartyId: null,
            idGenerator.NextId(),
            IdempotencyKey.Create(
                DesignationOperationType,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{command.CurrencyId.Value}:{designation.Id.Value}")),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            operation.Id,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            DesignationOperationType,
            DesignationTargetType,
            designation.Id.Value,
            command.Tier.ToToken(),
            now);

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            DesignationEventType,
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"currency_id":"{{command.CurrencyId.Value}}","tier":"{{command.Tier.ToToken()}}"}"""),
            now));

        return Result<CurrencyTrustDesignationView>.Success(new CurrencyTrustDesignationView(
            designation.Id, designation.CurrencyId, designation.Tier, designation.Status));
    }

    internal readonly record struct TrustObservation(
        long AgeSeconds,
        int TradeDays,
        int Counterparties,
        long UnresolvedIssues);

    internal static TrustObservation? Observe(
        IBankingUnitOfWork unitOfWork,
        EconomyScopeId economyScopeId,
        CurrencyId currencyId,
        IClock clock)
    {
        if (unitOfWork.Currencies.Find(currencyId) is not { } currency)
        {
            return null;
        }

        FxTradingObservation trading = unitOfWork.Fx.ObserveTrading(currencyId);

        return new TrustObservation(
            Math.Max(
                0L,
                (clock.Now().UnixMilliseconds - currency.CreatedAt.UnixMilliseconds) / 1000),
            trading.TradeDays,
            trading.DistinctCounterparties,
            unitOfWork.Reconciliation.CountUnresolvedIssues(economyScopeId));
    }

    private TrustObservation? Observe(
        IBankingUnitOfWork unitOfWork,
        EconomyScopeId economyScopeId,
        CurrencyId currencyId) => Observe(unitOfWork, economyScopeId, currencyId, clock);

    private static Result<CurrencyTrustDesignationView> SetDesignationState(
        IBankingUnitOfWork unitOfWork,
        SetCurrencyTrustDesignationStateCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<CurrencyTrustDesignationView>.Failure(scope.Error!);
        }

        if (unitOfWork.Governance.FindTrustDesignation(
                command.CurrencyTrustDesignationId) is not { } designation)
        {
            return Result<CurrencyTrustDesignationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyTrustDesignationNotFound);
        }

        if (designation.Status == command.DesiredStatus)
        {
            return Result<CurrencyTrustDesignationView>.Success(new CurrencyTrustDesignationView(
                designation.Id, designation.CurrencyId, designation.Tier, designation.Status));
        }

        try
        {
            CurrencyTrustDesignationStatusCatalog.EnsureTransition(
                designation.Status, command.DesiredStatus);
        }
        catch (InvariantViolationException)
        {
            return Result<CurrencyTrustDesignationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CurrencyTrustDesignationStateInvalid);
        }

        CurrencyTrustDesignationRecord updated = designation with
        {
            Status = command.DesiredStatus,
            Version = designation.Version + 1,
        };

        unitOfWork.Governance.UpdateTrustDesignation(updated);

        return Result<CurrencyTrustDesignationView>.Success(new CurrencyTrustDesignationView(
            updated.Id, updated.CurrencyId, updated.Tier, updated.Status));
    }

    private static Result<CurrencyTrustPolicyRecord> ResolveDraft(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        CurrencyTrustPolicyVersionId id)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, actor);

        if (!scope.IsSuccess)
        {
            return Result<CurrencyTrustPolicyRecord>.Failure(scope.Error!);
        }

        if (unitOfWork.Governance.FindTrustPolicy(id) is not { } policy
            || policy.EconomyScopeId != scope.Value)
        {
            return Result<CurrencyTrustPolicyRecord>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyTrustPolicyNotFound);
        }

        return policy.Status == CurrencyTrustPolicyVersionStatus.Draft
            ? Result<CurrencyTrustPolicyRecord>.Success(policy)
            : Result<CurrencyTrustPolicyRecord>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CurrencyTrustPolicyNotDraft);
    }

    private static CurrencyTrustPolicyRecord Compose(
        CurrencyTrustPolicyVersionId id,
        EconomyScopeId scope,
        CurrencyTrustPolicyInput policy,
        CurrencyTrustPolicyVersionStatus status,
        long version) =>
        new(
            id,
            scope,
            policy.Established.MinimumAgeSeconds,
            policy.Established.MinimumTradeDays,
            policy.Established.MinimumCounterparties,
            policy.Trusted.MinimumAgeSeconds,
            policy.Trusted.MinimumTradeDays,
            policy.Trusted.MinimumCounterparties,
            policy.ReserveEligible.MinimumAgeSeconds,
            policy.ReserveEligible.MinimumTradeDays,
            policy.ReserveEligible.MinimumCounterparties,
            status,
            version);
}
