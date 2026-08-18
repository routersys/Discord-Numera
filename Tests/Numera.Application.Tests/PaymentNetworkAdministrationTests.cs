using Microsoft.Data.Sqlite;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Application.Tests;

[TestClass]
public sealed class PaymentNetworkAdministrationTests
{
    private const ulong OperatorDiscordUserId = 810_000_000_000_000_001UL;
    private const ulong OwnerDiscordUserId = 810_000_000_000_000_002UL;
    private const ulong OutsiderDiscordUserId = 810_000_000_000_000_003UL;
    private const ulong GuildId = 900UL;
    private const ulong OtherGuildId = 901UL;

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string root;

        private Harness(string root, SqliteDatabaseOptions options)
        {
            this.root = root;
            ConnectionFactory = new SqliteConnectionFactory(options);
        }

        public SqliteConnectionFactory ConnectionFactory { get; }

        public SqliteWriteCoordinator Coordinator { get; private set; } = null!;

        public PaymentNetworkAdministrationApplicationService Networks { get; private set; } = null!;

        public EconomyScopeId Scope { get; } = EconomyScopeId.FromValue(EntityIdValue.FromBits(1));

        public PartyId Operator { get; } = PartyId.FromValue(EntityIdValue.FromBits(3));

        public AccountingBookId Book { get; } = AccountingBookId.FromValue(EntityIdValue.FromBits(4));

        public LedgerAccountId LiquidAsset { get; } = LedgerAccountId.FromValue(EntityIdValue.FromBits(5));

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-network", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            new SqliteDatabaseInitializer(
                options, harness.ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_776_000_000_000);
            harness.Seed();

            harness.Coordinator = new SqliteWriteCoordinator(
                harness.ConnectionFactory, new SqliteRetryPolicy(3, 1, static () => 0));
            harness.Coordinator.Start();

            harness.Networks = new PaymentNetworkAdministrationApplicationService(
                new SqliteBankingWriteGateway(new FinancialWriteCoordinator(harness.Coordinator)),
                new FixedClock(),
                new SequentialIdGenerator(7_000));

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed() => Execute($"""
            INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
            VALUES({Blob(1)}, '{GuildId}', 'Asia/Tokyo', 'ACTIVE', 1);

            INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                base_money_supply_cap_minor, created_at, retired_at, version)
            VALUES({Blob(2)}, {Blob(1)}, 'ACTIVE', 2, NULL, 1, NULL, 1);

            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(3)}, 'SYSTEM', '清算機関', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES({Blob(4)}, {Blob(3)}, 'SYSTEM', 'OPEN', 1, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES({Blob(5)}, {Blob(4)}, NULL, '1000', 'CASH_ASSET', 'ASSET', 'DEBIT', {Blob(2)}, 1,
                NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO system_owner_identities(discord_user_id, created_at)
            VALUES('{OwnerDiscordUserId}', 1);
            """);

        public void Execute(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public string? ReadText(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;

            return command.ExecuteScalar() as string;
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync().ConfigureAwait(false);
            SqliteConnection.ClearPool(ConnectionFactory.OpenRuntimeConnection());

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static AuthorizationContext GuildOperator(ulong guildId = GuildId) =>
        new(AuthorizationLevel.GuildOperator, OperatorDiscordUserId, guildId);

    private static AuthorizationContext SystemOwner() =>
        new(AuthorizationLevel.SystemOwner, OwnerDiscordUserId, GuildId);

    private static AuthorizationContext Customer() =>
        new(AuthorizationLevel.Customer, OutsiderDiscordUserId, GuildId);

    private static PaymentNetworkPolicyInput ClearingPolicy(
        BeneficiaryPostingPolicy posting = BeneficiaryPostingPolicy.AfterFinalSettlement) =>
        new(SettlementMode.Clearing, posting, null, 3600, 10000, 1_000_000);

    private static Task<Result<PaymentNetworkDraftView>> DraftAsync(
        Harness harness,
        AuthorizationContext actor,
        string code = "ZENGIN") =>
        harness.Networks.StartNetworkDraftAsync(
            new StartPaymentNetworkDraftCommand(
                actor, code, harness.Operator, harness.Book, harness.LiquidAsset),
            CancellationToken.None);

    [TestMethod]
    public async Task GuildOperatorStartsADraftNetwork()
    {
        await using Harness harness = Harness.Create();

        Result<PaymentNetworkDraftView> result = await DraftAsync(harness, GuildOperator());

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentNetworkStatus.Draft, result.Value.Status);
        Assert.AreEqual("DRAFT", harness.ReadText("SELECT status FROM payment_networks;"));
    }

    [TestMethod]
    public async Task SystemOwnerFromAnotherGuildIsStillAuthorised()
    {
        await using Harness harness = Harness.Create();

        Result<PaymentNetworkDraftView> result = await harness.Networks.StartNetworkDraftAsync(
            new StartPaymentNetworkDraftCommand(
                new AuthorizationContext(AuthorizationLevel.SystemOwner, OwnerDiscordUserId, OtherGuildId),
                "ZENGIN",
                harness.Operator,
                harness.Book,
                harness.LiquidAsset,
                harness.Scope),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task GuildOperatorOfAnotherGuildIsRejected()
    {
        await using Harness harness = Harness.Create();

        Result<PaymentNetworkDraftView> result = await harness.Networks.StartNetworkDraftAsync(
            new StartPaymentNetworkDraftCommand(
                GuildOperator(OtherGuildId),
                "ZENGIN",
                harness.Operator,
                harness.Book,
                harness.LiquidAsset,
                harness.Scope),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Forbidden, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.ManagementAuthorityMissing, result.Error.Code);
    }

    [TestMethod]
    public async Task AGuildWithoutAnEconomyIsNotFound()
    {
        await using Harness harness = Harness.Create();

        Result<PaymentNetworkDraftView> result = await DraftAsync(harness, GuildOperator(OtherGuildId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.GuildEconomyNotFound, result.Error.Code);
    }

    [TestMethod]
    public async Task CustomerCannotStartADraftNetwork()
    {
        await using Harness harness = Harness.Create();

        Result<PaymentNetworkDraftView> result = await DraftAsync(harness, Customer());

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Forbidden, result.Error!.Category);
    }

    [TestMethod]
    public async Task ClaimedSystemOwnerLevelIsVerifiedAgainstTheDatabase()
    {
        await using Harness harness = Harness.Create();

        Result<PaymentNetworkDraftView> result = await harness.Networks.StartNetworkDraftAsync(
            new StartPaymentNetworkDraftCommand(
                new AuthorizationContext(AuthorizationLevel.SystemOwner, OutsiderDiscordUserId, OtherGuildId),
                "ZENGIN",
                harness.Operator,
                harness.Book,
                harness.LiquidAsset,
                harness.Scope),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.ManagementAuthorityMissing, result.Error!.Code);
    }

    [TestMethod]
    public async Task DuplicateNetworkCodeIsRejected()
    {
        await using Harness harness = Harness.Create();
        await DraftAsync(harness, SystemOwner());

        Result<PaymentNetworkDraftView> second = await DraftAsync(harness, SystemOwner());

        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.PaymentNetworkAlreadyExists, second.Error!.Code);
    }

    [TestMethod]
    public async Task FirstPublishActivatesTheNetwork()
    {
        await using Harness harness = Harness.Create();
        Result<PaymentNetworkDraftView> draft = await DraftAsync(harness, SystemOwner());

        Result<PaymentNetworkView> published = await harness.Networks.PublishNetworkAsync(
            new PublishPaymentNetworkCommand(SystemOwner(), draft.Value.Id, ClearingPolicy()),
            CancellationToken.None);

        Assert.IsTrue(published.IsSuccess);
        Assert.AreEqual(PaymentNetworkStatus.Active, published.Value.Status);
        Assert.IsNotNull(published.Value.CurrentPolicyVersionId);
    }

    [TestMethod]
    public async Task SecondPublishOnADraftIsRejected()
    {
        await using Harness harness = Harness.Create();
        Result<PaymentNetworkDraftView> draft = await DraftAsync(harness, SystemOwner());
        await harness.Networks.PublishNetworkAsync(
            new PublishPaymentNetworkCommand(SystemOwner(), draft.Value.Id, ClearingPolicy()),
            CancellationToken.None);

        Result<PaymentNetworkView> again = await harness.Networks.PublishNetworkAsync(
            new PublishPaymentNetworkCommand(SystemOwner(), draft.Value.Id, ClearingPolicy()),
            CancellationToken.None);

        Assert.IsFalse(again.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.PaymentNetworkNotDraft, again.Error!.Code);
    }

    [TestMethod]
    public async Task SecondActiveNetworkInTheSameScopeIsRejected()
    {
        await using Harness harness = Harness.Create();
        Result<PaymentNetworkDraftView> first = await DraftAsync(harness, SystemOwner());
        await harness.Networks.PublishNetworkAsync(
            new PublishPaymentNetworkCommand(SystemOwner(), first.Value.Id, ClearingPolicy()),
            CancellationToken.None);

        Result<PaymentNetworkDraftView> second = await DraftAsync(harness, SystemOwner(), "RETAIL");

        Result<PaymentNetworkView> published = await harness.Networks.PublishNetworkAsync(
            new PublishPaymentNetworkCommand(SystemOwner(), second.Value.Id, ClearingPolicy()),
            CancellationToken.None);

        Assert.IsFalse(published.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.PaymentNetworkAlreadyActive, published.Error!.Code);
    }

    [TestMethod]
    public async Task PolicyPublishKeepsTheNetworkActiveAndBumpsTheVersion()
    {
        await using Harness harness = Harness.Create();
        Result<PaymentNetworkDraftView> draft = await DraftAsync(harness, SystemOwner());
        await harness.Networks.PublishNetworkAsync(
            new PublishPaymentNetworkCommand(SystemOwner(), draft.Value.Id, ClearingPolicy()),
            CancellationToken.None);

        Result<PaymentNetworkPolicyVersionView> next = await harness.Networks.PublishPolicyAsync(
            new PublishPaymentNetworkPolicyCommand(SystemOwner(), draft.Value.Id, ClearingPolicy()),
            CancellationToken.None);

        Assert.IsTrue(next.IsSuccess);
        Assert.AreEqual(2L, next.Value.Version);
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM payment_networks;"));
        Assert.AreEqual("2", harness.ReadText(
            "SELECT CAST(count(*) AS TEXT) FROM payment_network_policy_versions;"));
    }

    [TestMethod]
    public async Task PolicyPublishOnADraftNetworkIsRejected()
    {
        await using Harness harness = Harness.Create();
        Result<PaymentNetworkDraftView> draft = await DraftAsync(harness, SystemOwner());

        Result<PaymentNetworkPolicyVersionView> published = await harness.Networks.PublishPolicyAsync(
            new PublishPaymentNetworkPolicyCommand(SystemOwner(), draft.Value.Id, ClearingPolicy()),
            CancellationToken.None);

        Assert.IsFalse(published.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.PaymentNetworkNotOperating, published.Error!.Code);
    }

    [TestMethod]
    public async Task InconsistentPolicyIsRejectedAsValidation()
    {
        await using Harness harness = Harness.Create();
        Result<PaymentNetworkDraftView> draft = await DraftAsync(harness, SystemOwner());

        Result<PaymentNetworkView> published = await harness.Networks.PublishNetworkAsync(
            new PublishPaymentNetworkCommand(
                SystemOwner(),
                draft.Value.Id,
                new PaymentNetworkPolicyInput(
                    SettlementMode.Rtgs,
                    BeneficiaryPostingPolicy.GuaranteedPreCredit,
                    null,
                    null,
                    10000,
                    0)),
            CancellationToken.None);

        Assert.IsFalse(published.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.PaymentNetworkPolicyInvalid, published.Error!.Code);
    }

    [TestMethod]
    public async Task SuspendAndResumeMoveTheNetworkBetweenStates()
    {
        await using Harness harness = Harness.Create();
        Result<PaymentNetworkDraftView> draft = await DraftAsync(harness, SystemOwner());
        await harness.Networks.PublishNetworkAsync(
            new PublishPaymentNetworkCommand(SystemOwner(), draft.Value.Id, ClearingPolicy()),
            CancellationToken.None);

        Result suspended = await harness.Networks.SuspendNetworkAsync(
            new SuspendPaymentNetworkCommand(SystemOwner(), draft.Value.Id), CancellationToken.None);

        Assert.IsTrue(suspended.IsSuccess);
        Assert.AreEqual("SUSPENDED", harness.ReadText("SELECT status FROM payment_networks;"));

        Result resumed = await harness.Networks.ResumeNetworkAsync(
            new ResumePaymentNetworkCommand(SystemOwner(), draft.Value.Id), CancellationToken.None);

        Assert.IsTrue(resumed.IsSuccess);
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM payment_networks;"));
    }

    [TestMethod]
    public async Task SuspendingADraftNetworkIsRejected()
    {
        await using Harness harness = Harness.Create();
        Result<PaymentNetworkDraftView> draft = await DraftAsync(harness, SystemOwner());

        Result suspended = await harness.Networks.SuspendNetworkAsync(
            new SuspendPaymentNetworkCommand(SystemOwner(), draft.Value.Id), CancellationToken.None);

        Assert.IsFalse(suspended.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.PaymentNetworkNotOperating, suspended.Error!.Code);
    }

    [TestMethod]
    public async Task UnknownNetworkIsNotFound()
    {
        await using Harness harness = Harness.Create();

        Result suspended = await harness.Networks.SuspendNetworkAsync(
            new SuspendPaymentNetworkCommand(
                SystemOwner(), PaymentNetworkId.FromValue(EntityIdValue.FromBits(999))),
            CancellationToken.None);

        Assert.IsFalse(suspended.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.PaymentNetworkNotFound, suspended.Error!.Code);
    }
}
