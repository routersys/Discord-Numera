using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Numera.Discord.Abstractions;
using Numera.Discord.Commands;
using Numera.Discord.Gateway;
using Numera.Discord.Endpoints;
using Numera.Discord.Rendering;
using Numera.Host.Configuration;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Host.Tests;

[TestClass]
public sealed class EconomyWalkthroughTests
{
    internal const ulong Operator = 700_000_000_000_000_001UL;
    private const ulong Depositor = 700_000_000_000_000_002UL;
    private const ulong Beneficiary = 700_000_000_000_000_003UL;
    internal const ulong Guild = 1_284_327_110_349_164_587UL;
    private const string Institution = "NUM0001";
    private const long MinimumCapital = 1_000_000L;
    private const long Genesis = 100_000_000L;

    internal sealed class Walkthrough : IAsyncDisposable
    {
        private readonly string root;
        private ulong interaction = 5_000_000_000_000_000_001UL;

        private Walkthrough(string root, IHost host)
        {
            this.root = root;
            Host = host;
        }

        public IHost Host { get; }

        public string IssuanceBookId { get; private set; } = string.Empty;

        public static Walkthrough Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-walk", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(Path.Combine(root, "data"));

            HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

            NumeraHost.Configure(builder, new NumeraOptions(
                HostEnvironmentKind.Production,
                ApplicationId: 1,
                TestGuildId: 0,
                ControlGuildId: Guild,
                CommandRegistrationMode.Global,
                [Operator],
                Path.Combine(root, "data", "economy.db"),
                NumeraOptionsValidator.CanonicalBusyTimeoutSeconds,
                NumeraOptionsValidator.CanonicalInteractionSessionMinutes,
                NumeraOptionsValidator.CanonicalStatementPageSize));

            Walkthrough walkthrough = new(root, builder.Build());
            walkthrough.Bootstrap();
            return walkthrough;
        }

        private void Bootstrap()
        {
            SqliteDatabaseOptions options = Host.Services.GetRequiredService<SqliteDatabaseOptions>();
            SqliteConnectionFactory factory = Host.Services.GetRequiredService<SqliteConnectionFactory>();

            new SqliteDatabaseInitializer(
                options, factory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_787_000_000_000);

            SqliteDatabaseBootstrapService bootstrap = new(
                factory, static () => Guid.CreateVersion7().ToByteArray(bigEndian: true));

            _ = bootstrap.SynchronizeSystemOwners(
                [Operator.ToString(System.Globalization.CultureInfo.InvariantCulture)], 1_787_000_000_000);

            EconomyBootstrapOutcome outcome = bootstrap.InitializeEconomy(
                Guild.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Asia/Tokyo",
                MinimumCapital,
                1_787_000_000_000);

            Assert.IsTrue(outcome.IsSuccess, outcome.Detail);
            IssuanceBookId = outcome.IssuanceAccountingBookId;

            Host.Services.GetRequiredService<SqliteWriteCoordinator>().Start();
        }

        public TEndpoint Endpoint<TEndpoint>()
            where TEndpoint : notnull =>
            Host.Services.GetRequiredService<TEndpoint>();

        public DiscordEndpointContext Context(
            ulong userId,
            AuthorizationLevel level,
            string commandPath,
            string sessionToken = "") =>
            new(interaction++, userId, Guild, 1UL, "ja", commandPath, level, sessionToken);

        public DiscordEndpointContext ContextIn(
            ulong guildId,
            ulong userId,
            AuthorizationLevel level,
            string commandPath,
            string sessionToken = "") =>
            new(interaction++, userId, guildId, 1UL, "ja", commandPath, level, sessionToken);

        public string InitializeEconomy(ulong guildId, long minimumCapital)
        {
            SqliteConnectionFactory factory = Host.Services.GetRequiredService<SqliteConnectionFactory>();

            SqliteDatabaseBootstrapService bootstrap = new(
                factory, static () => Guid.CreateVersion7().ToByteArray(bigEndian: true));

            EconomyBootstrapOutcome outcome = bootstrap.InitializeEconomy(
                guildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Asia/Tokyo",
                minimumCapital,
                1_787_000_000_000);

            Assert.IsTrue(outcome.IsSuccess, outcome.Detail);

            return outcome.IssuanceAccountingBookId;
        }

        private static readonly CatalogResponseComposer Composer =
            new(CanonicalTextCatalog.Create());

        public static DiscordEndpointResponse Deliver(
            DiscordInteractionKind kind,
            DiscordEndpointResponse response)
        {
            Assert.AreNotEqual(DiscordResponseKind.Failure, response.Kind, Detail(response));

            ResponsePlan plan = new DiscordResponseStateMachine(kind).PlanResponse(response.Kind);

            Assert.IsTrue(
                plan.IsPermitted,
                $"{kind} は {response.Kind} を返せません（{plan.Failure}）。{response.ViewKey}");

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

        public static string TokenOf(DiscordEndpointResponse response)
        {
            Assert.AreNotEqual(DiscordResponseKind.Failure, response.Kind, Detail(response));
            Assert.AreEqual(1, response.Body.Components.Buttons.Count, response.ViewKey);

            string customId = response.Body.Components.Buttons[0].CustomId;
            return customId[(customId.LastIndexOf(':') + 1)..];
        }

        public static string SelectTokenOf(DiscordEndpointResponse response)
        {
            Assert.AreNotEqual(DiscordResponseKind.Failure, response.Kind, Detail(response));
            Assert.IsNotNull(response.Body.Components.Select, response.ViewKey);

            string customId = response.Body.Components.Select!.CustomId;
            return customId[(customId.LastIndexOf(':') + 1)..];
        }

        public static string ModalTokenOf(DiscordEndpointResponse response)
        {
            Assert.AreEqual(DiscordResponseKind.Modal, response.Kind, Detail(response));

            string customId = response.ViewData["customId"];
            return customId[(customId.LastIndexOf(':') + 1)..];
        }

        public static string Detail(DiscordEndpointResponse response) =>
            response.Failure is { } failure
                ? failure.CategoryToken + "/" + failure.ErrorCode + "/" + failure.Field
                : response.ViewKey;

        public static void Succeeded(DiscordEndpointResponse response) =>
            Assert.AreNotEqual(DiscordResponseKind.Failure, response.Kind, Detail(response));

        public long Scalar(string sql)
        {
            SqliteConnectionFactory factory = Host.Services.GetRequiredService<SqliteConnectionFactory>();
            using SqliteConnection connection = factory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public string Text(string sql)
        {
            SqliteConnectionFactory factory = Host.Services.GetRequiredService<SqliteConnectionFactory>();
            using SqliteConnection connection = factory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar() as string ?? string.Empty;
        }

        public string BeneficiaryAccountNumber() => Text("""
            SELECT d.account_number FROM deposit_accounts AS d
            JOIN customer_accounts AS c ON c.customer_account_id = d.customer_account_id
            WHERE c.public_handle = 'beneficiary';
            """);

        public long BalanceOf(string handle) => Scalar($"""
            SELECT p.posted_balance_minor FROM ledger_balance_projections AS p
            JOIN deposit_accounts AS d ON d.ledger_account_id = p.ledger_account_id
            JOIN customer_accounts AS c ON c.customer_account_id = d.customer_account_id
            WHERE c.public_handle = '{handle}';
            """);

        public async ValueTask DisposeAsync()
        {
            await Host.Services.GetRequiredService<SqliteWriteCoordinator>().DisposeAsync();

            SqliteConnectionFactory factory = Host.Services.GetRequiredService<SqliteConnectionFactory>();
            using (SqliteConnection pooled = factory.OpenRuntimeConnection())
            {
                SqliteConnection.ClearPool(pooled);
            }

            Host.Dispose();

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [TestMethod]
    public async Task TheManagementPanelEditsTheBusinessCalendarThroughItsReviewStep()
    {
        await using Walkthrough walk = Walkthrough.Create();
        CancellationToken token = TestContext.CancellationTokenSource.Token;

        ManagePanelEndpoints panel = walk.Endpoint<ManagePanelEndpoints>();

        DiscordEndpointResponse opened = Walkthrough.Deliver(
            DiscordInteractionKind.SlashCommand,
            await panel.ShowAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "/manage panel"), token));

        string session = Walkthrough.SelectTokenOf(opened);

        Walkthrough.Deliver(
            DiscordInteractionKind.SelectMenu,
            await panel.SelectCategoryAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "panel-category"),
                new DiscordComponentInput("panel-category", session, ["economy-calendar"]),
                token));

        DiscordEndpointResponse editor = Walkthrough.Deliver(
            DiscordInteractionKind.SelectMenu,
            await panel.SelectActionAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "panel-action"),
                new DiscordComponentInput("panel-action", session, ["calendar-set"]),
                token));

        Assert.AreEqual(ViewKeys.ManagePanelEditor, editor.ViewKey);

        DiscordEndpointResponse modal = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await panel.OpenEditorAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "panel-edit"),
                new DiscordComponentInput("panel-edit", session),
                token));

        DiscordEndpointResponse review = Walkthrough.Deliver(
            DiscordInteractionKind.ModalSubmit,
            await panel.SubmitCalendarSetAsync(
                walk.Context(
                    Operator,
                    AuthorizationLevel.GuildOperator,
                    "panel-calendar-set",
                    Walkthrough.ModalTokenOf(modal)),
                new PanelCalendarSetForm
                {
                    LocalDate = "2026-08-20",
                    DayClass = "NON_BUSINESS_DAY",
                    Description = "臨時休業",
                },
                token));

        Assert.AreEqual(ViewKeys.ManagePanelReview, review.ViewKey);
        Assert.AreEqual("営業日（既定）", review.ViewData["current"]);
        StringAssert.Contains(review.ViewData["after"], "NON_BUSINESS_DAY");
        Assert.AreEqual(0L, walk.Scalar("SELECT COUNT(*) FROM economy_calendar_overrides;"));

        DiscordEndpointResponse applied = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await panel.CommitEditorAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "panel-commit"),
                new DiscordComponentInput("panel-commit", Walkthrough.TokenOf(review)),
                token));

        Assert.AreEqual(ViewKeys.ManagePanelApplied, applied.ViewKey);
        Assert.AreEqual(1L, walk.Scalar("SELECT COUNT(*) FROM economy_calendar_overrides;"));

        Assert.AreEqual(
            "NON_BUSINESS_DAY",
            walk.Text("SELECT day_class FROM economy_calendar_overrides LIMIT 1;"));
    }

    [TestMethod]
    public async Task TheManagementPanelPublishesThePrudentialAndTrustPolicies()
    {
        await using Walkthrough walk = Walkthrough.Create();
        CancellationToken token = TestContext.CancellationTokenSource.Token;

        ManagePanelEndpoints panel = walk.Endpoint<ManagePanelEndpoints>();

        string trust = await PanelSessionAsync(
            walk, panel, "currency-trust", "trust-policy", token);

        DiscordEndpointResponse trustReview = Walkthrough.Deliver(
            DiscordInteractionKind.ModalSubmit,
            await panel.SubmitTrustPolicyAsync(
                walk.Context(
                    Operator, AuthorizationLevel.GuildOperator, "panel-trust-policy", trust),
                new PanelTrustPolicyForm
                {
                    Established = "604800,3,2",
                    Trusted = "2592000,10,5",
                    Reserve = "7776000,30,12",
                },
                token));

        Assert.AreEqual("なし", trustReview.ViewData["current"]);

        Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await panel.CommitEditorAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "panel-commit"),
                new DiscordComponentInput("panel-commit", Walkthrough.TokenOf(trustReview)),
                token));

        Assert.AreEqual(
            1L,
            walk.Scalar(
                "SELECT COUNT(*) FROM currency_trust_policy_versions WHERE status = 'PUBLISHED';"));

        string prudential = await PanelSessionAsync(
            walk, panel, "prudential-resolution", "prudential-policy", token);

        DiscordEndpointResponse prudentialReview = Walkthrough.Deliver(
            DiscordInteractionKind.ModalSubmit,
            await panel.SubmitPrudentialPolicyAsync(
                walk.Context(
                    Operator, AuthorizationLevel.GuildOperator, "panel-prudential-policy", prudential),
                new PanelPrudentialPolicyForm
                {
                    Cet1 = "500,800",
                    Leverage = "350,400",
                    Liquidity = "10500",
                    MinimumCapital = "2000000",
                },
                token));

        StringAssert.Contains(prudentialReview.ViewData["after"], "10500");

        Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await panel.CommitEditorAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "panel-commit"),
                new DiscordComponentInput("panel-commit", Walkthrough.TokenOf(prudentialReview)),
                token));

        Assert.AreEqual(
            10500L,
            walk.Scalar(
                """
                SELECT minimum_liquidity_bps FROM prudential_policy_versions
                WHERE status = 'PUBLISHED' ORDER BY version DESC LIMIT 1;
                """));
    }

    [TestMethod]
    public async Task TheManagementPanelRejectsAnUnknownPaymentNetwork()
    {
        await using Walkthrough walk = Walkthrough.Create();
        CancellationToken token = TestContext.CancellationTokenSource.Token;

        ManagePanelEndpoints panel = walk.Endpoint<ManagePanelEndpoints>();

        string session = await PanelSessionAsync(
            walk, panel, "payment-network", "network-state", token);

        DiscordEndpointResponse review = Walkthrough.Deliver(
            DiscordInteractionKind.ModalSubmit,
            await panel.SubmitNetworkStateAsync(
                walk.Context(
                    Operator, AuthorizationLevel.GuildOperator, "panel-network-state", session),
                new PanelNetworkStateForm { NetworkCode = "MISSING", DesiredState = "SUSPENDED" },
                token));

        Assert.AreEqual("なし", review.ViewData["current"]);

        DiscordEndpointResponse commit = await panel.CommitEditorAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "panel-commit"),
            new DiscordComponentInput("panel-commit", Walkthrough.TokenOf(review)),
            token);

        Assert.AreEqual(DiscordResponseKind.Failure, commit.Kind);
    }

    [TestMethod]
    public async Task TheManagementPanelPublishesThePresentationPalette()
    {
        await using Walkthrough walk = Walkthrough.Create();
        CancellationToken token = TestContext.CancellationTokenSource.Token;

        ManagePanelEndpoints panel = walk.Endpoint<ManagePanelEndpoints>();

        string session = await PanelSessionAsync(
            walk, panel, "presentation", "presentation-profile", token);

        DiscordEndpointResponse review = Walkthrough.Deliver(
            DiscordInteractionKind.ModalSubmit,
            await panel.SubmitPresentationAsync(
                walk.Context(
                    Operator, AuthorizationLevel.GuildOperator, "panel-presentation", session),
                new PanelPresentationForm
                {
                    Information = "1D4ED8",
                    Success = "16A34A",
                    Warning = "F59E0B",
                    Error = "DC2626",
                    Neutral = "6B7280",
                },
                token));

        Assert.AreEqual("なし", review.ViewData["current"]);

        Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await panel.CommitEditorAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "panel-commit"),
                new DiscordComponentInput("panel-commit", Walkthrough.TokenOf(review)),
                token));

        Assert.AreEqual(
            1L,
            walk.Scalar(
                """
                SELECT COUNT(*) FROM presentation_profile_versions WHERE status = 'PUBLISHED';
                """));
    }

    [TestMethod]
    public async Task TheManagementPanelProvisionsTheDepositInsuranceFund()
    {
        await using Walkthrough walk = Walkthrough.Create();
        CancellationToken token = TestContext.CancellationTokenSource.Token;

        ManageEndpoints manage = walk.Endpoint<ManageEndpoints>();
        ManagePanelEndpoints panel = walk.Endpoint<ManagePanelEndpoints>();

        Walkthrough.Deliver(DiscordInteractionKind.SlashCommand, await manage.CreateCurrencyAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "/manage currency-create"),
            walk.IssuanceBookId, "ヌメラ", "NMR", "N", 2, Genesis, token));

        string session = await PanelSessionAsync(
            walk, panel, "deposit-insurance", "insurance-fund", token);

        DiscordEndpointResponse review = Walkthrough.Deliver(
            DiscordInteractionKind.ModalSubmit,
            await panel.SubmitInsuranceFundAsync(
                walk.Context(
                    Operator, AuthorizationLevel.GuildOperator, "panel-insurance-fund", session),
                new PanelInsuranceFundForm { Confirmation = "CREATE" },
                token));

        Assert.AreEqual("なし", review.ViewData["current"]);
        Assert.AreEqual(0L, walk.Scalar("SELECT COUNT(*) FROM deposit_insurance_funds;"));

        Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await panel.CommitEditorAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "panel-commit"),
                new DiscordComponentInput("panel-commit", Walkthrough.TokenOf(review)),
                token));

        Assert.AreEqual(1L, walk.Scalar("SELECT COUNT(*) FROM deposit_insurance_funds;"));

        Assert.AreEqual(
            4L,
            walk.Scalar(
                """
                SELECT COUNT(*) FROM ledger_accounts WHERE account_kind IN (
                    'CENTRAL_BANK_SETTLEMENT_LIABILITY', 'CENTRAL_BANK_RESERVE_ASSET',
                    'FEE_REVENUE', 'RESOLUTION_LOSS_EXPENSE')
                  AND account_code LIKE '%510-%';
                """));

        Assert.AreEqual(
            1L,
            walk.Scalar(
                "SELECT COUNT(*) FROM parties WHERE display_name = 'DEPOSIT_INSURANCE_FUND';"));

        Assert.AreEqual(
            1L,
            walk.Scalar(
                """
                SELECT COUNT(*) FROM accounting_periods AS p
                JOIN deposit_insurance_funds AS f ON f.accounting_book_id = p.accounting_book_id;
                """));
    }

    private async Task<string> PanelSessionAsync(
        Walkthrough walk,
        ManagePanelEndpoints panel,
        string category,
        string action,
        CancellationToken token)
    {
        DiscordEndpointResponse opened = Walkthrough.Deliver(
            DiscordInteractionKind.SlashCommand,
            await panel.ShowAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "/manage panel"), token));

        string session = Walkthrough.SelectTokenOf(opened);

        Walkthrough.Deliver(
            DiscordInteractionKind.SelectMenu,
            await panel.SelectCategoryAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "panel-category"),
                new DiscordComponentInput("panel-category", session, [category]),
                token));

        DiscordEndpointResponse editor = Walkthrough.Deliver(
            DiscordInteractionKind.SelectMenu,
            await panel.SelectActionAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "panel-action"),
                new DiscordComponentInput("panel-action", session, [action]),
                token));

        Assert.AreEqual(ViewKeys.ManagePanelEditor, editor.ViewKey);

        DiscordEndpointResponse modal = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await panel.OpenEditorAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "panel-edit"),
                new DiscordComponentInput("panel-edit", session),
                token));

        return Walkthrough.ModalTokenOf(modal);
    }

    [TestMethod]
    public async Task TheCanonicalRouteCarriesAFreshEconomyToAFundedDepositAccount()
    {
        await using Walkthrough walk = Walkthrough.Create();
        CancellationToken token = TestContext.CancellationTokenSource.Token;

        ManageEndpoints manage = walk.Endpoint<ManageEndpoints>();
        ManageBankEndpoints manageBank = walk.Endpoint<ManageBankEndpoints>();
        AccountEndpoints account = walk.Endpoint<AccountEndpoints>();
        BankEndpoints bank = walk.Endpoint<BankEndpoints>();

        Walkthrough.Deliver(DiscordInteractionKind.SlashCommand, await manage.CreateCurrencyAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "/manage currency-create"),
            walk.IssuanceBookId,
            "ヌメラ",
            "NMR",
            "N",
            2,
            Genesis,
            token));

        DiscordEndpointResponse draft = Walkthrough.Deliver(
            DiscordInteractionKind.SlashCommand,
            await manageBank.BankCreateAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "/manage bank-create"),
            Institution,
            token));

        string createToken = Walkthrough.TokenOf(draft);

        DiscordEndpointResponse identityModal = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await manageBank.OpenBankCreateInputAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "bank-create-input"),
            new DiscordComponentInput("bank-create-input", createToken),
            token));

        DiscordEndpointResponse review = Walkthrough.Deliver(
            DiscordInteractionKind.ModalSubmit,
            await manageBank.SubmitBankCreateAsync(
            walk.Context(
                Operator,
                AuthorizationLevel.GuildOperator,
                "bank-create",
                Walkthrough.ModalTokenOf(identityModal)),
            new BankCreateForm
            {
                BankName = "ヌメラ銀行",
                BranchCode = "001",
                BranchName = "本店",
                ProductCode = "DEMAND01",
                ProductName = "普通預金",
            },
            token));

        DiscordEndpointResponse created = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await manageBank.CommitBankCreateAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "bank-create-commit"),
            new DiscordComponentInput("bank-create-commit", Walkthrough.TokenOf(review)),
            token));

        Assert.AreEqual("PENDING_ACTIVATION", walk.Text("SELECT status FROM banks;"));

        string capitalToken = Walkthrough.TokenOf(created);

        DiscordEndpointResponse capitalModal = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await manageBank.OpenBankCapitalInputAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "bank-capital-input"),
            new DiscordComponentInput("bank-capital-input", capitalToken),
            token));

        DiscordEndpointResponse capitalReview = Walkthrough.Deliver(
            DiscordInteractionKind.ModalSubmit,
            await manageBank.SubmitBankCapitalAsync(
            walk.Context(
                Operator,
                AuthorizationLevel.GuildOperator,
                "bank-capital",
                Walkthrough.ModalTokenOf(capitalModal)),
            new BankCapitalForm
            {
                Amount = (MinimumCapital / 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
                SourceInstitutionCode = string.Empty,
            },
            token));

        DiscordEndpointResponse shortfall = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await manageBank.CommitBankCapitalAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "bank-capital-commit"),
            new DiscordComponentInput("bank-capital-commit", Walkthrough.TokenOf(capitalReview)),
            token));

        Assert.AreEqual(ViewKeys.ManageBankCapitalShortfall, shortfall.ViewKey);
        Assert.AreEqual("PENDING_ACTIVATION", walk.Text("SELECT status FROM banks;"));

        DiscordEndpointResponse remainderModal = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await manageBank.OpenBankCapitalInputAsync(
                walk.Context(Operator, AuthorizationLevel.GuildOperator, "bank-capital-input"),
                new DiscordComponentInput("bank-capital-input", Walkthrough.TokenOf(shortfall)),
                token));

        DiscordEndpointResponse remainderReview = Walkthrough.Deliver(
            DiscordInteractionKind.ModalSubmit,
            await manageBank.SubmitBankCapitalAsync(
                walk.Context(
                    Operator,
                    AuthorizationLevel.GuildOperator,
                    "bank-capital",
                    Walkthrough.ModalTokenOf(remainderModal)),
                new BankCapitalForm
                {
                    Amount = (MinimumCapital / 2).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    SourceInstitutionCode = string.Empty,
                },
                token));

        DiscordEndpointResponse contributed = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await manageBank.CommitBankCapitalAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "bank-capital-commit"),
            new DiscordComponentInput("bank-capital-commit", Walkthrough.TokenOf(remainderReview)),
            token));

        Assert.AreEqual(ViewKeys.ManageBankCapitalContributed, contributed.ViewKey);

        DiscordEndpointResponse activated = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await manageBank.ActivateBankAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "bank-activate"),
            new DiscordComponentInput("bank-activate", Walkthrough.TokenOf(contributed)),
            token));

        Walkthrough.Succeeded(activated);
        Assert.AreEqual("OPERATING", walk.Text("SELECT status FROM banks;"));
        Assert.AreEqual("ACTIVE", walk.Text("SELECT status FROM settlement_participations;"));

        Walkthrough.Deliver(DiscordInteractionKind.SlashCommand, await account.RegisterAsync(
            walk.Context(Depositor, AuthorizationLevel.Unregistered, "/account register"),
            "depositor",
            "預金者",
            token));

        Walkthrough.Deliver(DiscordInteractionKind.SlashCommand, await bank.OpenAsync(
            walk.Context(Depositor, AuthorizationLevel.Customer, "/bank open"),
            Institution,
            token));

        Assert.AreEqual(1L, walk.Scalar("SELECT COUNT(*) FROM deposit_accounts;"));
        Assert.AreEqual("ACTIVE", walk.Text("SELECT status FROM deposit_accounts;"));

        BankQueryEndpoints bankQueries = walk.Endpoint<BankQueryEndpoints>();

        DiscordEndpointResponse banks = Walkthrough.Deliver(
            DiscordInteractionKind.SlashCommand,
            await bankQueries.ListAsync(
            walk.Context(Depositor, AuthorizationLevel.Customer, "/bank list"), token));

        string detailToken = Walkthrough.SelectTokenOf(banks);

        DiscordEndpointResponse detail = Walkthrough.Deliver(
            DiscordInteractionKind.SelectMenu,
            await bankQueries.SelectBankDetailAsync(
            walk.Context(Depositor, AuthorizationLevel.Customer, "bank-detail"),
            new DiscordComponentInput("bank-detail", detailToken, [Institution]),
            token));

        DiscordEndpointResponse loanModal = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await bankQueries.OpenBankLoanInputAsync(
            walk.Context(Depositor, AuthorizationLevel.Customer, "bank-loan-input"),
            new DiscordComponentInput("bank-loan-input", Walkthrough.TokenOf(detail)),
            token));

        DiscordEndpointResponse loanReview = Walkthrough.Deliver(
            DiscordInteractionKind.ModalSubmit,
            await bankQueries.SubmitBankLoanAsync(
            walk.Context(
                Depositor,
                AuthorizationLevel.Customer,
                "bank-loan",
                Walkthrough.ModalTokenOf(loanModal)),
            new BankLoanForm { Principal = "500000", ProductCode = "DEMAND01" },
            token));

        DiscordEndpointResponse originated = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await bankQueries.CommitBankLoanAsync(
            walk.Context(Depositor, AuthorizationLevel.Customer, "bank-loan-commit"),
            new DiscordComponentInput("bank-loan-commit", Walkthrough.TokenOf(loanReview)),
            token));

        Walkthrough.Succeeded(originated);
        Assert.AreEqual(1L, walk.Scalar("SELECT COUNT(*) FROM loan_contracts;"));
        Assert.AreEqual("ACTIVE", walk.Text("SELECT status FROM loan_contracts;"));

        Walkthrough.Succeeded(await account.RegisterAsync(
            walk.Context(Beneficiary, AuthorizationLevel.Unregistered, "/account register"),
            "beneficiary",
            "受取人",
            token));

        Walkthrough.Succeeded(await bank.OpenAsync(
            walk.Context(Beneficiary, AuthorizationLevel.Customer, "/bank open"),
            Institution,
            token));

        DiscordEndpointResponse sources = Walkthrough.Deliver(
            DiscordInteractionKind.SlashCommand,
            await bank.TransferAsync(
            walk.Context(Depositor, AuthorizationLevel.Customer, "/bank transfer"), token));

        DiscordEndpointResponse chosen = Walkthrough.Deliver(
            DiscordInteractionKind.SelectMenu,
            await bank.SelectTransferSourceAsync(
            walk.Context(Depositor, AuthorizationLevel.Customer, "transfer-source"),
            new DiscordComponentInput(
                "transfer-source",
                Walkthrough.SelectTokenOf(sources),
                [sources.Body.Components.Select!.Options[0].Value]),
            token));

        DiscordEndpointResponse transferModal = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await bank.OpenTransferInputAsync(
            walk.Context(Depositor, AuthorizationLevel.Customer, "transfer-input"),
            new DiscordComponentInput("transfer-input", Walkthrough.TokenOf(chosen)),
            token));

        DiscordEndpointResponse transferReview = Walkthrough.Deliver(
            DiscordInteractionKind.ModalSubmit,
            await bank.SubmitTransferAsync(
            walk.Context(
                Depositor,
                AuthorizationLevel.Customer,
                "transfer",
                Walkthrough.ModalTokenOf(transferModal)),
            new TransferForm
            {
                BankCode = Institution,
                BranchCode = "001",
                AccountNumber = walk.BeneficiaryAccountNumber(),
                Amount = "120000",
                Memo = string.Empty,
            },
            token));

        DiscordEndpointResponse transferred = Walkthrough.Deliver(
            DiscordInteractionKind.Button,
            await bank.ExecuteTransferAsync(
            walk.Context(Depositor, AuthorizationLevel.Customer, "transfer-execute"),
            new DiscordComponentInput("transfer-execute", Walkthrough.TokenOf(transferReview)),
            token));

        Walkthrough.Succeeded(transferred);
        Assert.AreEqual(120_000L, walk.BalanceOf("beneficiary"));
        Assert.AreEqual(380_000L, walk.BalanceOf("depositor"));

        SuggestionEndpoints suggestions = walk.Endpoint<SuggestionEndpoints>();

        IReadOnlyList<DiscordAutocompleteOption> accounts =
            await suggestions.SuggestDepositAccountsAsync(
                new DiscordAutocompleteRequest(
                    Depositor, Guild, "/bank statement", "account", string.Empty),
                token);

        Assert.AreEqual(1, accounts.Count);
        StringAssert.Contains(accounts[0].Name, Institution);

        Walkthrough.Deliver(
            DiscordInteractionKind.SlashCommand,
            await bankQueries.StatementAsync(
                walk.Context(Depositor, AuthorizationLevel.Customer, "/bank statement"),
                accounts[0].Value,
                token));
    }

    public TestContext TestContext { get; set; } = null!;
}
