using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Domain.Banking;

namespace Numera.Discord.Endpoints;

public sealed partial class ManagePanelEndpoints
{
    internal const string ActionOperatorGrant = "operator-grant";
    internal const string ActionFeeSchedule = "fee-schedule";
    internal const string ActionAccountReview = "account-review";
    internal const string ActionBankDesign = "bank-design";

    internal const string FieldUser = "user";
    internal const string FieldType = "type";
    internal const string FieldAmounts = "amounts";
    internal const string FieldBounds = "bounds";
    internal const string FieldFree = "free";

    private const string GrantToken = "GRANT";

    [EconomyModal(Sessions.ManagementPanelCatalog.OperatorGrantEditor, typeof(PanelOperatorGrantForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitOperatorGrantAsync(
        DiscordEndpointContext context,
        PanelOperatorGrantForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        string desired = form.DesiredState.Trim().ToUpperInvariant();
        bool user = ulong.TryParse(
            form.TargetUserId.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong target);

        if (!user || desired is not (GrantToken or "REVOKE"))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.BankOperatorGrantInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldInstitution] = form.InstitutionCode.Trim(),
                [FieldUser] = target.ToString(CultureInfo.InvariantCulture),
                [FieldState] = desired,
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.FeeScheduleEditor, typeof(PanelFeeRuleForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitFeeRuleAsync(
        DiscordEndpointContext context,
        PanelFeeRuleForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        if (Pair(form.Amounts) is null ||
            Bounds(form.Bounds) is null ||
            !TryUnsigned(form.FreeOccurrences, out _) ||
            form.FeeType.Trim().Length == 0)
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.FeeRuleInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldInstitution] = form.InstitutionCode.Trim(),
                [FieldType] = form.FeeType.Trim().ToUpperInvariant(),
                [FieldAmounts] = form.Amounts.Trim(),
                [FieldBounds] = form.Bounds.Trim(),
                [FieldFree] = form.FreeOccurrences.Trim(),
            },
            cancellationToken);
    }

    private async Task<Result> ApplyBankAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        switch (payload.Action)
        {
            case ActionOperatorGrant:
                return await ApplyOperatorGrantAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);

            case ActionFeeSchedule:
                return await ApplyFeeScheduleAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);

            case ActionAccountReview:
                return await ApplyAccountReviewAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);

            case ActionBankDesign:
                return await ApplyBankDesignAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);

            default:
                return Result.Failure(
                    ErrorCategory.Validation, BankingErrorCodes.ManagementActionUnknown);
        }
    }

    private async Task<Result> ApplyOperatorGrantAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(
                Field(payload, FieldUser),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out ulong target))
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.BankOperatorGrantInvalid);
        }

        string institution = Field(payload, FieldInstitution);

        if (string.Equals(Field(payload, FieldState), GrantToken, StringComparison.Ordinal))
        {
            Result<BankOperatorGrantView> granted = await grants
                .GrantAsync(
                    new GrantBankOperatorCommand(actor, institution, target), cancellationToken)
                .ConfigureAwait(false);

            return granted.IsSuccess ? Result.Success() : Result.Failure(granted.Error!);
        }

        return await grants
            .RevokeAsync(new RevokeBankOperatorCommand(actor, institution, target), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result> ApplyFeeScheduleAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        if (Pair(Field(payload, FieldAmounts)) is not { } amounts ||
            Bounds(Field(payload, FieldBounds)) is not { } bounds ||
            !TryUnsigned(Field(payload, FieldFree), out long free))
        {
            return Result.Failure(ErrorCategory.Validation, BankingErrorCodes.FeeRuleInvalid);
        }

        Result<FeeScheduleDraftView> draft = await fees
            .StartDraftAsync(
                new StartFeeScheduleDraftCommand(actor, Field(payload, FieldInstitution)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!draft.IsSuccess)
        {
            return Result.Failure(draft.Error!);
        }

        Result<FeeRuleView> rule = await fees
            .UpsertRuleAsync(
                new UpsertFeeRuleCommand(
                    actor,
                    draft.Value.Id,
                    Field(payload, FieldType),
                    1,
                    amounts.First,
                    (int)amounts.Second,
                    bounds.Minimum,
                    bounds.Maximum,
                    (int)free),
                cancellationToken)
            .ConfigureAwait(false);

        if (!rule.IsSuccess)
        {
            return Result.Failure(rule.Error!);
        }

        Result<FeeScheduleVersionView> published = await fees
            .PublishAsync(
                new PublishFeeScheduleCommand(actor, draft.Value.Id), cancellationToken)
            .ConfigureAwait(false);

        return published.IsSuccess ? Result.Success() : Result.Failure(published.Error!);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.AccountReviewEditor, typeof(PanelAccountReviewForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitAccountReviewAsync(
        DiscordEndpointContext context,
        PanelAccountReviewForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        string decision = form.Decision.Trim().ToUpperInvariant();
        bool user = ulong.TryParse(
            form.ApplicantUserId.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out ulong applicant);

        if (!user || decision is not ("APPROVE" or "REJECT"))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.AccountOpeningDecisionInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldInstitution] = form.InstitutionCode.Trim(),
                [FieldUser] = applicant.ToString(CultureInfo.InvariantCulture),
                [FieldState] = decision,
                [FieldDescription] = form.ReasonCode.Trim(),
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.BankDesignEditor, typeof(PanelBankDesignForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitBankDesignAsync(
        DiscordEndpointContext context,
        PanelBankDesignForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        string[] colours = [form.Information, form.Success, form.Warning, form.Error];

        if (colours.Any(static value => Rgb(value) is null))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.PresentationProfileColourInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldInstitution] = form.InstitutionCode.Trim(),
                [FieldPalette] = string.Join(',', colours.Select(static value => value.Trim())),
            },
            cancellationToken);
    }

    private async Task<Result> ApplyAccountReviewAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<AccountOpeningReviewView> review =
            await AccountReview(actor, payload, cancellationToken).ConfigureAwait(false);

        if (!review.IsSuccess)
        {
            return Result.Failure(review.Error!);
        }

        if (review.Value.ApplicationId is not { } applicationId)
        {
            return Result.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AccountOpeningApplicationNotFound);
        }

        if (string.Equals(Field(payload, FieldState), "APPROVE", StringComparison.Ordinal))
        {
            Result<AccountOpeningApplicationView> approved = await banks
                .ApproveAccountOpeningAsync(
                    new ApproveAccountOpeningCommand(actor, applicationId), cancellationToken)
                .ConfigureAwait(false);

            return approved.IsSuccess ? Result.Success() : Result.Failure(approved.Error!);
        }

        string reason = Field(payload, FieldDescription);

        Result<AccountOpeningApplicationView> rejected = await banks
            .RejectAccountOpeningAsync(
                new RejectAccountOpeningCommand(
                    actor, applicationId, reason.Length == 0 ? "OPERATOR_DECISION" : reason),
                cancellationToken)
            .ConfigureAwait(false);

        return rejected.IsSuccess ? Result.Success() : Result.Failure(rejected.Error!);
    }

    private async Task<Result> ApplyBankDesignAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<AccountOpeningReviewView> bank =
            await AccountReview(actor, payload, cancellationToken).ConfigureAwait(false);

        if (!bank.IsSuccess)
        {
            return Result.Failure(bank.Error!);
        }

        int?[] colours =
        [
            .. Field(payload, FieldPalette).Split(',', StringSplitOptions.TrimEntries).Select(Rgb),
        ];

        if (colours.Length != 4 || colours.Any(static value => value is null))
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.PresentationProfileColourInvalid);
        }

        Result<PresentationProfileDraftView> draft = await presentation
            .StartDraftAsync(
                new StartPresentationProfileDraftCommand(
                    actor,
                    new PresentationProfilePalette(
                        colours[0], colours[1], colours[2], colours[3], null),
                    bank.Value.BankId),
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

    private async Task<string?> BankCurrentAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.Action is not (ActionAccountReview or ActionBankDesign))
        {
            return null;
        }

        Result<AccountOpeningReviewView> review =
            await AccountReview(actor, payload, cancellationToken).ConfigureAwait(false);

        if (!review.IsSuccess)
        {
            return null;
        }

        return review.Value.Status is { } status
            ? review.Value.ApplicantHandle
                + " " + catalog.Resolve(Rendering.ViewKeys.StatusOf(status.ToToken()))
            : review.Value.InstitutionCode;
    }

    private Task<Result<AccountOpeningReviewView>> AccountReview(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken) =>
        banks.GetAccountOpeningReviewAsync(
            new GetAccountOpeningReviewQuery(
                actor,
                Field(payload, FieldInstitution),
                ulong.TryParse(
                    Field(payload, FieldUser),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out ulong applicant)
                    ? applicant
                    : 0UL),
            cancellationToken);

    private static (long Minimum, long? Maximum)? Bounds(string text)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length is not (1 or 2) ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long minimum))
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return (minimum, null);
        }

        return long.TryParse(
            parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long maximum)
            ? (minimum, maximum)
            : null;
    }
}
