using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Numera.Discord.Abstractions;
using Numera.Discord.Commands;
using Numera.Discord.Endpoints;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Host.Configuration;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Host.Tests;

[TestClass]
public sealed class FxWalkthroughTests
{
    private const ulong Operator = 720_000_000_000_000_001UL;
    private const ulong Maker = 720_000_000_000_000_002UL;
    private const ulong Taker = 720_000_000_000_000_003UL;
    private const ulong HomeGuild = 1_284_327_110_349_164_587UL;
    private const ulong AwayGuild = 1_520_411_351_489_445_999UL;

    private const string HomeBank = "NUM0001";
    private const string AwayBank = "AWY0001";
    private const string HomeCode = "NMR";
    private const string AwayCode = "AWY";
    private const string Product = "DEMAND01";

    private const long MinimumCapital = 1_000_000L;
    private const long Genesis = 100_000_000L;
    private const long LoanPrincipal = 500_000L;

    private const long PriceScale = 100L;
    private const long TickSize = 1L;
    private const long LotSize = 100L;
    private const long TradePriceUnits = 150L;
    private const long TradeBaseMinor = 1_000L;

    private ulong interaction = 7_000_000_000_000_000_001UL;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task TwoEconomiesReachAFilledFxTradeThroughTheCanonicalRoute()
    {
        string root = Path.Combine(Path.GetTempPath(), "numera-fx", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(root, "data"));

        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        NumeraHost.Configure(builder, new NumeraOptions(
            HostEnvironmentKind.Production,
            ApplicationId: 1,
            TestGuildId: 0,
            ControlGuildId: HomeGuild,
            CommandRegistrationMode.Global,
            [Operator],
            Path.Combine(root, "data", "economy.db"),
            NumeraOptionsValidator.CanonicalBusyTimeoutSeconds,
            NumeraOptionsValidator.CanonicalInteractionSessionMinutes,
            NumeraOptionsValidator.CanonicalStatementPageSize));

        using IHost host = builder.Build();
        CancellationToken token = TestContext.CancellationTokenSource.Token;

        SqliteDatabaseOptions options = host.Services.GetRequiredService<SqliteDatabaseOptions>();
        SqliteConnectionFactory factory = host.Services.GetRequiredService<SqliteConnectionFactory>();

        new SqliteDatabaseInitializer(
            options, factory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
            .Initialize(1_787_000_000_000);

        SqliteDatabaseBootstrapService bootstrap = new(
            factory, static () => Guid.CreateVersion7().ToByteArray(bigEndian: true));

        _ = bootstrap.SynchronizeSystemOwners(
            [Operator.ToString(System.Globalization.CultureInfo.InvariantCulture)], 1_787_000_000_000);

        string homeBook = Initialize(bootstrap, HomeGuild);
        string awayBook = Initialize(bootstrap, AwayGuild);

        SqliteWriteCoordinator coordinator = host.Services.GetRequiredService<SqliteWriteCoordinator>();
        coordinator.Start();

        try
        {
            ManageEndpoints manage = host.Services.GetRequiredService<ManageEndpoints>();
            ManageBankEndpoints manageBank = host.Services.GetRequiredService<ManageBankEndpoints>();
            ManageFxEndpoints manageFx = host.Services.GetRequiredService<ManageFxEndpoints>();
            AccountEndpoints account = host.Services.GetRequiredService<AccountEndpoints>();
            BankEndpoints bank = host.Services.GetRequiredService<BankEndpoints>();
            BankQueryEndpoints queries = host.Services.GetRequiredService<BankQueryEndpoints>();
            FxEndpoints fx = host.Services.GetRequiredService<FxEndpoints>();
            SuggestionEndpoints suggest = host.Services.GetRequiredService<SuggestionEndpoints>();

            await OpenEconomyAsync(manage, manageBank, HomeGuild, homeBook, HomeCode, HomeBank, token);
            await OpenEconomyAsync(manage, manageBank, AwayGuild, awayBook, AwayCode, AwayBank, token);

            await RegisterAsync(account, Maker, "maker", token);
            await RegisterAsync(account, Taker, "taker", token);

            foreach (ulong trader in new[] { Maker, Taker })
            {
                await OpenAndBorrowAsync(bank, queries, trader, HomeGuild, HomeBank, token);
                await OpenAndBorrowAsync(bank, queries, trader, AwayGuild, AwayBank, token);
            }

            Deliver(
                DiscordInteractionKind.SlashCommand,
                await manageFx.FxMarketAsync(
                    Context(HomeGuild, Operator, AuthorizationLevel.GuildOperator, "/manage fx-market"),
                    "create",
                    null,
                    HomeCode,
                    AwayCode,
                    HomeBank,
                    PriceScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    TickSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    LotSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    null,
                    null,
                    token));

            IReadOnlyList<DiscordAutocompleteOption> markets = await suggest.SuggestFxMarketsAsync(
                new DiscordAutocompleteRequest(
                    Maker, HomeGuild, "/manage fx-market", "market", string.Empty),
                token);

            Assert.AreEqual(1, markets.Count);
            string market = markets[0].Value;

            Deliver(
                DiscordInteractionKind.SlashCommand,
                await manageFx.FxMarketAsync(
                    Context(HomeGuild, Operator, AuthorizationLevel.GuildOperator, "/manage fx-market"),
                    "policy", market, null, null, null, null, null, null, "0", "0", "500", token));

            Deliver(
                DiscordInteractionKind.SlashCommand,
                await manageFx.FxMarketAsync(
                    Context(HomeGuild, Operator, AuthorizationLevel.GuildOperator, "/manage fx-market"),
                    "override", market, null, null, null, null, null, null, null, null, null, token));

            Assert.AreEqual("ACTIVE", Text(factory, "SELECT status FROM fx_markets;"));

            await PlaceAsync(fx, suggest, Maker, "SELL_BASE", "LIMIT", TradePriceUnits, token);

            Assert.AreEqual(1L, Scalar(factory, "SELECT COUNT(*) FROM fx_orders WHERE status = 'OPEN';"));

            IReadOnlyList<DiscordAutocompleteOption> resting = await suggest.SuggestFxOrdersAsync(
                new DiscordAutocompleteRequest(Maker, HomeGuild, "/fx cancel", "order", string.Empty),
                token);

            Assert.AreEqual(1, resting.Count);
            StringAssert.Contains(resting[0].Name, HomeCode + "/" + AwayCode);

            await PlaceAsync(fx, suggest, Taker, "BUY_BASE", "MARKET_IOC", null, token);

            Assert.AreEqual(1L, Scalar(factory, "SELECT COUNT(*) FROM fx_trades;"));
            Assert.AreEqual(
                TradeBaseMinor,
                Scalar(factory, "SELECT SUM(base_minor) FROM fx_trades;"));
            Assert.AreEqual(
                0L,
                Scalar(factory, "SELECT COUNT(*) FROM fx_orders WHERE status IN ('OPEN','PARTIALLY_FILLED');"));

            Deliver(
                DiscordInteractionKind.SlashCommand,
                await fx.MarketAsync(
                    Context(HomeGuild, Maker, AuthorizationLevel.Customer, "/fx market"), market, token));

            Deliver(
                DiscordInteractionKind.SlashCommand,
                await fx.RateAsync(
                    Context(HomeGuild, Maker, AuthorizationLevel.Customer, "/fx rate"), market, token));

            Deliver(
                DiscordInteractionKind.SlashCommand,
                await fx.BoardAsync(
                    Context(HomeGuild, Maker, AuthorizationLevel.Customer, "/fx board"), market, token));

            Deliver(
                DiscordInteractionKind.SlashCommand,
                await fx.OrdersAsync(
                    Context(HomeGuild, Maker, AuthorizationLevel.Customer, "/fx orders"), null, token));

            Deliver(
                DiscordInteractionKind.SlashCommand,
                await fx.HistoryAsync(
                    Context(HomeGuild, Maker, AuthorizationLevel.Customer, "/fx history"),
                    market,
                    null,
                    token));
        }
        finally
        {
            await coordinator.DisposeAsync();
            using (SqliteConnection pooled = factory.OpenRuntimeConnection())
            {
                SqliteConnection.ClearPool(pooled);
            }

            host.Dispose();

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string Initialize(SqliteDatabaseBootstrapService bootstrap, ulong guildId)
    {
        EconomyBootstrapOutcome outcome = bootstrap.InitializeEconomy(
            guildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Asia/Tokyo",
            MinimumCapital,
            1_787_000_000_000);

        Assert.IsTrue(outcome.IsSuccess, outcome.Detail);

        return outcome.IssuanceAccountingBookId;
    }

    private async Task OpenEconomyAsync(
        ManageEndpoints manage,
        ManageBankEndpoints manageBank,
        ulong guildId,
        string issuanceBook,
        string currencyCode,
        string institutionCode,
        CancellationToken token)
    {
        Deliver(
            DiscordInteractionKind.SlashCommand,
            await manage.CreateCurrencyAsync(
                Context(guildId, Operator, AuthorizationLevel.GuildOperator, "/manage currency-create"),
                issuanceBook,
                currencyCode + "通貨",
                currencyCode,
                currencyCode[..1],
                2,
                Genesis,
                token));

        DiscordEndpointResponse draft = Deliver(
            DiscordInteractionKind.SlashCommand,
            await manageBank.BankCreateAsync(
                Context(guildId, Operator, AuthorizationLevel.GuildOperator, "/manage bank-create"),
                institutionCode,
                token));

        DiscordEndpointResponse modal = Deliver(
            DiscordInteractionKind.Button,
            await manageBank.OpenBankCreateInputAsync(
                Context(guildId, Operator, AuthorizationLevel.GuildOperator, "bank-create-input"),
                new DiscordComponentInput("bank-create-input", TokenOf(draft)),
                token));

        DiscordEndpointResponse review = Deliver(
            DiscordInteractionKind.ModalSubmit,
            await manageBank.SubmitBankCreateAsync(
                Context(
                    guildId,
                    Operator,
                    AuthorizationLevel.GuildOperator,
                    "bank-create",
                    ModalTokenOf(modal)),
                new BankCreateForm
                {
                    BankName = institutionCode + "銀行",
                    BranchCode = "001",
                    BranchName = "本店",
                    ProductCode = Product,
                    ProductName = "普通預金",
                },
                token));

        DiscordEndpointResponse created = Deliver(
            DiscordInteractionKind.Button,
            await manageBank.CommitBankCreateAsync(
                Context(guildId, Operator, AuthorizationLevel.GuildOperator, "bank-create-commit"),
                new DiscordComponentInput("bank-create-commit", TokenOf(review)),
                token));

        DiscordEndpointResponse capitalModal = Deliver(
            DiscordInteractionKind.Button,
            await manageBank.OpenBankCapitalInputAsync(
                Context(guildId, Operator, AuthorizationLevel.GuildOperator, "bank-capital-input"),
                new DiscordComponentInput("bank-capital-input", TokenOf(created)),
                token));

        DiscordEndpointResponse capitalReview = Deliver(
            DiscordInteractionKind.ModalSubmit,
            await manageBank.SubmitBankCapitalAsync(
                Context(
                    guildId,
                    Operator,
                    AuthorizationLevel.GuildOperator,
                    "bank-capital",
                    ModalTokenOf(capitalModal)),
                new BankCapitalForm
                {
                    Amount = MinimumCapital.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    SourceInstitutionCode = string.Empty,
                },
                token));

        DiscordEndpointResponse contributed = Deliver(
            DiscordInteractionKind.Button,
            await manageBank.CommitBankCapitalAsync(
                Context(guildId, Operator, AuthorizationLevel.GuildOperator, "bank-capital-commit"),
                new DiscordComponentInput("bank-capital-commit", TokenOf(capitalReview)),
                token));

        Deliver(
            DiscordInteractionKind.Button,
            await manageBank.ActivateBankAsync(
                Context(guildId, Operator, AuthorizationLevel.GuildOperator, "bank-activate"),
                new DiscordComponentInput("bank-activate", TokenOf(contributed)),
                token));
    }

    private async Task RegisterAsync(
        AccountEndpoints account,
        ulong userId,
        string handle,
        CancellationToken token) =>
        Deliver(
            DiscordInteractionKind.SlashCommand,
            await account.RegisterAsync(
                Context(HomeGuild, userId, AuthorizationLevel.Unregistered, "/account register"),
                handle,
                handle,
                token));

    private async Task OpenAndBorrowAsync(
        BankEndpoints bank,
        BankQueryEndpoints queries,
        ulong userId,
        ulong guildId,
        string institutionCode,
        CancellationToken token)
    {
        Deliver(
            DiscordInteractionKind.SlashCommand,
            await bank.OpenAsync(
                Context(guildId, userId, AuthorizationLevel.Customer, "/bank open"),
                institutionCode,
                token));

        DiscordEndpointResponse banks = Deliver(
            DiscordInteractionKind.SlashCommand,
            await queries.ListAsync(
                Context(guildId, userId, AuthorizationLevel.Customer, "/bank list"), token));

        DiscordEndpointResponse detail = Deliver(
            DiscordInteractionKind.SelectMenu,
            await queries.SelectBankDetailAsync(
                Context(guildId, userId, AuthorizationLevel.Customer, "bank-detail"),
                new DiscordComponentInput("bank-detail", SelectTokenOf(banks), [institutionCode]),
                token));

        DiscordEndpointResponse loanModal = Deliver(
            DiscordInteractionKind.Button,
            await queries.OpenBankLoanInputAsync(
                Context(guildId, userId, AuthorizationLevel.Customer, "bank-loan-input"),
                new DiscordComponentInput("bank-loan-input", TokenOf(detail)),
                token));

        DiscordEndpointResponse loanReview = Deliver(
            DiscordInteractionKind.ModalSubmit,
            await queries.SubmitBankLoanAsync(
                Context(
                    guildId,
                    userId,
                    AuthorizationLevel.Customer,
                    "bank-loan",
                    ModalTokenOf(loanModal)),
                new BankLoanForm
                {
                    Principal = LoanPrincipal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ProductCode = Product,
                },
                token));

        Deliver(
            DiscordInteractionKind.Button,
            await queries.CommitBankLoanAsync(
                Context(guildId, userId, AuthorizationLevel.Customer, "bank-loan-commit"),
                new DiscordComponentInput("bank-loan-commit", TokenOf(loanReview)),
                token));
    }

    private async Task PlaceAsync(
        FxEndpoints fx,
        SuggestionEndpoints suggest,
        ulong userId,
        string side,
        string type,
        long? priceUnits,
        CancellationToken token)
    {
        IReadOnlyList<DiscordAutocompleteOption> accounts = await suggest.SuggestDepositAccountsAsync(
            new DiscordAutocompleteRequest(userId, HomeGuild, "/fx order", "source", string.Empty),
            token);

        Assert.AreEqual(2, accounts.Count);

        DiscordAutocompleteOption home = accounts.First(option => option.Name.StartsWith(HomeBank, StringComparison.Ordinal));
        DiscordAutocompleteOption away = accounts.First(option => option.Name.StartsWith(AwayBank, StringComparison.Ordinal));

        IReadOnlyList<DiscordAutocompleteOption> markets = await suggest.SuggestFxMarketsAsync(
            new DiscordAutocompleteRequest(userId, HomeGuild, "/fx order", "market", string.Empty),
            token);

        Deliver(
            DiscordInteractionKind.SlashCommand,
            await fx.OrderAsync(
                Context(HomeGuild, userId, AuthorizationLevel.Customer, "/fx order"),
                markets[0].Value,
                side,
                type,
                TradeBaseMinor,
                side == "SELL_BASE" ? home.Value : away.Value,
                side == "SELL_BASE" ? away.Value : home.Value,
                priceUnits?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                type == "LIMIT" ? null : "500",
                token));
    }

    private DiscordEndpointContext Context(
        ulong guildId,
        ulong userId,
        AuthorizationLevel level,
        string commandPath,
        string sessionToken = "") =>
        new(interaction++, userId, guildId, 1UL, "ja", commandPath, level, sessionToken);

    private static readonly CatalogResponseComposer Composer = new(CanonicalTextCatalog.Create());

    private static DiscordEndpointResponse Deliver(
        DiscordInteractionKind kind,
        DiscordEndpointResponse response)
    {
        Assert.AreNotEqual(DiscordResponseKind.Failure, response.Kind, Detail(response));

        ResponsePlan plan = new DiscordResponseStateMachine(kind).PlanResponse(response.Kind);

        Assert.IsTrue(plan.IsPermitted, $"{kind}/{response.Kind}/{plan.Failure}/{response.ViewKey}");

        if (response.Kind == DiscordResponseKind.Modal)
        {
            Assert.IsNotEmpty(Composer.ResolveModalCustomId(response), response.ViewKey);
            return response;
        }

        DiscordEmbedPayload embed = Composer.Compose(response);

        Assert.DoesNotContain("{", embed.Title, response.ViewKey);
        Assert.DoesNotContain("{", embed.Description, response.ViewKey);

        _ = Composer.ComposeComponents(response);

        return response;
    }

    private static string Detail(DiscordEndpointResponse response) =>
        response.Failure is { } failure
            ? failure.CategoryToken + "/" + failure.ErrorCode + "/" + failure.Field
            : response.ViewKey;

    private static string TokenOf(DiscordEndpointResponse response)
    {
        Assert.AreEqual(1, response.Body.Components.Buttons.Count, response.ViewKey);

        string customId = response.Body.Components.Buttons[0].CustomId;
        return customId[(customId.LastIndexOf(':') + 1)..];
    }

    private static string SelectTokenOf(DiscordEndpointResponse response)
    {
        Assert.IsNotNull(response.Body.Components.Select, response.ViewKey);

        string customId = response.Body.Components.Select!.CustomId;
        return customId[(customId.LastIndexOf(':') + 1)..];
    }

    private static string ModalTokenOf(DiscordEndpointResponse response)
    {
        Assert.AreEqual(DiscordResponseKind.Modal, response.Kind, Detail(response));

        string customId = response.ViewData["customId"];
        return customId[(customId.LastIndexOf(':') + 1)..];
    }

    private static long Scalar(SqliteConnectionFactory factory, string sql)
    {
        using SqliteConnection connection = factory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() is long value ? value : 0L;
    }

    private static string Text(SqliteConnectionFactory factory, string sql)
    {
        using SqliteConnection connection = factory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }
}
