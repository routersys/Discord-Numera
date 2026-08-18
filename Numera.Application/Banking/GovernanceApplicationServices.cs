using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
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
    PresentationProfileVersionId PresentationProfileVersionId);

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

public interface IPresentationProfileAdministrationApplicationService
{
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
        unitOfWork.Governance.AddPresentationProfile(draft);

        return Result<PresentationProfileDraftView>.Success(
            new PresentationProfileDraftView(draft.Id, draft.Status, draft.Version));
    }

    private static Result<PresentationProfileDraftView> UpdateDraft(
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

        unitOfWork.Governance.UpdatePresentationProfile(updated);

        return Result<PresentationProfileDraftView>.Success(
            new PresentationProfileDraftView(updated.Id, updated.Status, updated.Version));
    }

    private static Result<PresentationProfileVersionView> Publish(
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

        if (unitOfWork.Governance.FindPublishedPresentationProfile(
                draft.EconomyScopeId, draft.BankId) is { } current)
        {
            PresentationProfileStatusCatalog.EnsureTransition(
                current.Status, PresentationProfileVersionStatus.Retired);

            unitOfWork.Governance.UpdatePresentationProfile(current with
            {
                Status = PresentationProfileVersionStatus.Retired,
                Version = current.Version + 1,
            });
        }

        PresentationProfileStatusCatalog.EnsureTransition(
            draft.Status, PresentationProfileVersionStatus.Published);

        PresentationProfileRecord published = draft with
        {
            Status = PresentationProfileVersionStatus.Published,
            Version = draft.Version + 1,
        };

        unitOfWork.Governance.UpdatePresentationProfile(published);

        return Result<PresentationProfileVersionView>.Success(new PresentationProfileVersionView(
            published.Id, published.EconomyScopeId, published.BankId, published.Status));
    }

    private static Result<bool> Retire(
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

        unitOfWork.Governance.UpdatePresentationProfile(profile with
        {
            Status = PresentationProfileVersionStatus.Retired,
            Version = profile.Version + 1,
        });

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
