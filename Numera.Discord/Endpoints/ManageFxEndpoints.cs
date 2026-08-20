using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("manage", "経済圏を管理します。")]
public sealed class ManageFxEndpoints : IEconomyEndpoint
{
    private const string ActionCreate = "create";
    private const string ActionApprove = "approve";
    private const string ActionOverride = "override";
    private const string ActionPolicy = "policy";
    private const string ActionSuspend = "suspend";
    private const string ActionResume = "resume";
    private const string ActionRetire = "retire";

    private readonly IFxAdministrationApplicationService markets;
    private readonly ITextCatalog catalog;

    public ManageFxEndpoints(IFxAdministrationApplicationService markets, ITextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(markets);
        ArgumentNullException.ThrowIfNull(catalog);

        this.markets = markets;
        this.catalog = catalog;
    }

    [EconomySlashCommand("fx-market", "為替市場を設置し方針を公開します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    public async Task<DiscordEndpointResponse> FxMarketAsync(
        DiscordEndpointContext context,
        [EconomyOption("action", "実行する操作を選びます。", true)]
        [EconomyChoice("市場を設置", ActionCreate)]
        [EconomyChoice("承認を申請", ActionApprove)]
        [EconomyChoice("承認を代行", ActionOverride)]
        [EconomyChoice("手数料方針を公開", ActionPolicy)]
        [EconomyChoice("取引を停止", ActionSuspend)]
        [EconomyChoice("取引を再開", ActionResume)]
        [EconomyChoice("市場を廃止", ActionRetire)]
        string action,
        [EconomyOption("market", "対象の市場を指定します。", false)]
        [EconomyAutocomplete(SuggestionEndpoints.FxMarketProviderKey)]
        string? market,
        [EconomyOption("base", "基軸通貨を指定します。", false)]
        [EconomyAutocomplete(SuggestionEndpoints.CurrencyProviderKey)]
        string? baseCurrency,
        [EconomyOption("quote", "決済通貨を指定します。", false)]
        [EconomyAutocomplete(SuggestionEndpoints.CurrencyProviderKey)]
        string? quoteCurrency,
        [EconomyOption("operator", "運営主体の銀行を選びます。", false)]
        [EconomyAutocomplete(SuggestionEndpoints.BankProviderKey)]
        string? marketOperator,
        [EconomyOption("price-scale", "価格の桁数を入力します。", false)] string? priceScale,
        [EconomyOption("tick-size", "呼値の刻みを入力します。", false)] string? tickSize,
        [EconomyOption("lot-size", "売買単位を入力します。", false)] string? lotSize,
        [EconomyOption("maker-fee-bps", "メイカー手数料をbpsで入力します。", false)] string? makerFeeBps,
        [EconomyOption("taker-fee-bps", "テイカー手数料をbpsで入力します。", false)] string? takerFeeBps,
        [EconomyOption("max-slippage-bps", "許容スリッページをbpsで入力します。", false)]
        string? maximumSlippageBps,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        AuthorizationContext actor = EndpointAuthorization.ToActor(context);

        return action switch
        {
            ActionCreate => await CreateAsync(
                actor,
                baseCurrency,
                quoteCurrency,
                marketOperator,
                priceScale,
                tickSize,
                lotSize,
                cancellationToken).ConfigureAwait(false),
            ActionPolicy => await PublishPolicyAsync(
                actor,
                market,
                makerFeeBps,
                takerFeeBps,
                maximumSlippageBps,
                cancellationToken).ConfigureAwait(false),
            ActionApprove => await ApproveAsync(actor, market, cancellationToken).ConfigureAwait(false),
            ActionOverride => await OverrideAsync(actor, market, cancellationToken).ConfigureAwait(false),
            ActionSuspend => await StateAsync(
                actor, market, FxMarketStatus.Suspended, cancellationToken).ConfigureAwait(false),
            ActionResume => await StateAsync(
                actor, market, FxMarketStatus.Active, cancellationToken).ConfigureAwait(false),
            ActionRetire => await StateAsync(
                actor, market, FxMarketStatus.Retired, cancellationToken).ConfigureAwait(false),
            _ => EndpointFailures.From(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound),
        };
    }

    private async Task<DiscordEndpointResponse> CreateAsync(
        AuthorizationContext actor,
        string? baseCurrency,
        string? quoteCurrency,
        string? marketOperator,
        string? priceScale,
        string? tickSize,
        string? lotSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseCurrency) ||
            string.IsNullOrWhiteSpace(quoteCurrency) ||
            string.IsNullOrWhiteSpace(marketOperator) ||
            !TryNumber(priceScale, out long scale) ||
            !TryNumber(tickSize, out long tick) ||
            !TryNumber(lotSize, out long lot))
        {
            return EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.FxMarketParametersInvalid);
        }

        Result<FxMarketView> result = await markets
            .CreateMarketAsync(
                new CreateFxMarketCommand(
                    actor,
                    baseCurrency,
                    quoteCurrency,
                    marketOperator,
                    scale,
                    tick,
                    lot),
                cancellationToken)
            .ConfigureAwait(false);

        return Market(result, ViewKeys.ManageFxMarketCreated);
    }

    private async Task<DiscordEndpointResponse> PublishPolicyAsync(
        AuthorizationContext actor,
        string? market,
        string? makerFeeBps,
        string? takerFeeBps,
        string? maximumSlippageBps,
        CancellationToken cancellationToken)
    {
        if (!TryMarket(market, out FxMarketId id) ||
            !TryBasisPoints(makerFeeBps, out int maker) ||
            !TryBasisPoints(takerFeeBps, out int taker) ||
            !TryBasisPoints(maximumSlippageBps, out int slippage))
        {
            return EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.FxMarketParametersInvalid);
        }

        Result<FxMarketPolicyView> result = await markets
            .PublishPolicyAsync(
                new PublishFxMarketPolicyCommand(actor, id, maker, taker, slippage),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.ManageFxMarketPolicyPublished,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["makerFeeBps"] = Number(result.Value.MakerFeeBps),
                    ["takerFeeBps"] = Number(result.Value.TakerFeeBps),
                    ["maximumSlippageBps"] = Number(result.Value.MaximumMarketSlippageBps),
                    ["version"] = Number(result.Value.Version),
                })
            : EndpointFailures.From(result.Error!);
    }

    private async Task<DiscordEndpointResponse> ApproveAsync(
        AuthorizationContext actor,
        string? market,
        CancellationToken cancellationToken)
    {
        if (!TryMarket(market, out FxMarketId id))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        Result<FxMarketView> result = await markets
            .SubmitApprovalAsync(new SubmitFxMarketApprovalCommand(actor, id), cancellationToken)
            .ConfigureAwait(false);

        return Market(result, ViewKeys.ManageFxMarketState);
    }

    private async Task<DiscordEndpointResponse> OverrideAsync(
        AuthorizationContext actor,
        string? market,
        CancellationToken cancellationToken)
    {
        if (!TryMarket(market, out FxMarketId id))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        Result<FxMarketView> result = await markets
            .OverrideActivationAsync(new OverrideFxMarketActivationCommand(actor, id), cancellationToken)
            .ConfigureAwait(false);

        return Market(result, ViewKeys.ManageFxMarketState);
    }

    private async Task<DiscordEndpointResponse> StateAsync(
        AuthorizationContext actor,
        string? market,
        FxMarketStatus desired,
        CancellationToken cancellationToken)
    {
        if (!TryMarket(market, out FxMarketId id))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        Result<FxMarketView> result = await markets
            .SetMarketStateAsync(new SetFxMarketStateCommand(actor, id, desired), cancellationToken)
            .ConfigureAwait(false);

        return Market(result, ViewKeys.ManageFxMarketState);
    }

    private DiscordEndpointResponse Market(Result<FxMarketView> result, string viewKey) =>
        result.IsSuccess
            ? DiscordEndpointResponse.Message(
                viewKey,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["market"] = result.Value.Id.Value.ToString(),
                    ["status"] = catalog.Resolve(ViewKeys.StatusOf(result.Value.Status.ToToken())),
                    ["priceScale"] = Number(result.Value.PriceScale),
                    ["tickSize"] = Number(result.Value.TickSizePriceUnits),
                    ["lotSize"] = Number(result.Value.LotSizeBaseMinor),
                })
            : EndpointFailures.From(result.Error!);

    private static bool TryMarket(string? text, out FxMarketId id)
    {
        if (text is not null && FxMarketReference.TryParse(text, out id))
        {
            return true;
        }

        id = default;
        return false;
    }

    private static bool TryNumber(string? text, out long value) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool TryBasisPoints(string? text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
