using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Numera.Discord.Abstractions;
using Numera.Discord.Endpoints;
using Numera.Host.Configuration;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Host.Tests;

[TestClass]
public sealed class EconomyWalkthroughTests
{
    private const ulong Operator = 700_000_000_000_000_001UL;
    private const ulong Depositor = 700_000_000_000_000_002UL;
    private const ulong Guild = 1_284_327_110_349_164_587UL;
    private const string Institution = "NUM0001";
    private const long MinimumCapital = 1_000_000L;
    private const long Genesis = 100_000_000L;

    private sealed class Walkthrough : IAsyncDisposable
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

        public static string TokenOf(DiscordEndpointResponse response)
        {
            Assert.AreNotEqual(DiscordResponseKind.Failure, response.Kind, Detail(response));
            Assert.AreEqual(1, response.Body.Components.Buttons.Count, response.ViewKey);

            string customId = response.Body.Components.Buttons[0].CustomId;
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
    public async Task TheCanonicalRouteCarriesAFreshEconomyToAFundedDepositAccount()
    {
        await using Walkthrough walk = Walkthrough.Create();
        CancellationToken token = TestContext.CancellationTokenSource.Token;

        ManageEndpoints manage = walk.Endpoint<ManageEndpoints>();
        ManageBankEndpoints manageBank = walk.Endpoint<ManageBankEndpoints>();
        AccountEndpoints account = walk.Endpoint<AccountEndpoints>();
        BankEndpoints bank = walk.Endpoint<BankEndpoints>();

        Walkthrough.Succeeded(await manage.CreateCurrencyAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "/manage currency-create"),
            walk.IssuanceBookId,
            "ヌメラ",
            "NMR",
            "N",
            2,
            Genesis,
            token));

        DiscordEndpointResponse draft = await manageBank.BankCreateAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "/manage bank-create"),
            Institution,
            token);

        string createToken = Walkthrough.TokenOf(draft);

        DiscordEndpointResponse identityModal = await manageBank.OpenBankCreateInputAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "bank-create-input"),
            new DiscordComponentInput("bank-create-input", createToken),
            token);

        DiscordEndpointResponse review = await manageBank.SubmitBankCreateAsync(
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
            token);

        DiscordEndpointResponse created = await manageBank.CommitBankCreateAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "bank-create-commit"),
            new DiscordComponentInput("bank-create-commit", Walkthrough.TokenOf(review)),
            token);

        Assert.AreEqual("PENDING_ACTIVATION", walk.Text("SELECT status FROM banks;"));

        string capitalToken = Walkthrough.TokenOf(created);

        DiscordEndpointResponse capitalModal = await manageBank.OpenBankCapitalInputAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "bank-capital-input"),
            new DiscordComponentInput("bank-capital-input", capitalToken),
            token);

        DiscordEndpointResponse capitalReview = await manageBank.SubmitBankCapitalAsync(
            walk.Context(
                Operator,
                AuthorizationLevel.GuildOperator,
                "bank-capital",
                Walkthrough.ModalTokenOf(capitalModal)),
            new BankCapitalForm
            {
                Amount = MinimumCapital.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SourceInstitutionCode = string.Empty,
            },
            token);

        DiscordEndpointResponse contributed = await manageBank.CommitBankCapitalAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "bank-capital-commit"),
            new DiscordComponentInput("bank-capital-commit", Walkthrough.TokenOf(capitalReview)),
            token);

        DiscordEndpointResponse activated = await manageBank.ActivateBankAsync(
            walk.Context(Operator, AuthorizationLevel.GuildOperator, "bank-activate"),
            new DiscordComponentInput("bank-activate", Walkthrough.TokenOf(contributed)),
            token);

        Walkthrough.Succeeded(activated);
        Assert.AreEqual("OPERATING", walk.Text("SELECT status FROM banks;"));
        Assert.AreEqual("ACTIVE", walk.Text("SELECT status FROM settlement_participations;"));

        Walkthrough.Succeeded(await account.RegisterAsync(
            walk.Context(Depositor, AuthorizationLevel.Unregistered, "/account register"),
            "depositor",
            "預金者",
            token));

        Walkthrough.Succeeded(await bank.OpenAsync(
            walk.Context(Depositor, AuthorizationLevel.Customer, "/bank open"),
            Institution,
            token));

        Assert.AreEqual(1L, walk.Scalar("SELECT COUNT(*) FROM deposit_accounts;"));
        Assert.AreEqual("ACTIVE", walk.Text("SELECT status FROM deposit_accounts;"));
    }

    public TestContext TestContext { get; set; } = null!;
}
