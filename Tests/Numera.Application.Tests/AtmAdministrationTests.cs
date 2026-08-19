using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Application.Tests;

[TestClass]
public sealed class AtmAdministrationTests
{
    private const ulong GuildId = 970UL;
    private const ulong OwnerDiscordUserId = 770_000_000_000_000_001UL;
    private const ulong OperatorDiscordUserId = 770_000_000_000_000_002UL;

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

        public CashAdministrationApplicationService Cash { get; private set; } = null!;

        public AtmAdministrationApplicationService Atm { get; private set; } = null!;

        public AtmInstallationAdministrationApplicationService Installations { get; private set; } = null!;

        public BankId Bank { get; } = BankId.FromValue(EntityIdValue.FromBits(5));

        public CurrencyId Currency { get; } = CurrencyId.FromValue(EntityIdValue.FromBits(2));

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-atm", Guid.NewGuid().ToString("n"));
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

            SqliteBankingWriteGateway gateway = new(new FinancialWriteCoordinator(harness.Coordinator));
            SequentialIdGenerator ids = new(9_000);
            FixedClock clock = new();

            harness.Cash = new CashAdministrationApplicationService(gateway, clock, ids);
            harness.Atm = new AtmAdministrationApplicationService(gateway, clock, ids);
            harness.Installations =
                new AtmInstallationAdministrationApplicationService(gateway, clock, ids);
            harness.WriteGateway = gateway;

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed() => Execute($"""
            INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
            VALUES({Blob(1)}, '{GuildId}', 'Asia/Tokyo', 'ACTIVE', 1);

            INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                base_money_supply_cap_minor, created_at, retired_at, version)
            VALUES({Blob(2)}, {Blob(1)}, 'ACTIVE', 0, NULL, 1, NULL, 1);

            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(3)}, 'BANK', '銀行主体', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES({Blob(4)}, {Blob(3)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

            INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                current_fee_schedule_version_id, created_at, version)
            VALUES({Blob(5)}, {Blob(1)}, {Blob(3)}, 'NUM0070', 'ヌメラ銀行', 'NORMAL', NULL,
                'OPERATING', {Blob(4)}, NULL, NULL, 1, 1);

            INSERT INTO cash_holders(cash_holder_id, currency_id, holder_type, owner_reference_id,
                created_at)
            VALUES({Blob(20)}, {Blob(2)}, 'BANK_VAULT', {Blob(5)}, 1);

            INSERT INTO bank_cash_vaults(bank_cash_vault_id, bank_id, currency_id, cash_holder_id,
                status, version)
            VALUES({Blob(21)}, {Blob(5)}, {Blob(2)}, {Blob(20)}, 'ACTIVE', 1);

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

        public SqliteBankingWriteGateway WriteGateway { get; private set; } = null!;

        public string ReadText(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar()?.ToString() ?? string.Empty;
        }

        public long Count(string table)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public long Scalar(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public void StockVault(CurrencyDenominationId denomination, long count) => Execute($"""
            INSERT INTO cash_positions(cash_holder_id, currency_denomination_id, on_hand_count,
                reserved_count, version)
            VALUES({Blob(20)}, x'{Convert.ToHexString(denomination.Value.ToByteArray())}', {count}, 0, 1);
            """);

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync().ConfigureAwait(false);
            using (SqliteConnection pooled = ConnectionFactory.OpenRuntimeConnection())
            {
                SqliteConnection.ClearPool(pooled);
            }

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static AuthorizationContext Owner() =>
        new(AuthorizationLevel.SystemOwner, OwnerDiscordUserId, GuildId);

    private static AuthorizationContext Customer() =>
        new(AuthorizationLevel.Customer, OperatorDiscordUserId, GuildId);

    private static Task<Result<CurrencyDenominationView>> CreateDenominationAsync(
        Harness harness,
        long valueMinor,
        bool dispense = true) =>
        harness.Cash.CreateDenominationAsync(
            new CreateCurrencyDenominationCommand(
                Owner(), harness.Currency, valueMinor, "NOTE", dispense, true),
            CancellationToken.None);

    private static Task<Result<AtmTerminalView>> CreateTerminalAsync(Harness harness) =>
        harness.Atm.CreateTerminalAsync(
            new CreateAtmTerminalCommand(Owner(), harness.Bank, GuildId.ToString(), null, "本店ATM"),
            CancellationToken.None);

    [TestMethod]
    public async Task AnUnconfirmedInstallationMessageBecomesBrokenWithoutReposting()
    {
        await using Harness harness = Harness.Create();
        Result<AtmTerminalView> terminal = await CreateTerminalAsync(harness);

        Assert.IsTrue((await harness.Installations.PublishAsync(
            new PublishAtmInstallationCommand(
                Owner(), terminal.Value.Id, 1234UL, 5678UL, EntityIdValue.FromBits(910)),
            CancellationToken.None)).IsSuccess);

        RecordingInstallationMessageGateway gateway = new()
        {
            State = AtmInstallationMessageState.Confirmed,
        };

        AtmInstallationRecoveryService recovery = new(harness.WriteGateway, gateway);

        AtmInstallationRecoveryReport confirmed = await recovery.ScanAsync(CancellationToken.None);

        Assert.AreEqual(1, confirmed.Examined);
        Assert.AreEqual(1, confirmed.Confirmed);
        Assert.AreEqual(0, confirmed.Broken);
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM atm_discord_installations;"));
        Assert.AreEqual(1, gateway.Calls.Count);
        Assert.IsTrue(gateway.Calls[0].StartsWith("1234:5678:", StringComparison.Ordinal));

        gateway.State = AtmInstallationMessageState.Missing;

        AtmInstallationRecoveryReport broken = await recovery.ScanAsync(CancellationToken.None);

        Assert.AreEqual(1, broken.Broken);
        Assert.AreEqual("BROKEN", harness.ReadText("SELECT status FROM atm_discord_installations;"));
        Assert.AreEqual(1L, harness.Count("atm_discord_installations"));

        AtmInstallationRecoveryReport quiet = await recovery.ScanAsync(CancellationToken.None);

        Assert.AreEqual(0, quiet.Examined);
    }

    [TestMethod]
    public async Task DenominationsMustKeepTheDivisibilityChain()
    {
        await using Harness harness = Harness.Create();

        Assert.IsTrue((await CreateDenominationAsync(harness, 10_000)).IsSuccess);
        Assert.IsTrue((await CreateDenominationAsync(harness, 1_000)).IsSuccess);

        Result<CurrencyDenominationView> broken = await CreateDenominationAsync(harness, 3_000);

        Assert.IsFalse(broken.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencyDenominationChainBroken, broken.Error!.Code);
        Assert.AreEqual(2L, harness.Count("currency_denominations"));
    }

    [TestMethod]
    public async Task AChainBreakingDenominationIsAllowedWhenDispenseIsDisabled()
    {
        await using Harness harness = Harness.Create();

        Assert.IsTrue((await CreateDenominationAsync(harness, 10_000)).IsSuccess);
        Assert.IsTrue((await CreateDenominationAsync(harness, 1_000)).IsSuccess);

        Result<CurrencyDenominationView> allowed =
            await CreateDenominationAsync(harness, 3_000, dispense: false);

        Assert.IsTrue(allowed.IsSuccess, allowed.Error?.Code);
        Assert.AreEqual(3L, harness.Count("currency_denominations"));
    }

    [TestMethod]
    public async Task ADuplicateDenominationValueIsRejected()
    {
        await using Harness harness = Harness.Create();

        Assert.IsTrue((await CreateDenominationAsync(harness, 1_000)).IsSuccess);

        Result<CurrencyDenominationView> duplicate = await CreateDenominationAsync(harness, 1_000);

        Assert.IsFalse(duplicate.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencyDenominationAlreadyExists, duplicate.Error!.Code);
    }

    [TestMethod]
    public async Task ACustomerCannotAdministerCash()
    {
        await using Harness harness = Harness.Create();

        Result<CurrencyDenominationView> denied = await harness.Cash.CreateDenominationAsync(
            new CreateCurrencyDenominationCommand(Customer(), harness.Currency, 500, "COIN", true, true),
            CancellationToken.None);

        Assert.IsFalse(denied.IsSuccess);
        Assert.AreEqual(ErrorCategory.Forbidden, denied.Error!.Category);
    }

    [TestMethod]
    public async Task ANewTerminalStartsOutOfService()
    {
        await using Harness harness = Harness.Create();

        Result<AtmTerminalView> terminal = await CreateTerminalAsync(harness);

        Assert.IsTrue(terminal.IsSuccess, terminal.Error?.Code);
        Assert.AreEqual(AtmTerminalStatus.OutOfService, terminal.Value.Status);
    }

    [TestMethod]
    public async Task ARetiredTerminalCannotReturnToService()
    {
        await using Harness harness = Harness.Create();
        Result<AtmTerminalView> terminal = await CreateTerminalAsync(harness);

        Assert.IsTrue((await harness.Atm.UpdateTerminalAsync(
            new UpdateAtmTerminalCommand(
                Owner(), terminal.Value.Id, "本店ATM", AtmTerminalStatus.Retired, true, true, true, true),
            CancellationToken.None)).IsSuccess);

        Result<AtmTerminalView> revived = await harness.Atm.UpdateTerminalAsync(
            new UpdateAtmTerminalCommand(
                Owner(), terminal.Value.Id, "本店ATM", AtmTerminalStatus.Operating, true, true, true, true),
            CancellationToken.None);

        Assert.IsFalse(revived.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AtmTerminalStateInvalid, revived.Error!.Code);
    }

    [TestMethod]
    public async Task ACassetteSlotCannotBeUsedTwice()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyDenominationView> denomination = await CreateDenominationAsync(harness, 1_000);
        Result<AtmTerminalView> terminal = await CreateTerminalAsync(harness);

        Assert.IsTrue((await harness.Atm.ConfigureCassetteAsync(
            new ConfigureAtmCashCassetteCommand(
                Owner(), terminal.Value.Id, denomination.Value.Id, "DISPENSE", 0, 200),
            CancellationToken.None)).IsSuccess);

        Result<AtmCashCassetteView> conflict = await harness.Atm.ConfigureCassetteAsync(
            new ConfigureAtmCashCassetteCommand(
                Owner(), terminal.Value.Id, denomination.Value.Id, "DISPENSE", 0, 200),
            CancellationToken.None);

        Assert.IsFalse(conflict.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AtmCassetteSlotOccupied, conflict.Error!.Code);
        Assert.AreEqual(1L, harness.Count("atm_cash_cassettes"));
    }

    [TestMethod]
    public async Task ATerminalCannotExceedEightCassettes()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyDenominationView> denomination = await CreateDenominationAsync(harness, 1_000);
        Result<AtmTerminalView> terminal = await CreateTerminalAsync(harness);

        for (int priority = 0; priority < 8; priority++)
        {
            Assert.IsTrue((await harness.Atm.ConfigureCassetteAsync(
                new ConfigureAtmCashCassetteCommand(
                    Owner(), terminal.Value.Id, denomination.Value.Id, "DISPENSE", priority, 200),
                CancellationToken.None)).IsSuccess);
        }

        Result<AtmCashCassetteView> overflow = await harness.Atm.ConfigureCassetteAsync(
            new ConfigureAtmCashCassetteCommand(
                Owner(), terminal.Value.Id, denomination.Value.Id, "DISPENSE", 7, 200),
            CancellationToken.None);

        Assert.IsFalse(overflow.IsSuccess);
        Assert.AreEqual(8L, harness.Count("atm_cash_cassettes"));
    }

    [TestMethod]
    public async Task ReplenishMovesCashFromTheVaultIntoTheCassette()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyDenominationView> denomination = await CreateDenominationAsync(harness, 1_000);
        Result<AtmTerminalView> terminal = await CreateTerminalAsync(harness);

        Result<AtmCashCassetteView> cassette = await harness.Atm.ConfigureCassetteAsync(
            new ConfigureAtmCashCassetteCommand(
                Owner(), terminal.Value.Id, denomination.Value.Id, "DISPENSE", 0, 200),
            CancellationToken.None);

        harness.StockVault(denomination.Value.Id, 100);

        Result replenished = await harness.Atm.ReplenishAsync(
            new ReplenishAtmCashCommand(Owner(), cassette.Value.Id, 40), CancellationToken.None);

        Assert.IsTrue(replenished.IsSuccess, replenished.Error?.Code);
        Assert.AreEqual(1L, harness.Count("cash_movements"));
        Assert.AreEqual(
            60L,
            harness.Scalar(
                "SELECT on_hand_count FROM cash_positions p JOIN cash_holders h " +
                "ON h.cash_holder_id = p.cash_holder_id WHERE h.holder_type = 'BANK_VAULT';"));
        Assert.AreEqual(
            40L,
            harness.Scalar(
                "SELECT on_hand_count FROM cash_positions p JOIN cash_holders h " +
                "ON h.cash_holder_id = p.cash_holder_id WHERE h.holder_type = 'ATM_CASSETTE';"));
    }

    [TestMethod]
    public async Task ReplenishBeyondTheCassetteCapacityIsRejected()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyDenominationView> denomination = await CreateDenominationAsync(harness, 1_000);
        Result<AtmTerminalView> terminal = await CreateTerminalAsync(harness);

        Result<AtmCashCassetteView> cassette = await harness.Atm.ConfigureCassetteAsync(
            new ConfigureAtmCashCassetteCommand(
                Owner(), terminal.Value.Id, denomination.Value.Id, "DISPENSE", 0, 30),
            CancellationToken.None);

        harness.StockVault(denomination.Value.Id, 100);

        Result replenished = await harness.Atm.ReplenishAsync(
            new ReplenishAtmCashCommand(Owner(), cassette.Value.Id, 40), CancellationToken.None);

        Assert.IsFalse(replenished.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AtmCassetteCapacityExceeded, replenished.Error!.Code);
        Assert.AreEqual(0L, harness.Count("cash_movements"));
    }

    [TestMethod]
    public async Task ReplenishWithoutVaultStockIsRejected()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyDenominationView> denomination = await CreateDenominationAsync(harness, 1_000);
        Result<AtmTerminalView> terminal = await CreateTerminalAsync(harness);

        Result<AtmCashCassetteView> cassette = await harness.Atm.ConfigureCassetteAsync(
            new ConfigureAtmCashCassetteCommand(
                Owner(), terminal.Value.Id, denomination.Value.Id, "DISPENSE", 0, 200),
            CancellationToken.None);

        Result replenished = await harness.Atm.ReplenishAsync(
            new ReplenishAtmCashCommand(Owner(), cassette.Value.Id, 1), CancellationToken.None);

        Assert.IsFalse(replenished.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CashVaultInsufficient, replenished.Error!.Code);
    }

    [TestMethod]
    public async Task ReserveConversionIsRejectedWhileTheCashRailIsUnavailable()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyDenominationView> denomination = await CreateDenominationAsync(harness, 1_000);

        Result<CashConversionView> converted = await harness.Cash.ConvertReserveToCashAsync(
            new ConvertReserveToCashCommand(
                Owner(), harness.Bank, denomination.Value.Id, 10, "convert-1"),
            CancellationToken.None);

        Assert.IsFalse(converted.IsSuccess);
        Assert.AreEqual(
            BankingErrorCodes.SettlementParticipationUnavailable, converted.Error!.Code);
        Assert.AreEqual(0L, harness.Count("cash_movements"));
    }

    [TestMethod]
    public async Task AnInstallationIsPublishedAndRemovedOnce()
    {
        await using Harness harness = Harness.Create();
        Result<AtmTerminalView> terminal = await CreateTerminalAsync(harness);

        Result<AtmDiscordInstallationView> published = await harness.Installations.PublishAsync(
            new PublishAtmInstallationCommand(
                Owner(), terminal.Value.Id, 1234UL, 5678UL, EntityIdValue.FromBits(901)),
            CancellationToken.None);

        Assert.IsTrue(published.IsSuccess, published.Error?.Code);
        Assert.AreEqual(AtmDiscordInstallationStatus.Active, published.Value.Status);

        Result<AtmDiscordInstallationView> duplicate = await harness.Installations.PublishAsync(
            new PublishAtmInstallationCommand(
                Owner(), terminal.Value.Id, 1234UL, 9999UL, EntityIdValue.FromBits(902)),
            CancellationToken.None);

        Assert.IsFalse(duplicate.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AtmInstallationStateInvalid, duplicate.Error!.Code);

        Result removed = await harness.Installations.RemoveAsync(
            new RemoveAtmInstallationCommand(Owner(), published.Value.Id), CancellationToken.None);

        Assert.IsTrue(removed.IsSuccess, removed.Error?.Code);
        Assert.AreEqual(1L, harness.Count("atm_discord_installations WHERE status = 'REMOVED'"));
    }

    [TestMethod]
    public void TheGreedyPlannerUsesTheFewestPieces()
    {
        CashDispenseAllocation[] available =
        [
            new CashDispenseAllocation(10_000, 5),
            new CashDispenseAllocation(1_000, 20),
        ];

        Assert.IsTrue(CashDispensePlanner.TryPlan(
            available, 23_000, out IReadOnlyList<CashDispenseAllocation> plan));
        Assert.AreEqual(2, plan.Count);
        Assert.AreEqual(2L, plan[0].Count);
        Assert.AreEqual(3L, plan[1].Count);
    }

    [TestMethod]
    public void ThePlannerRefusesAmountsItCannotComposeExactly()
    {
        CashDispenseAllocation[] available = [new CashDispenseAllocation(10_000, 5)];

        Assert.IsFalse(CashDispensePlanner.TryPlan(
            available, 15_000, out IReadOnlyList<CashDispenseAllocation> _));
    }

    [TestMethod]
    public void ThePlannerRefusesABrokenDivisibilityChain()
    {
        CashDispenseAllocation[] available =
        [
            new CashDispenseAllocation(3_000, 5),
            new CashDispenseAllocation(2_000, 5),
        ];

        Assert.IsFalse(CashDispensePlanner.TryPlan(
            available, 6_000, out IReadOnlyList<CashDispenseAllocation> _));
    }
}
