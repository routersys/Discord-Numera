using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Domain.Banking;

namespace Numera.Discord.Endpoints;

public sealed partial class ManagePanelEndpoints
{
    internal const string ActionInsuranceFund = "insurance-fund";
    internal const string ActionResolution = "resolution-case";

    internal const string FieldStep = "step";
    internal const string FieldSuccessor = "successor";

    private const string StepSuccessor = "SUCCESSOR";
    private const string StepBridge = "BRIDGE";
    private const string StepTransfer = "TRANSFER";
    private const string StepLiquidate = "LIQUIDATE";

    [EconomyModal(Sessions.ManagementPanelCatalog.InsuranceFundEditor, typeof(PanelInsuranceFundForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitInsuranceFundAsync(
        DiscordEndpointContext context,
        PanelInsuranceFundForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        if (!string.Equals(form.Confirmation.Trim(), "CREATE", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.DepositInsuranceSchemeInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldState] = "CREATE",
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.ResolutionEditor, typeof(PanelResolutionForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitResolutionAsync(
        DiscordEndpointContext context,
        PanelResolutionForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        string step = form.Step.Trim().ToUpperInvariant();

        if (step is not (StepSuccessor or StepBridge or StepTransfer or StepLiquidate))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.ResolutionStepInvalid));
        }

        if (step == StepSuccessor && form.SuccessorCode.Trim().Length == 0)
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.ResolutionStepInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldInstitution] = form.InstitutionCode.Trim(),
                [FieldStep] = step,
                [FieldSuccessor] = form.SuccessorCode.Trim(),
            },
            cancellationToken);
    }

    private async Task<Result> ApplyResolutionAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        switch (payload.Action)
        {
            case ActionInsuranceFund:
                return await CreateInsuranceFundAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);

            case ActionResolution:
                return await AdvanceResolutionAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);

            default:
                return Result.Failure(
                    ErrorCategory.Validation, BankingErrorCodes.ManagementActionUnknown);
        }
    }

    private async Task<Result> CreateInsuranceFundAsync(
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

        Result<DepositInsuranceFundView> created = await insurance
            .CreateFundAsync(
                new CreateDepositInsuranceFundCommand(actor, status.Value.CurrencyId),
                cancellationToken)
            .ConfigureAwait(false);

        return created.IsSuccess ? Result.Success() : Result.Failure(created.Error!);
    }

    private async Task<Result> AdvanceResolutionAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<ResolutionCaseLookupView> lookup = await ResolutionCase(
            actor, payload, cancellationToken).ConfigureAwait(false);

        if (!lookup.IsSuccess)
        {
            return Result.Failure(lookup.Error!);
        }

        if (lookup.Value.Id is not { } caseId)
        {
            return Result.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.ResolutionCaseNotFound);
        }

        Result<ResolutionCaseView> outcome = Field(payload, FieldStep) switch
        {
            StepSuccessor => lookup.Value.SuccessorBankId is { } successor
                ? await resolutions
                    .SelectSuccessorBankAsync(
                        new SelectResolutionSuccessorBankCommand(actor, caseId, successor),
                        cancellationToken)
                    .ConfigureAwait(false)
                : Result<ResolutionCaseView>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.BankNotFound),

            StepBridge => await resolutions
                .CreateBridgeBankAsync(
                    new CreateResolutionBridgeBankCommand(actor, caseId), cancellationToken)
                .ConfigureAwait(false),

            StepTransfer => await resolutions
                .StartTransferAsync(
                    new StartResolutionTransferCommand(actor, caseId), cancellationToken)
                .ConfigureAwait(false),

            _ => await resolutions
                .StartLiquidationAsync(
                    new StartResolutionLiquidationCommand(actor, caseId), cancellationToken)
                .ConfigureAwait(false),
        };

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    private async Task<string?> ResolutionCurrentAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.Action == ActionInsuranceFund)
        {
            Result<DepositInsuranceSchemeStatusView> status = await insurance
                .GetSchemeStatusAsync(
                    new GetDepositInsuranceSchemeQuery(actor, Field(payload, FieldClass)),
                    cancellationToken)
                .ConfigureAwait(false);

            return status is { IsSuccess: true, Value.HasFund: true }
                ? catalog.Resolve(Rendering.ViewKeys.PanelFundExists)
                : null;
        }

        if (payload.Action != ActionResolution)
        {
            return null;
        }

        Result<ResolutionCaseLookupView> lookup = await ResolutionCase(
            actor, payload, cancellationToken).ConfigureAwait(false);

        if (!lookup.IsSuccess)
        {
            return null;
        }

        return lookup.Value.Status is { } status2
            ? lookup.Value.InstitutionCode
                + " " + catalog.Resolve(Rendering.ViewKeys.StatusOf(status2.ToToken()))
            : null;
    }

    private Task<Result<ResolutionCaseLookupView>> ResolutionCase(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken) =>
        resolutions.FindCaseAsync(
            new FindResolutionCaseQuery(
                actor, Field(payload, FieldInstitution), Field(payload, FieldSuccessor)),
            cancellationToken);
}
