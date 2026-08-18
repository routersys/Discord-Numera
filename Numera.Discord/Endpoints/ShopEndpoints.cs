using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("shop", "加盟店の商品を閲覧します。")]
public sealed class ShopEndpoints : IEconomyEndpoint
{
    private readonly ICommerceApplicationService commerce;
    private readonly ITextCatalog catalog;

    public ShopEndpoints(ICommerceApplicationService commerce, ITextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(commerce);
        ArgumentNullException.ThrowIfNull(catalog);

        this.commerce = commerce;
        this.catalog = catalog;
    }

    [EconomySlashCommand("browse", "利用できる加盟店と商品を表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> BrowseAsync(
        DiscordEndpointContext context,
        [EconomyOption("store", "加盟店を指定すると商品を表示します。", false)] string? store,
        [EconomyOption("cursor", "次のページの位置を指定します。", false)] string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(store))
        {
            return await BrowseStoresAsync(context, cursor, cancellationToken).ConfigureAwait(false);
        }

        if (!EntityIdValue.TryParse(store, out EntityIdValue parsed))
        {
            return EndpointFailures.From(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantProfileNotFound);
        }

        Result<MerchantProductPageView> products = await commerce
            .ListMerchantProductsAsync(
                new ListMerchantProductsQuery(
                    context.GuildId, MerchantProfileId.FromValue(parsed), cursor),
                cancellationToken)
            .ConfigureAwait(false);

        if (!products.IsSuccess)
        {
            return EndpointFailures.From(products.Error!);
        }

        return products.Value.Items.Count == 0
            ? Empty(ViewKeys.ShopProductsEmpty)
            : Page(
                ViewKeys.ShopProducts,
                products.Value.Items.Count,
                products.Value.NextCursor,
                [
                    .. products.Value.Items.Select(static item =>
                        $"{item.Sku} {item.DisplayName} {item.UnitPrice.Value}"),
                ]);
    }

    [EconomySlashCommand("orders", "自分の注文履歴を表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> OrdersAsync(
        DiscordEndpointContext context,
        [EconomyOption("cursor", "次のページの位置を指定します。", false)] string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<CommerceOrderPageView> orders = await commerce
            .GetCommerceOrdersAsync(
                new GetCommerceOrdersQuery(EndpointAuthorization.ToActor(context), cursor),
                cancellationToken)
            .ConfigureAwait(false);

        if (!orders.IsSuccess)
        {
            return EndpointFailures.From(orders.Error!);
        }

        return orders.Value.Items.Count == 0
            ? Empty(ViewKeys.ShopOrdersEmpty)
            : Page(
                ViewKeys.ShopOrders,
                orders.Value.Items.Count,
                orders.Value.NextCursor,
                [
                    .. orders.Value.Items.Select(item =>
                        $"{item.OrderTotalPresentment.Value} {Status(item.Status.ToToken())}"),
                ]);
    }

    private async Task<DiscordEndpointResponse> BrowseStoresAsync(
        DiscordEndpointContext context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        Result<MerchantStorePageView> stores = await commerce
            .BrowseMerchantStoresAsync(
                new BrowseMerchantStoresQuery(context.GuildId, cursor), cancellationToken)
            .ConfigureAwait(false);

        if (!stores.IsSuccess)
        {
            return EndpointFailures.From(stores.Error!);
        }

        return stores.Value.Items.Count == 0
            ? Empty(ViewKeys.ShopStoresEmpty)
            : Page(
                ViewKeys.ShopStores,
                stores.Value.Items.Count,
                stores.Value.NextCursor,
                [
                    .. stores.Value.Items.Select(static item =>
                        $"{item.DisplayName} {item.Id.Value} {item.ActiveProductCount}"),
                ]);
    }

    private static DiscordEndpointResponse Empty(string viewKey) =>
        DiscordEndpointResponse.Message(viewKey, new Dictionary<string, string>(StringComparer.Ordinal));

    private static DiscordEndpointResponse Page(
        string viewKey,
        int count,
        string? nextCursor,
        IReadOnlyList<string> lines) =>
        DiscordEndpointResponse.Message(
            viewKey,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["count"] = count.ToString(CultureInfo.InvariantCulture),
                ["items"] = string.Join('\n', lines),
                ["cursor"] = nextCursor ?? string.Empty,
            });

    private string Status(string token) => catalog.Resolve(ViewKeys.StatusOf(token));
}
