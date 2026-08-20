using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record PresentationProfilePalette(
    int? InformationRgb,
    int? SuccessRgb,
    int? WarningRgb,
    int? ErrorRgb,
    int? NeutralRgb);

public sealed record StartPresentationProfileDraftCommand(
    AuthorizationContext Actor,
    PresentationProfilePalette Palette,
    BankId? BankId = null);

public sealed record UpdatePresentationProfileDraftCommand(
    AuthorizationContext Actor,
    PresentationProfileVersionId PresentationProfileVersionId,
    PresentationProfilePalette Palette);

public sealed record PreviewPresentationProfileQuery(
    AuthorizationContext Actor,
    PresentationProfileVersionId PresentationProfileVersionId);

public sealed record PublishPresentationProfileCommand(
    AuthorizationContext Actor,
    PresentationProfileVersionId PresentationProfileVersionId,
    long ExpectedVersion);

public sealed record RetirePresentationProfileCommand(
    AuthorizationContext Actor,
    PresentationProfileVersionId PresentationProfileVersionId);

public sealed record PresentationProfileDraftView(
    PresentationProfileVersionId Id,
    PresentationProfileVersionStatus Status,
    long Version);

public sealed record PresentationProfilePreviewView(
    PresentationProfileVersionId Id,
    PresentationProfilePalette Palette);

public sealed record PresentationProfileVersionView(
    PresentationProfileVersionId Id,
    EconomyScopeId EconomyScopeId,
    BankId? BankId,
    PresentationProfileVersionStatus Status);

public sealed record GetPresentationProfileQuery(AuthorizationContext Actor);

public sealed record PresentationProfileStatusView(
    EconomyScopeId EconomyScopeId,
    bool HasPublished,
    PresentationProfilePalette? Palette,
    PresentationProfileVersionId? VersionId,
    long Version);

public interface IPresentationProfileAdministrationApplicationService
{
    Task<Result<PresentationProfileStatusView>> GetProfileStatusAsync(
        GetPresentationProfileQuery query,
        CancellationToken cancellationToken);

    Task<Result<PresentationProfileDraftView>> StartDraftAsync(
        StartPresentationProfileDraftCommand command,
        CancellationToken cancellationToken);

    Task<Result<PresentationProfileDraftView>> UpdateDraftAsync(
        UpdatePresentationProfileDraftCommand command,
        CancellationToken cancellationToken);

    Task<Result<PresentationProfilePreviewView>> PreviewAsync(
        PreviewPresentationProfileQuery query,
        CancellationToken cancellationToken);

    Task<Result<PresentationProfileVersionView>> PublishAsync(
        PublishPresentationProfileCommand command,
        CancellationToken cancellationToken);

    Task<Result> RetireAsync(
        RetirePresentationProfileCommand command,
        CancellationToken cancellationToken);
}

public sealed class PresentationProfileAdministrationApplicationService
    : IPresentationProfileAdministrationApplicationService
{
    public const string ProfilePublishOperationType = "PRESENTATION_PROFILE_PUBLISH";
    public const string ProfilePublishedEventType = "PRESENTATION_PROFILE_PUBLISHED";

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public PresentationProfileAdministrationApplicationService(
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

    public Task<Result<PresentationProfileStatusView>> GetProfileStatusAsync(
        GetPresentationProfileQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork => ProfileStatus(unitOfWork, query), cancellationToken);
    }

    private static Result<PresentationProfileStatusView> ProfileStatus(
        IBankingUnitOfWork unitOfWork,
        GetPresentationProfileQuery query)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, query.Actor);

        if (!scope.IsSuccess)
        {
            return Result<PresentationProfileStatusView>.Failure(scope.Error!);
        }

        if (unitOfWork.Governance.FindPublishedPresentationProfile(scope.Value, null)
            is not { } profile)
        {
            return Result<PresentationProfileStatusView>.Success(
                new PresentationProfileStatusView(scope.Value, false, null, null, 0L));
        }

        return Result<PresentationProfileStatusView>.Success(new PresentationProfileStatusView(
            scope.Value,
            true,
            new PresentationProfilePalette(
                profile.InformationRgb,
                profile.SuccessRgb,
                profile.WarningRgb,
                profile.ErrorRgb,
                profile.NeutralRgb),
            profile.Id,
            profile.Version));
    }

    public Task<Result<PresentationProfileDraftView>> StartDraftAsync(
        StartPresentationProfileDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => StartDraft(unitOfWork, command), cancellationToken);
    }

    public Task<Result<PresentationProfileDraftView>> UpdateDraftAsync(
        UpdatePresentationProfileDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => UpdateDraft(unitOfWork, command), cancellationToken);
    }

    public Task<Result<PresentationProfilePreviewView>> PreviewAsync(
        PreviewPresentationProfileQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork => unitOfWork.Governance.FindPresentationProfile(
                query.PresentationProfileVersionId) is { } profile
                ? Result<PresentationProfilePreviewView>.Success(new PresentationProfilePreviewView(
                    profile.Id,
                    new PresentationProfilePalette(
                        profile.InformationRgb,
                        profile.SuccessRgb,
                        profile.WarningRgb,
                        profile.ErrorRgb,
                        profile.NeutralRgb)))
                : Result<PresentationProfilePreviewView>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.PresentationProfileNotFound),
            cancellationToken);
    }

    public Task<Result<PresentationProfileVersionView>> PublishAsync(
        PublishPresentationProfileCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Publish(unitOfWork, command), cancellationToken);
    }

    public async Task<Result> RetireAsync(
        RetirePresentationProfileCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<bool> outcome = await writeGateway
            .ExecuteAsync(unitOfWork => Retire(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    private Result<PresentationProfileDraftView> StartDraft(
        IBankingUnitOfWork unitOfWork,
        StartPresentationProfileDraftCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<PresentationProfileDraftView>.Failure(scope.Error!);
        }

        if (!IsPaletteValid(command.Palette))
        {
            return Result<PresentationProfileDraftView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.PresentationProfilePaletteInvalid);
        }

        PresentationProfileRecord draft = new(
            PresentationProfileVersionId.FromValue(idGenerator.NextId()),
            scope.Value,
            command.BankId,
            command.Palette.InformationRgb,
            command.Palette.SuccessRgb,
            command.Palette.WarningRgb,
            command.Palette.ErrorRgb,
            command.Palette.NeutralRgb,
            PresentationProfileVersionStatus.Draft,
            1);

        PresentationProfileStatusCatalog.EnsureCreatable(draft.Status);
        unitOfWork.Governance.AddPresentationProfile(draft, clock.Now());

        return Result<PresentationProfileDraftView>.Success(
            new PresentationProfileDraftView(draft.Id, draft.Status, draft.Version));
    }

    private Result<PresentationProfileDraftView> UpdateDraft(
        IBankingUnitOfWork unitOfWork,
        UpdatePresentationProfileDraftCommand command)
    {
        Result<PresentationProfileRecord> resolved =
            ResolveDraft(unitOfWork, command.Actor, command.PresentationProfileVersionId);

        if (!resolved.IsSuccess)
        {
            return Result<PresentationProfileDraftView>.Failure(resolved.Error!);
        }

        if (!IsPaletteValid(command.Palette))
        {
            return Result<PresentationProfileDraftView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.PresentationProfilePaletteInvalid);
        }

        PresentationProfileRecord updated = resolved.Value with
        {
            InformationRgb = command.Palette.InformationRgb,
            SuccessRgb = command.Palette.SuccessRgb,
            WarningRgb = command.Palette.WarningRgb,
            ErrorRgb = command.Palette.ErrorRgb,
            NeutralRgb = command.Palette.NeutralRgb,
            Version = resolved.Value.Version + 1,
        };

        unitOfWork.Governance.UpdatePresentationProfile(updated, clock.Now());

        return Result<PresentationProfileDraftView>.Success(
            new PresentationProfileDraftView(updated.Id, updated.Status, updated.Version));
    }

    private Result<PresentationProfileVersionView> Publish(
        IBankingUnitOfWork unitOfWork,
        PublishPresentationProfileCommand command)
    {
        Result<PresentationProfileRecord> resolved =
            ResolveDraft(unitOfWork, command.Actor, command.PresentationProfileVersionId);

        if (!resolved.IsSuccess)
        {
            return Result<PresentationProfileVersionView>.Failure(resolved.Error!);
        }

        PresentationProfileRecord draft = resolved.Value;

        if (draft.Version != command.ExpectedVersion)
        {
            return Result<PresentationProfileVersionView>.Failure(
                ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        if (!IsPublishable(draft))
        {
            return Result<PresentationProfileVersionView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.PresentationProfilePaletteInvalid);
        }

        UtcTimestamp now = clock.Now();

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            ProfilePublishOperationType,
            draft.EconomyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            IdempotencyKey.Create(
                ProfilePublishOperationType,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{draft.Id.Value}:{command.ExpectedVersion}")),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        if (unitOfWork.Governance.FindPublishedPresentationProfile(
                draft.EconomyScopeId, draft.BankId) is { } current)
        {
            PresentationProfileStatusCatalog.EnsureTransition(
                current.Status, PresentationProfileVersionStatus.Retired);

            unitOfWork.Governance.UpdatePresentationProfile(
                current with
                {
                    Status = PresentationProfileVersionStatus.Retired,
                    Version = current.Version + 1,
                },
                now);
        }

        PresentationProfileStatusCatalog.EnsureTransition(
            draft.Status, PresentationProfileVersionStatus.Published);

        PresentationProfileRecord published = draft with
        {
            Status = PresentationProfileVersionStatus.Published,
            Version = draft.Version + 1,
        };

        unitOfWork.Governance.UpdatePresentationProfile(published, now);

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            operation.Id,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            ProfilePublishOperationType,
            "presentation_profile_version",
            published.Id.Value,
            reason: null,
            now);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            ProfilePublishedEventType,
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"presentation_profile_version_id":"{{published.Id.Value}}"}"""),
            now));

        return Result<PresentationProfileVersionView>.Success(new PresentationProfileVersionView(
            published.Id, published.EconomyScopeId, published.BankId, published.Status));
    }

    private static bool IsPublishable(PresentationProfileRecord profile) =>
        IsChannelValid(profile.InformationRgb)
        && IsChannelValid(profile.SuccessRgb)
        && IsChannelValid(profile.WarningRgb)
        && IsChannelValid(profile.ErrorRgb)
        && IsChannelValid(profile.NeutralRgb);

    private static bool IsChannelValid(int? channel) =>
        channel is null || channel is >= 0 and <= 0xFFFFFF;

    private Result<bool> Retire(
        IBankingUnitOfWork unitOfWork,
        RetirePresentationProfileCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<bool>.Failure(scope.Error!);
        }

        if (unitOfWork.Governance.FindPresentationProfile(
                command.PresentationProfileVersionId) is not { } profile
            || profile.EconomyScopeId != scope.Value)
        {
            return Result<bool>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.PresentationProfileNotFound);
        }

        try
        {
            PresentationProfileStatusCatalog.EnsureTransition(
                profile.Status, PresentationProfileVersionStatus.Retired);
        }
        catch (InvariantViolationException)
        {
            return Result<bool>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.PresentationProfileNotRetirable);
        }

        unitOfWork.Governance.UpdatePresentationProfile(
            profile with
            {
                Status = PresentationProfileVersionStatus.Retired,
                Version = profile.Version + 1,
            },
            clock.Now());

        return Result<bool>.Success(true);
    }

    private static Result<PresentationProfileRecord> ResolveDraft(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        PresentationProfileVersionId id)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, actor);

        if (!scope.IsSuccess)
        {
            return Result<PresentationProfileRecord>.Failure(scope.Error!);
        }

        if (unitOfWork.Governance.FindPresentationProfile(id) is not { } profile
            || profile.EconomyScopeId != scope.Value)
        {
            return Result<PresentationProfileRecord>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.PresentationProfileNotFound);
        }

        return profile.Status == PresentationProfileVersionStatus.Draft
            ? Result<PresentationProfileRecord>.Success(profile)
            : Result<PresentationProfileRecord>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.PresentationProfileNotDraft);
    }

    private static bool IsPaletteValid(PresentationProfilePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        int?[] channels =
        [
            palette.InformationRgb,
            palette.SuccessRgb,
            palette.WarningRgb,
            palette.ErrorRgb,
            palette.NeutralRgb,
        ];

        return channels.All(static value => value is null or (>= 0 and <= 0xFFFFFF));
    }
}

internal static class GovernanceAuthorization
{
    internal static Result<EconomyScopeId> Authorise(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor)
    {
        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(unitOfWork, actor, requested: null);

        if (!scope.IsSuccess)
        {
            return scope;
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(unitOfWork, actor, scope.Value);

        return authorized.IsSuccess ? scope : Result<EconomyScopeId>.Failure(authorized.Error!);
    }

    internal static bool IsSystemOwner(IBankingUnitOfWork unitOfWork, AuthorizationContext actor) =>
        unitOfWork.SystemOwners.Contains(
            actor.DiscordUserId.ToString(CultureInfo.InvariantCulture));
}
