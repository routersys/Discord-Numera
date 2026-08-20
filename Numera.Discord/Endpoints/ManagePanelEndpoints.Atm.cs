using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Domain.Banking;

namespace Numera.Discord.Endpoints;

public sealed partial class ManagePanelEndpoints
{
    internal const string ActionAtmNetwork = "atm-network";
    internal const string ActionAtmTerminal = "atm-terminal";
    internal const string ActionAtmService = "atm-service";
    internal const string ActionAtmCassette = "atm-cassette";
    internal const string ActionDenomination = "cash-denomination";
    internal const string ActionCashConversion = "cash-conversion";

    internal const string FieldTerminal = "terminal";
    internal const string FieldInstitution = "institution";
    internal const string FieldFlags = "flags";
    internal const string FieldValue = "value";
    internal const string FieldRole = "role";
    internal const string FieldSlot = "slot";
    internal const string FieldKind = "kind";
    internal const string FieldQuantity = "quantity";
    internal const string FieldDirection = "direction";

    private const string DirectionToCash = "TO_CASH";

    [EconomyModal(Sessions.ManagementPanelCatalog.AtmNetworkEditor, typeof(PanelAtmNetworkForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitAtmNetworkAsync(
        DiscordEndpointContext context,
        PanelAtmNetworkForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        string desired = form.DesiredState.Trim().ToUpperInvariant();

        if (desired.Length > 0 && AtmState(desired) is null)
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.AtmNetworkInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldNetwork] = form.Name.Trim(),
                [FieldState] = desired,
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.AtmTerminalEditor, typeof(PanelAtmTerminalForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitAtmTerminalAsync(
        DiscordEndpointContext context,
        PanelAtmTerminalForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldTerminal] = form.TerminalName.Trim(),
                [FieldInstitution] = form.InstitutionCode.Trim(),
                [FieldNetwork] = form.NetworkName.Trim(),
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.AtmServiceEditor, typeof(PanelAtmServiceForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitAtmServiceAsync(
        DiscordEndpointContext context,
        PanelAtmServiceForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        string desired = form.DesiredState.Trim().ToUpperInvariant();

        if (Flags(form.Flags, 3) is null || desired is not ("ACTIVE" or "SUSPENDED"))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.AtmTerminalInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldTerminal] = form.TerminalName.Trim(),
                [FieldFlags] = form.Flags.Trim(),
                [FieldState] = desired,
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.AtmCassetteEditor, typeof(PanelAtmCassetteForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitAtmCassetteAsync(
        DiscordEndpointContext context,
        PanelAtmCassetteForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        bool value = TryAmount(form.DenominationValue, out long denomination);
        bool slot = Pair(form.Slot) is not null;

        if (!value || !slot || form.CassetteRole.Trim().Length == 0)
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.AtmCassetteInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldTerminal] = form.TerminalName.Trim(),
                [FieldValue] = denomination.ToString(CultureInfo.InvariantCulture),
                [FieldRole] = form.CassetteRole.Trim().ToUpperInvariant(),
                [FieldSlot] = form.Slot.Trim(),
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.DenominationEditor, typeof(PanelDenominationForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitDenominationAsync(
        DiscordEndpointContext context,
        PanelDenominationForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        bool value = TryAmount(form.ValueMinor, out long denomination);
        string kind = form.Kind.Trim().ToUpperInvariant();

        if (!value || kind is not ("NOTE" or "COIN") || Flags(form.Flags, 2) is null)
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyDenominationInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldValue] = denomination.ToString(CultureInfo.InvariantCulture),
                [FieldKind] = kind,
                [FieldFlags] = form.Flags.Trim(),
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.CashConversionEditor, typeof(PanelCashConversionForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitCashConversionAsync(
        DiscordEndpointContext context,
        PanelCashConversionForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        bool value = TryAmount(form.DenominationValue, out long denomination);
        bool quantity = TryAmount(form.Quantity, out long count);
        string direction = form.Direction.Trim().ToUpperInvariant();

        if (!value || !quantity || direction is not (DirectionToCash or "TO_RESERVE"))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.CashConversionInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldInstitution] = form.InstitutionCode.Trim(),
                [FieldValue] = denomination.ToString(CultureInfo.InvariantCulture),
                [FieldQuantity] = count.ToString(CultureInfo.InvariantCulture),
                [FieldDirection] = direction,
            },
            cancellationToken);
    }

    private async Task<Result> ApplyAtmAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        switch (payload.Action)
        {
            case ActionAtmNetwork:
                return await ApplyAtmNetworkAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);

            case ActionAtmTerminal:
                return await ApplyAtmTerminalAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);

            case ActionAtmService:
                return await ApplyAtmServiceAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);

            case ActionAtmCassette:
                return await ApplyAtmCassetteAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);

            case ActionDenomination:
                return await ApplyDenominationAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);

            case ActionCashConversion:
                return await ApplyCashConversionAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);

            default:
                return await ApplyMerchantAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);
        }
    }

    private async Task<Result> ApplyAtmNetworkAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<AtmDeploymentView> deployment = await Deployment(actor, payload, cancellationToken)
            .ConfigureAwait(false);

        if (!deployment.IsSuccess)
        {
            return Result.Failure(deployment.Error!);
        }

        string name = Field(payload, FieldNetwork);

        if (deployment.Value.NetworkId is not { } networkId)
        {
            Result<AtmNetworkView> created = await atms
                .CreateNetworkAsync(new CreateAtmNetworkCommand(actor, name), cancellationToken)
                .ConfigureAwait(false);

            return created.IsSuccess ? Result.Success() : Result.Failure(created.Error!);
        }

        if (AtmState(Field(payload, FieldState)) is not { } target)
        {
            return Result.Failure(ErrorCategory.Validation, BankingErrorCodes.AtmNetworkInvalid);
        }

        Result<AtmNetworkView> updated = await atms
            .UpdateNetworkAsync(
                new UpdateAtmNetworkCommand(actor, networkId, name, target), cancellationToken)
            .ConfigureAwait(false);

        return updated.IsSuccess ? Result.Success() : Result.Failure(updated.Error!);
    }

    private async Task<Result> ApplyAtmTerminalAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<AtmDeploymentView> deployment = await Deployment(actor, payload, cancellationToken)
            .ConfigureAwait(false);

        if (!deployment.IsSuccess)
        {
            return Result.Failure(deployment.Error!);
        }

        if (deployment.Value.BankId is not { } bankId)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        Result<AtmTerminalView> created = await atms
            .CreateTerminalAsync(
                new CreateAtmTerminalCommand(
                    actor,
                    bankId,
                    actor.GuildId.ToString(CultureInfo.InvariantCulture),
                    deployment.Value.NetworkId,
                    Field(payload, FieldTerminal)),
                cancellationToken)
            .ConfigureAwait(false);

        return created.IsSuccess ? Result.Success() : Result.Failure(created.Error!);
    }

    private async Task<Result> ApplyAtmServiceAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<AtmDeploymentView> deployment = await Deployment(actor, payload, cancellationToken)
            .ConfigureAwait(false);

        if (!deployment.IsSuccess)
        {
            return Result.Failure(deployment.Error!);
        }

        if (deployment.Value.TerminalId is not { } terminalId)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.AtmTerminalNotFound);
        }

        if (Flags(Field(payload, FieldFlags), 3) is not { } flags)
        {
            return Result.Failure(ErrorCategory.Validation, BankingErrorCodes.AtmTerminalInvalid);
        }

        AtmTerminalCurrencyServiceStatus status =
            string.Equals(Field(payload, FieldState), "SUSPENDED", StringComparison.Ordinal)
                ? AtmTerminalCurrencyServiceStatus.Suspended
                : AtmTerminalCurrencyServiceStatus.Active;

        Result<AtmTerminalCurrencyServiceView> configured = await atms
            .ConfigureCurrencyServiceAsync(
                new ConfigureAtmTerminalCurrencyServiceCommand(
                    actor, terminalId, deployment.Value.CurrencyId,
                    flags[0], flags[1], flags[2], status),
                cancellationToken)
            .ConfigureAwait(false);

        return configured.IsSuccess ? Result.Success() : Result.Failure(configured.Error!);
    }

    private async Task<Result> ApplyAtmCassetteAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<AtmDeploymentView> deployment = await Deployment(actor, payload, cancellationToken)
            .ConfigureAwait(false);

        if (!deployment.IsSuccess)
        {
            return Result.Failure(deployment.Error!);
        }

        if (deployment.Value.TerminalId is not { } terminalId ||
            deployment.Value.DenominationId is not { } denominationId)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.AtmCashCassetteNotFound);
        }

        if (Pair(Field(payload, FieldSlot)) is not { } slot)
        {
            return Result.Failure(ErrorCategory.Validation, BankingErrorCodes.AtmCassetteInvalid);
        }

        Result<AtmCashCassetteView> configured = await atms
            .ConfigureCassetteAsync(
                new ConfigureAtmCashCassetteCommand(
                    actor,
                    terminalId,
                    denominationId,
                    Field(payload, FieldRole),
                    (int)slot.First,
                    slot.Second),
                cancellationToken)
            .ConfigureAwait(false);

        return configured.IsSuccess ? Result.Success() : Result.Failure(configured.Error!);
    }

    private async Task<Result> ApplyDenominationAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<AtmDeploymentView> deployment = await Deployment(actor, payload, cancellationToken)
            .ConfigureAwait(false);

        if (!deployment.IsSuccess)
        {
            return Result.Failure(deployment.Error!);
        }

        if (!TryAmount(Field(payload, FieldValue), out long value) ||
            Flags(Field(payload, FieldFlags), 2) is not { } flags)
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyDenominationInvalid);
        }

        Result<CurrencyDenominationView> created = await cash
            .CreateDenominationAsync(
                new CreateCurrencyDenominationCommand(
                    actor,
                    deployment.Value.CurrencyId,
                    value,
                    Field(payload, FieldKind),
                    flags[0],
                    flags[1]),
                cancellationToken)
            .ConfigureAwait(false);

        return created.IsSuccess ? Result.Success() : Result.Failure(created.Error!);
    }

    private async Task<Result> ApplyCashConversionAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<AtmDeploymentView> deployment = await Deployment(actor, payload, cancellationToken)
            .ConfigureAwait(false);

        if (!deployment.IsSuccess)
        {
            return Result.Failure(deployment.Error!);
        }

        if (deployment.Value.BankId is not { } bankId)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (deployment.Value.DenominationId is not { } denominationId ||
            !TryAmount(Field(payload, FieldQuantity), out long quantity))
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CashConversionInvalid);
        }

        string token = "panel-cash-" + payload.Action + "-"
            + Field(payload, FieldInstitution) + "-" + Field(payload, FieldValue)
            + "-" + Field(payload, FieldQuantity);

        Result<CashConversionView> converted =
            string.Equals(Field(payload, FieldDirection), DirectionToCash, StringComparison.Ordinal)
                ? await cash
                    .ConvertReserveToCashAsync(
                        new ConvertReserveToCashCommand(
                            actor, bankId, denominationId, quantity, token),
                        cancellationToken)
                    .ConfigureAwait(false)
                : await cash
                    .ConvertCashToReserveAsync(
                        new ConvertCashToReserveCommand(
                            actor, bankId, denominationId, quantity, token),
                        cancellationToken)
                    .ConfigureAwait(false);

        return converted.IsSuccess ? Result.Success() : Result.Failure(converted.Error!);
    }

    private async Task<string?> AtmCurrentAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.Action is not (ActionAtmNetwork or ActionAtmTerminal or ActionAtmService
            or ActionAtmCassette or ActionDenomination or ActionCashConversion))
        {
            return await MerchantCurrentAsync(actor, payload, cancellationToken)
                .ConfigureAwait(false);
        }

        Result<AtmDeploymentView> deployment = await Deployment(actor, payload, cancellationToken)
            .ConfigureAwait(false);

        if (!deployment.IsSuccess)
        {
            return null;
        }

        List<string> parts = [];

        if (deployment.Value.NetworkStatus is { } network)
        {
            parts.Add(catalog.Resolve(Rendering.ViewKeys.StatusOf(network.ToToken())));
        }

        if (deployment.Value.TerminalStatus is { } terminal)
        {
            parts.Add(catalog.Resolve(Rendering.ViewKeys.StatusOf(terminal.ToToken())));
        }

        if (deployment.Value.DenominationId is not null)
        {
            parts.Add(Field(payload, FieldValue));
        }

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private Task<Result<AtmDeploymentView>> Deployment(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken) =>
        atms.GetDeploymentAsync(
            new GetAtmDeploymentQuery(
                actor,
                Field(payload, FieldNetwork),
                Field(payload, FieldTerminal),
                Field(payload, FieldInstitution),
                long.TryParse(
                    Field(payload, FieldValue),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long value)
                    ? value
                    : 0L),
            cancellationToken);

    private static AtmNetworkStatus? AtmState(string token) => token switch
    {
        "ACTIVE" => AtmNetworkStatus.Active,
        "SUSPENDED" => AtmNetworkStatus.Suspended,
        "RETIRED" => AtmNetworkStatus.Retired,
        _ => null,
    };

    private static bool[]? Flags(string text, int expected)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != expected || parts.Any(static value => value is not ("0" or "1")))
        {
            return null;
        }

        return [.. parts.Select(static value => value == "1")];
    }
}
