using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Domain.Banking;

namespace Numera.Discord.Endpoints;

public sealed partial class ManagePanelEndpoints
{
    internal const string ActionPresentation = "presentation-profile";
    internal const string ActionInsuranceScheme = "insurance-scheme";
    internal const string ActionInsuranceState = "insurance-state";
    internal const string ActionIntervention = "intervention";

    internal const string FieldClass = "class";
    internal const string FieldCoverage = "coverage";
    internal const string FieldFee = "fee";
    internal const string FieldPair = "pair";
    internal const string FieldSide = "side";
    internal const string FieldLimits = "limits";
    internal const string FieldSlippage = "slippage";
    internal const string FieldUntil = "until";
    internal const string FieldPalette = "palette";

    [EconomyModal(Sessions.ManagementPanelCatalog.PresentationEditor, typeof(PanelPresentationForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitPresentationAsync(
        DiscordEndpointContext context,
        PanelPresentationForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        string[] colours =
        [
            form.Information, form.Success, form.Warning, form.Error, form.Neutral,
        ];

        if (colours.Any(static value => Rgb(value) is null))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.PresentationProfileColourInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldPalette] = string.Join(',', colours.Select(static value => value.Trim())),
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.InsuranceSchemeEditor, typeof(PanelInsuranceSchemeForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitInsuranceSchemeAsync(
        DiscordEndpointContext context,
        PanelInsuranceSchemeForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        bool coverage = TryAmount(form.CoverageLimit, out long limit);
        bool fee = TryUnsigned(form.EnrollmentFee, out long enrollment);

        if (!coverage || !fee)
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.DepositInsuranceSchemeInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldClass] = form.ProtectionClass.Trim(),
                [FieldCoverage] = limit.ToString(CultureInfo.InvariantCulture),
                [FieldFee] = enrollment.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.InsuranceStateEditor, typeof(PanelInsuranceStateForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitInsuranceStateAsync(
        DiscordEndpointContext context,
        PanelInsuranceStateForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        string desired = form.DesiredState.Trim().ToUpperInvariant();

        if (desired is not ("SUSPENDED" or "ACTIVE" or "RETIRED"))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.DepositInsuranceSchemeInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldClass] = form.ProtectionClass.Trim(),
                [FieldState] = desired,
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.InterventionEditor, typeof(PanelInterventionForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitInterventionAsync(
        DiscordEndpointContext context,
        PanelInterventionForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        string side = form.AllowedSide.Trim().ToUpperInvariant();
        bool limits = Pair(form.Limits) is not null;
        bool slippage = TryUnsigned(form.Slippage, out long bps);
        bool until = TryAmount(form.ValidUntil, out long expiry);

        if (side is not ("BUY_BASE" or "SELL_BASE" or "BOTH") || !limits || !slippage || !until ||
            Codes(form.Pair.Trim().ToUpperInvariant()) is null)
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.FxInterventionMandateInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldPair] = form.Pair.Trim().ToUpperInvariant(),
                [FieldSide] = side,
                [FieldLimits] = form.Limits.Trim(),
                [FieldSlippage] = bps.ToString(CultureInfo.InvariantCulture),
                [FieldUntil] = expiry.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);
    }

    private async Task<Result> PublishPresentationAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        int?[] colours =
        [
            .. Field(payload, FieldPalette).Split(',', StringSplitOptions.TrimEntries).Select(Rgb),
        ];

        if (colours.Length != 5 || colours.Any(static value => value is null))
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.PresentationProfileColourInvalid);
        }

        Result<PresentationProfileDraftView> draft = await presentation
            .StartDraftAsync(
                new StartPresentationProfileDraftCommand(
                    actor,
                    new PresentationProfilePalette(
                        colours[0], colours[1], colours[2], colours[3], colours[4])),
                cancellationToken)
            .ConfigureAwait(false);

        if (!draft.IsSuccess)
        {
            return Result.Failure(draft.Error!);
        }

        Result<PresentationProfileVersionView> published = await presentation
            .PublishAsync(
                new PublishPresentationProfileCommand(actor, draft.Value.Id, draft.Value.Version),
                cancellationToken)
            .ConfigureAwait(false);

        return published.IsSuccess ? Result.Success() : Result.Failure(published.Error!);
    }

    private async Task<Result> PublishInsuranceSchemeAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<DepositInsuranceSchemeStatusView> status = await insurance
            .GetSchemeStatusAsync(
                new GetDepositInsuranceSchemeQuery(actor, Field(payload, FieldClass)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!status.IsSuccess)
        {
            return Result.Failure(status.Error!);
        }

        if (status.Value.FundId is not { } fundId)
        {
            return Result.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceFundNotFound);
        }

        if (!TryAmount(Field(payload, FieldCoverage), out long coverage) ||
            !TryUnsigned(Field(payload, FieldFee), out long fee))
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.DepositInsuranceSchemeInvalid);
        }

        Result<DepositInsuranceSchemeDraftView> draft = await insurance
            .StartDraftAsync(
                new StartDepositInsuranceSchemeDraftCommand(
                    actor,
                    status.Value.CurrencyId,
                    Field(payload, FieldClass),
                    fundId,
                    coverage,
                    fee),
                cancellationToken)
            .ConfigureAwait(false);

        if (!draft.IsSuccess)
        {
            return Result.Failure(draft.Error!);
        }

        Result<DepositInsuranceSchemeVersionView> published = await insurance
            .PublishAsync(
                new PublishDepositInsuranceSchemeCommand(actor, draft.Value.Id), cancellationToken)
            .ConfigureAwait(false);

        return published.IsSuccess ? Result.Success() : Result.Failure(published.Error!);
    }

    private async Task<Result> ChangeInsuranceStateAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<DepositInsuranceSchemeStatusView> status = await insurance
            .GetSchemeStatusAsync(
                new GetDepositInsuranceSchemeQuery(actor, Field(payload, FieldClass)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!status.IsSuccess)
        {
            return Result.Failure(status.Error!);
        }

        if (status.Value.SchemeId is not { } schemeId)
        {
            return Result.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceSchemeNotFound);
        }

        return Field(payload, FieldState) switch
        {
            "SUSPENDED" => await insurance
                .SuspendSchemeAsync(
                    new SuspendDepositInsuranceSchemeCommand(actor, schemeId), cancellationToken)
                .ConfigureAwait(false),
            "RETIRED" => await insurance
                .RetireAsync(
                    new RetireDepositInsuranceSchemeCommand(actor, schemeId), cancellationToken)
                .ConfigureAwait(false),
            _ => await insurance
                .ResumeSchemeAsync(
                    new ResumeDepositInsuranceSchemeCommand(actor, schemeId), cancellationToken)
                .ConfigureAwait(false),
        };
    }

    private async Task<Result> StartInterventionAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        if (Codes(Field(payload, FieldPair)) is not { } pair)
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxInterventionMandateInvalid);
        }

        Result<FxInterventionTargetView> target = await authorities
            .GetInterventionTargetAsync(
                new GetFxInterventionTargetQuery(actor, pair.Base, pair.Quote), cancellationToken)
            .ConfigureAwait(false);

        if (!target.IsSuccess)
        {
            return Result.Failure(target.Error!);
        }

        if (Pair(Field(payload, FieldLimits)) is not { } limits ||
            !TryUnsigned(Field(payload, FieldSlippage), out long slippage) ||
            !TryAmount(Field(payload, FieldUntil), out long until))
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxInterventionMandateInvalid);
        }

        Result<FxInterventionMandateView> mandate = await authorities
            .StartInterventionMandateAsync(
                new StartFxInterventionMandateCommand(
                    actor,
                    target.Value.MarketId,
                    Field(payload, FieldSide),
                    limits.First,
                    limits.Second,
                    (int)slippage,
                    until),
                cancellationToken)
            .ConfigureAwait(false);

        if (!mandate.IsSuccess)
        {
            return Result.Failure(mandate.Error!);
        }

        Result<FxInterventionMandateView> activated = await authorities
            .ActivateInterventionMandateAsync(
                new ActivateFxInterventionMandateCommand(actor, mandate.Value.Id), cancellationToken)
            .ConfigureAwait(false);

        return activated.IsSuccess ? Result.Success() : Result.Failure(activated.Error!);
    }

    private async Task<string?> PolicyCurrentAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        switch (payload.Action)
        {
            case ActionPresentation:
            {
                Result<PresentationProfileStatusView> status = await presentation
                    .GetProfileStatusAsync(new GetPresentationProfileQuery(actor), cancellationToken)
                    .ConfigureAwait(false);

                return status is { IsSuccess: true, Value.Palette: { } palette }
                    ? string.Join(
                        ',',
                        new[]
                        {
                            palette.InformationRgb, palette.SuccessRgb, palette.WarningRgb,
                            palette.ErrorRgb, palette.NeutralRgb,
                        }.Select(static value => value?.ToString("X6", CultureInfo.InvariantCulture) ?? "-"))
                    : null;
            }

            case ActionInsuranceScheme:
            case ActionInsuranceState:
            {
                Result<DepositInsuranceSchemeStatusView> status = await insurance
                    .GetSchemeStatusAsync(
                        new GetDepositInsuranceSchemeQuery(actor, Field(payload, FieldClass)),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!status.IsSuccess || !status.Value.HasScheme)
                {
                    return null;
                }

                return catalog.Resolve(
                        Rendering.ViewKeys.StatusOf(status.Value.Status!.Value.ToToken()))
                    + " " + status.Value.CoverageLimitMinor.ToString(CultureInfo.InvariantCulture)
                    + "," + status.Value.EnrollmentFeeMinor.ToString(CultureInfo.InvariantCulture);
            }

            case ActionIntervention:
            {
                if (Codes(Field(payload, FieldPair)) is not { } pair)
                {
                    return null;
                }

                Result<FxInterventionTargetView> target = await authorities
                    .GetInterventionTargetAsync(
                        new GetFxInterventionTargetQuery(actor, pair.Base, pair.Quote),
                        cancellationToken)
                    .ConfigureAwait(false);

                return target.IsSuccess
                    ? target.Value.PairCode + " "
                        + catalog.Resolve(
                            Rendering.ViewKeys.StatusOf(target.Value.AuthorityStatus.ToToken()))
                    : null;
            }

            default:
                return null;
        }
    }

    private static (string Base, string Quote)? Codes(string pair)
    {
        string[] parts = pair.Split('/', StringSplitOptions.TrimEntries);

        return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0
            ? (parts[0], parts[1])
            : null;
    }

    private static int? Rgb(string text)
    {
        string trimmed = text.Trim().TrimStart('#');

        return trimmed.Length == 6
            && int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    private static bool TryUnsigned(string text, out long value) =>
        long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
