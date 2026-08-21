using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Domain.Banking;

namespace Numera.Discord.Endpoints;

public sealed partial class ManagePanelEndpoints
{
    internal const string ActionTrustPolicy = "trust-policy";
    internal const string ActionNetworkPolicy = "network-policy";
    internal const string ActionNetworkState = "network-state";
    internal const string ActionPrudentialPolicy = "prudential-policy";

    internal const string FieldNetwork = "network";
    internal const string FieldState = "state";
    internal const string FieldEstablished = "established";
    internal const string FieldTrusted = "trusted";
    internal const string FieldReserve = "reserve";
    internal const string FieldMode = "mode";
    internal const string FieldPosting = "posting";
    internal const string FieldInterval = "interval";
    internal const string FieldExposure = "exposure";
    internal const string FieldCet1 = "cet1";
    internal const string FieldLeverage = "leverage";
    internal const string FieldLiquidity = "liquidity";
    internal const string FieldCapital = "capital";

    private const string StateSuspended = "SUSPENDED";

    [EconomyModal(Sessions.ManagementPanelCatalog.TrustPolicyEditor, typeof(PanelTrustPolicyForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitTrustPolicyAsync(
        DiscordEndpointContext context,
        PanelTrustPolicyForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        if (Thresholds(form.Established) is null ||
            Thresholds(form.Trusted) is null ||
            Thresholds(form.Reserve) is null)
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyTrustThresholdInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldEstablished] = form.Established.Trim(),
                [FieldTrusted] = form.Trusted.Trim(),
                [FieldReserve] = form.Reserve.Trim(),
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.NetworkPolicyEditor, typeof(PanelNetworkPolicyForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitNetworkPolicyAsync(
        DiscordEndpointContext context,
        PanelNetworkPolicyForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        bool parsedMode = PaymentOrderCatalog.TryParseSettlementModeToken(
            form.SettlementMode.Trim(), out SettlementMode mode);
        bool parsedPosting = PaymentOrderCatalog.TryParsePostingPolicyToken(
            form.PostingPolicy.Trim(), out BeneficiaryPostingPolicy posting);
        bool parsedExposure = TryAmount(form.ExposureLimit, out long exposure);

        if (!parsedMode || !parsedPosting || !parsedExposure)
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.PaymentNetworkPolicyInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldNetwork] = form.NetworkCode.Trim(),
                [FieldMode] = mode.ToToken(),
                [FieldPosting] = posting.ToToken(),
                [FieldInterval] = form.ClearingInterval.Trim(),
                [FieldExposure] = exposure.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.NetworkStateEditor, typeof(PanelNetworkStateForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitNetworkStateAsync(
        DiscordEndpointContext context,
        PanelNetworkStateForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        string desired = form.DesiredState.Trim().ToUpperInvariant();

        if (desired is not (StateSuspended or "ACTIVE"))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.PaymentNetworkPolicyInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldNetwork] = form.NetworkCode.Trim(),
                [FieldState] = desired,
            },
            cancellationToken);
    }

    [EconomyModal(
        Sessions.ManagementPanelCatalog.PrudentialPolicyEditor, typeof(PanelPrudentialPolicyForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitPrudentialPolicyAsync(
        DiscordEndpointContext context,
        PanelPrudentialPolicyForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        if (Pair(form.Cet1) is null ||
            Pair(form.Leverage) is null ||
            !TryAmount(form.Liquidity, out _) ||
            !TryAmount(form.MinimumCapital, out _))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.PrudentialPolicyInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldCet1] = form.Cet1.Trim(),
                [FieldLeverage] = form.Leverage.Trim(),
                [FieldLiquidity] = form.Liquidity.Trim(),
                [FieldCapital] = form.MinimumCapital.Trim(),
            },
            cancellationToken);
    }

    private async Task<Result> PublishTrustPolicyAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        if (Thresholds(Field(payload, FieldEstablished)) is not { } established ||
            Thresholds(Field(payload, FieldTrusted)) is not { } trusted ||
            Thresholds(Field(payload, FieldReserve)) is not { } reserve)
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyTrustThresholdInvalid);
        }

        Result<CurrencyTrustPolicyDraftView> draft = await trusts
            .StartPolicyDraftAsync(
                new StartCurrencyTrustPolicyDraftCommand(
                    actor, new CurrencyTrustPolicyInput(established, trusted, reserve)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!draft.IsSuccess)
        {
            return Result.Failure(draft.Error!);
        }

        Result<CurrencyTrustPolicyVersionView> published = await trusts
            .PublishPolicyAsync(
                new PublishCurrencyTrustPolicyCommand(actor, draft.Value.Id), cancellationToken)
            .ConfigureAwait(false);

        return published.IsSuccess ? Result.Success() : Result.Failure(published.Error!);
    }

    private async Task<Result> PublishNetworkPolicyAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<PaymentNetworkStatusView> network = await networks
            .GetNetworkStatusAsync(
                new GetPaymentNetworkQuery(actor, Field(payload, FieldNetwork)), cancellationToken)
            .ConfigureAwait(false);

        if (!network.IsSuccess)
        {
            return Result.Failure(network.Error!);
        }

        bool parsedMode = PaymentOrderCatalog.TryParseSettlementModeToken(
            Field(payload, FieldMode), out SettlementMode mode);
        bool parsedPosting = PaymentOrderCatalog.TryParsePostingPolicyToken(
            Field(payload, FieldPosting), out BeneficiaryPostingPolicy posting);
        bool parsedExposure = TryAmount(Field(payload, FieldExposure), out long exposure);

        if (!parsedMode || !parsedPosting || !parsedExposure)
        {
            return Result.Failure(
                ErrorCategory.Validation, BankingErrorCodes.PaymentNetworkPolicyInvalid);
        }

        int? interval = int.TryParse(
            Field(payload, FieldInterval), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;

        Result<PaymentNetworkPolicyVersionView> published = await networks
            .PublishPolicyAsync(
                new PublishPaymentNetworkPolicyCommand(
                    actor,
                    network.Value.Id,
                    new PaymentNetworkPolicyInput(mode, posting, null, interval, 10_000, exposure)),
                cancellationToken)
            .ConfigureAwait(false);

        return published.IsSuccess ? Result.Success() : Result.Failure(published.Error!);
    }

    private async Task<Result> ChangeNetworkStateAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<PaymentNetworkStatusView> network = await networks
            .GetNetworkStatusAsync(
                new GetPaymentNetworkQuery(actor, Field(payload, FieldNetwork)), cancellationToken)
            .ConfigureAwait(false);

        if (!network.IsSuccess)
        {
            return Result.Failure(network.Error!);
        }

        return string.Equals(Field(payload, FieldState), StateSuspended, StringComparison.Ordinal)
            ? await networks
                .SuspendNetworkAsync(
                    new SuspendPaymentNetworkCommand(actor, network.Value.Id), cancellationToken)
                .ConfigureAwait(false)
            : await networks
                .ResumeNetworkAsync(
                    new ResumePaymentNetworkCommand(actor, network.Value.Id), cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task<Result> PublishPrudentialPolicyAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        if (Pair(Field(payload, FieldCet1)) is not { } cet1 ||
            Pair(Field(payload, FieldLeverage)) is not { } leverage ||
            !TryAmount(Field(payload, FieldLiquidity), out long liquidity) ||
            !TryAmount(Field(payload, FieldCapital), out long capital))
        {
            return Result.Failure(ErrorCategory.Validation, BankingErrorCodes.PrudentialPolicyInvalid);
        }

        Result<PrudentialPolicyDraftView> draft = await prudential
            .StartDraftAsync(
                new StartPrudentialPolicyDraftCommand(
                    actor,
                    new PrudentialPolicyInput(
                        (int)cet1.First, (int)cet1.Second,
                        (int)leverage.First, (int)leverage.Second,
                        (int)liquidity, capital)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!draft.IsSuccess)
        {
            return Result.Failure(draft.Error!);
        }

        Result<PrudentialPolicyVersionView> published = await prudential
            .PublishAsync(
                new PublishPrudentialPolicyCommand(actor, draft.Value.Id), cancellationToken)
            .ConfigureAwait(false);

        return published.IsSuccess ? Result.Success() : Result.Failure(published.Error!);
    }

    private async Task<string?> GovernanceCurrentAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        switch (payload.Action)
        {
            case ActionTrustPolicy:
            {
                Result<CurrencyTrustPolicyStatusView> status = await trusts
                    .GetPolicyStatusAsync(new GetCurrencyTrustPolicyQuery(actor), cancellationToken)
                    .ConfigureAwait(false);

                return status is { IsSuccess: true, Value.Policy: { } policy }
                    ? Show(policy.Established) + " / " + Show(policy.Trusted) + " / " + Show(policy.ReserveEligible)
                    : null;
            }

            case ActionNetworkPolicy:
            case ActionNetworkState:
            {
                Result<PaymentNetworkStatusView> status = await networks
                    .GetNetworkStatusAsync(
                        new GetPaymentNetworkQuery(actor, Field(payload, FieldNetwork)),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!status.IsSuccess)
                {
                    return null;
                }

                string state = catalog.Resolve(
                    Rendering.ViewKeys.StatusOf(status.Value.Status.ToToken()));

                return status.Value.Policy is { } current
                    ? state + " " + current.SettlementMode.ToToken() + " " + current.BeneficiaryPostingPolicy.ToToken()
                    : state;
            }

            case ActionPrudentialPolicy:
            {
                Result<PrudentialPolicyStatusView> status = await prudential
                    .GetPolicyStatusAsync(new GetPrudentialPolicyQuery(actor), cancellationToken)
                    .ConfigureAwait(false);

                return status is { IsSuccess: true, Value.Policy: { } policy }
                    ? Join(
                        policy.MinimumCet1Bps, policy.LendingCet1Bps,
                        policy.MinimumLeverageBps, policy.ConfiguredWarningLeverageBps,
                        policy.MinimumLiquidityBps, policy.MinimumInitialBankCapitalMinor)
                    : null;
            }

            default:
                return null;
        }
    }

    private static string Show(CurrencyTrustTierThresholds thresholds) =>
        thresholds.MinimumAgeSeconds.ToString(CultureInfo.InvariantCulture)
        + "," + thresholds.MinimumTradeDays.ToString(CultureInfo.InvariantCulture)
        + "," + thresholds.MinimumCounterparties.ToString(CultureInfo.InvariantCulture);

    private static string Join(params long[] values) =>
        string.Join(',', values.Select(static value => value.ToString(CultureInfo.InvariantCulture)));

    private static CurrencyTrustTierThresholds? Thresholds(string text)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);

        return parts.Length == 3
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long age)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int days)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int peers)
            ? new CurrencyTrustTierThresholds(age, days, peers)
            : null;
    }

    private static (long First, long Second)? Pair(string text)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);

        return parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long first)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long second)
            ? (first, second)
            : null;
    }

    private static bool TryAmount(string text, out long value) =>
        long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && value > 0;
}
