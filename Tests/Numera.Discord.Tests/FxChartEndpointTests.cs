using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Endpoints;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Common;
using SkiaSharp;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class FxChartEndpointTests
{
    private const string MarketReference = "01a01db8-a2eb-741e-90f6-c157068d2cf0";

    private const long PriceScale = 100L;

    private sealed class StubFontProvider : ICardFontProvider
    {
        public SKTypeface Resolve(CardFontRole role) => SKTypeface.Default;

        public bool TryResolveFallback(out SKTypeface typeface)
        {
            typeface = SKTypeface.Default;
            return true;
        }
    }

    private sealed class SilentDiagnostics : ICardRenderDiagnostics
    {
        public void MissingGlyph(int codePoint)
        {
        }

        public void RendererUnavailable(string reason)
        {
        }
    }

    private sealed class StubMarkets : IFxApplicationService
    {
        private readonly IReadOnlyList<FxOhlcBucket> buckets;

        internal StubMarkets(IReadOnlyList<FxOhlcBucket> buckets) => this.buckets = buckets;

        internal GetFxChartVisualQuery? LastChartQuery { get; private set; }

        public Task<Result<FxChartVisualView>> GetFxChartVisualAsync(
            GetFxChartVisualQuery query,
            CancellationToken cancellationToken)
        {
            LastChartQuery = query;

            return Task.FromResult(Result<FxChartVisualView>.Success(new FxChartVisualView(
                query.MarketId,
                "NMR/YUU",
                PriceScale,
                2,
                query.BucketSeconds,
                1_787_210_100L,
                buckets,
                1L,
                new FxVisualCacheKey(1L, 1L, 1L, 1L))));
        }

        public Task<Result<FxMarketView>> GetFxMarketAsync(
            GetFxMarketQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<FxRateVisualView>> GetFxRateVisualAsync(
            GetFxRateVisualQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<FxBoardVisualView>> GetFxBoardVisualAsync(
            GetFxBoardVisualQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<FxOrderPageView>> ListFxOrdersAsync(
            ListFxOrdersQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<FxTradeHistoryPageView>> GetFxHistoryAsync(
            GetFxHistoryQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<FxOrderView>> PlaceFxOrderAsync(
            PlaceFxOrderCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<FxOrderView>> CancelFxOrderAsync(
            CancelFxOrderCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedAccounts : ICustomerAccountApplicationService
    {
        public Task<Result<CustomerAccountView>> RegisterCustomerAccountAsync(
            RegisterCustomerAccountCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CustomerAccountStatusView>> GetCustomerAccountStatusAsync(
            GetCustomerAccountStatusQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<LinkGrantView>> CreateLinkGrantAsync(
            CreateLinkGrantCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CustomerAccountView>> ConsumeLinkGrantAsync(
            ConsumeLinkGrantCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result> UnlinkDiscordIdentityAsync(
            UnlinkDiscordIdentityCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static IReadOnlyList<FxOhlcBucket> Flat(int count, long price)
    {
        List<FxOhlcBucket> buckets = [];

        for (int index = 0; index < count; index++)
        {
            buckets.Add(new FxOhlcBucket(
                FxMarketId.FromValue(EntityIdValue.FromBits(1)),
                60,
                1_787_208_540L + (index * 600L),
                price,
                price,
                price,
                price,
                BaseVolumeMinor: 1_000,
                QuoteVolumeMinor: 1_500,
                LastTradeSequenceNo: index + 1,
                ProjectionVersion: 1));
        }

        return buckets;
    }

    private static (FxEndpoints Endpoints, StubMarkets Markets) Build(
        IReadOnlyList<FxOhlcBucket> buckets)
    {
        StubMarkets markets = new(buckets);

        FxEndpoints endpoints = new(
            markets,
            new UnusedAccounts(),
            CanonicalTextCatalog.Create(),
            new SkiaFxChartImageRenderer(
                new FxChartRenderer(new StubFontProvider()), new SilentDiagnostics()));

        return (endpoints, markets);
    }

    private static DiscordEndpointContext Context() =>
        new(1UL, 2UL, 3UL, 4UL, "ja", "/fx chart", Numera.Discord.Abstractions.AuthorizationLevel.Customer, string.Empty);

    private static int PngDimension(byte[] content, int offset) =>
        (content[offset] << 24)
        | (content[offset + 1] << 16)
        | (content[offset + 2] << 8)
        | content[offset + 3];

    [TestMethod]
    public async Task TheChartAttachesAPngOfTheSizeSection27Ag3Declares()
    {
        (FxEndpoints endpoints, _) = Build(Flat(2, 150));

        DiscordEndpointResponse response = await endpoints.ChartAsync(
            Context(), MarketReference, "1H", "CANDLE", CancellationToken.None);

        Assert.AreEqual(ViewKeys.FxChart, response.ViewKey);

        DiscordResponseAttachment attachment = response.Body.Attachment!;

        Assert.IsNotNull(attachment);
        Assert.AreEqual("fx-chart.png", attachment.FileName);
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, attachment.Content[..4]);
        Assert.AreEqual(1280, PngDimension(attachment.Content, 16));
        Assert.AreEqual(720, PngDimension(attachment.Content, 20));
    }

    [TestMethod]
    public async Task TheChartNeverPublishesTheInternalMarketIdentifier()
    {
        (FxEndpoints endpoints, _) = Build(Flat(2, 150));

        DiscordEndpointResponse response = await endpoints.ChartAsync(
            Context(), MarketReference, "1H", null, CancellationToken.None);

        Assert.AreEqual("NMR/YUU", response.ViewData["pair"]);

        foreach (string value in response.ViewData.Values)
        {
            Assert.DoesNotContain(MarketReference, value);
        }

        DiscordEmbedPayload embed = new CatalogResponseComposer(CanonicalTextCatalog.Create())
            .Compose(response);

        Assert.DoesNotContain(MarketReference, embed.Description);
        Assert.DoesNotContain("{", embed.Description);
        Assert.DoesNotContain("{", embed.Title);
        StringAssert.Contains(embed.Description, "NMR/YUU");
    }

    [TestMethod]
    public async Task EveryPeriodPinsTheBucketAndWindowSection27Ag8Fixes()
    {
        (string Period, int Bucket, long Window)[] expected =
        [
            ("1H", 60, 3_600L),
            ("24H", 300, 86_400L),
            ("7D", 3600, 604_800L),
            ("30D", 3600, 2_592_000L),
        ];

        foreach ((string period, int bucket, long window) in expected)
        {
            (FxEndpoints endpoints, StubMarkets markets) = Build(Flat(2, 150));

            DiscordEndpointResponse response = await endpoints.ChartAsync(
                Context(), MarketReference, period, null, CancellationToken.None);

            Assert.AreEqual(bucket, markets.LastChartQuery!.BucketSeconds, period);
            Assert.AreEqual(window, markets.LastChartQuery!.WindowSeconds, period);
            Assert.AreEqual(period, response.ViewData["period"], period);
        }
    }

    [TestMethod]
    public async Task AnUnknownPeriodFallsBackToTheShortestWindow()
    {
        (FxEndpoints endpoints, StubMarkets markets) = Build(Flat(2, 150));

        _ = await endpoints.ChartAsync(
            Context(), MarketReference, "99Y", null, CancellationToken.None);

        Assert.AreEqual(60, markets.LastChartQuery!.BucketSeconds);
        Assert.AreEqual(3_600L, markets.LastChartQuery!.WindowSeconds);
    }

    [TestMethod]
    public async Task AMarketWithoutCompletedBucketsRendersNoAttachment()
    {
        (FxEndpoints endpoints, _) = Build([]);

        DiscordEndpointResponse response = await endpoints.ChartAsync(
            Context(), MarketReference, "1H", null, CancellationToken.None);

        Assert.AreEqual(ViewKeys.FxChartEmpty, response.ViewKey);
        Assert.IsNull(response.Body.Attachment);
    }

    [TestMethod]
    public async Task AMalformedMarketReferenceIsRejectedBeforeTheQuery()
    {
        (FxEndpoints endpoints, StubMarkets markets) = Build(Flat(2, 150));

        DiscordEndpointResponse response = await endpoints.ChartAsync(
            Context(), "not-a-market", "1H", null, CancellationToken.None);

        Assert.AreEqual(DiscordResponseKind.Failure, response.Kind);
        Assert.IsNull(markets.LastChartQuery);
    }
}
