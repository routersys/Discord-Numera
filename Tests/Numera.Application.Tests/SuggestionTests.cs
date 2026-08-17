using Microsoft.Data.Sqlite;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Application.Tests;

[TestClass]
public sealed class SuggestionTests
{
    private const string DisplayPattern = "{symbol}{amount}";

    private const ulong OperatorUser = 555_000_000_000_000_001UL;
    private const ulong OtherUser = 555_000_000_000_000_002UL;

    private sealed class Harness : IDisposable
    {
        private readonly string root;

        private Harness(string root, SqliteDatabaseOptions options)
        {
            this.root = root;
            ConnectionFactory = new SqliteConnectionFactory(options);
            Service = new SuggestionApplicationService(new SqliteBankingReadGateway(ConnectionFactory));
        }

        public SqliteConnectionFactory ConnectionFactory { get; }

        public SuggestionApplicationService Service { get; }

        public EconomyScopeId Scope { get; } = EconomyScopeId.FromValue(EntityIdValue.FromBits(1));

        public EconomyScopeId OtherScope { get; } = EconomyScopeId.FromValue(EntityIdValue.FromBits(2));

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-suggest", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            new SqliteDatabaseInitializer(
                options, harness.ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_776_000_000_000);
            harness.Seed();
            return harness;
        }

        public void Execute(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed()
        {
            Execute($"""
                INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
                VALUES({Blob(1)}, '900', 'Asia/Tokyo', 'ACTIVE', 1);

                INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
                VALUES({Blob(2)}, '901', 'Asia/Tokyo', 'ACTIVE', 1);

                INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                    base_money_supply_cap_minor, created_at, retired_at, version)
                VALUES({Blob(3)}, {Blob(1)}, 'ACTIVE', 2, NULL, 1, NULL, 1);

                INSERT INTO currency_metadata_versions(currency_metadata_version_id, currency_id, name, code,
                    symbol, display_pattern, effective_from, effective_to, version)
                VALUES({Blob(4)}, {Blob(3)}, 'ヌメラ円', 'NMR', 'N', '{DisplayPattern}', 1, NULL, 1);
                """);

            AddBank(10, "NUM0001", "みどり銀行", "OPERATING", 1);
            AddBank(20, "NUM0002", "あおぞら銀行", "PENDING_ACTIVATION", 1);
            AddBank(30, "NUM0003", "さくら銀行", "SETTLEMENT_SUSPENDED", 1);
            AddBank(40, "NUM0004", "解散銀行", "CLOSED", 1);
            AddBank(50, "NUM0005", "別Guild銀行", "OPERATING", 2);

            Execute($"""
                INSERT INTO bank_operator_grants(bank_operator_grant_id, bank_id, discord_user_id, status,
                    granted_by_discord_user_id, granted_at, revoked_at, version)
                VALUES({Blob(60)}, {Blob(10)}, '{OperatorUser}', 'ACTIVE', '{OtherUser}', 1, NULL, 1);

                INSERT INTO bank_operator_grants(bank_operator_grant_id, bank_id, discord_user_id, status,
                    granted_by_discord_user_id, granted_at, revoked_at, version)
                VALUES({Blob(61)}, {Blob(30)}, '{OperatorUser}', 'REVOKED', '{OtherUser}', 1, 2, 1);
                """);
        }

        public void AddBank(int seed, string code, string name, string status, int scopeSeed)
        {
            Execute($"""
                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(seed + 1)}, 'BANK', '{name}主体', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status, created_at, version)
                VALUES({Blob(seed + 2)}, {Blob(seed + 1)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

                INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                    resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                    current_fee_schedule_version_id, created_at, version)
                VALUES({Blob(seed)}, {Blob(scopeSeed)}, {Blob(seed + 1)}, '{code}', '{name}', 'NORMAL',
                    NULL, '{status}', {Blob(seed + 2)}, NULL, NULL, 1, 1);
                """);
        }

        public Task<Result<IReadOnlyList<BankSuggestion>>> SuggestBanksAsync(
            AuthorizationLevel level,
            ulong discordUserId = OtherUser) =>
            Service.SuggestBanksAsync(
                new SuggestBanksQuery(Scope, new AuthorizationContext(level, discordUserId, 900), string.Empty),
                CancellationToken.None);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();

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
    public async Task CustomerSeesOnlyUsableBanks()
    {
        using Harness harness = Harness.Create();

        Result<IReadOnlyList<BankSuggestion>> result = await harness.SuggestBanksAsync(AuthorizationLevel.Customer);

        CollectionAssert.AreEqual(
            new[] { "NUM0003", "NUM0001" },
            result.Value.Select(static suggestion => suggestion.InstitutionCode).ToArray());
    }

    [TestMethod]
    public async Task GuildOperatorSeesPendingAndRestrictedBanks()
    {
        using Harness harness = Harness.Create();

        Result<IReadOnlyList<BankSuggestion>> result =
            await harness.SuggestBanksAsync(AuthorizationLevel.GuildOperator);

        CollectionAssert.AreEquivalent(
            new[] { "NUM0001", "NUM0002", "NUM0003" },
            result.Value.Select(static suggestion => suggestion.InstitutionCode).ToArray());
    }

    [TestMethod]
    public async Task ClosedBankIsNeverSuggested()
    {
        using Harness harness = Harness.Create();

        foreach (AuthorizationLevel level in Enum.GetValues<AuthorizationLevel>())
        {
            Result<IReadOnlyList<BankSuggestion>> result = await harness.SuggestBanksAsync(level, OperatorUser);

            Assert.IsFalse(
                result.Value.Any(static suggestion => suggestion.InstitutionCode == "NUM0004"),
                $"{level} へ解散済み銀行が提示されました。");
        }
    }

    [TestMethod]
    public async Task BankOperatorSeesOnlyGrantedBanks()
    {
        using Harness harness = Harness.Create();

        Result<IReadOnlyList<BankSuggestion>> result =
            await harness.SuggestBanksAsync(AuthorizationLevel.BankOperator, OperatorUser);

        CollectionAssert.AreEqual(
            new[] { "NUM0001" },
            result.Value.Select(static suggestion => suggestion.InstitutionCode).ToArray());
    }

    [TestMethod]
    public async Task RevokedGrantDoesNotSuggestTheBank()
    {
        using Harness harness = Harness.Create();

        Result<IReadOnlyList<BankSuggestion>> result =
            await harness.SuggestBanksAsync(AuthorizationLevel.BankOperator, OperatorUser);

        Assert.IsFalse(result.Value.Any(static suggestion => suggestion.InstitutionCode == "NUM0003"));
    }

    [TestMethod]
    public async Task BankOperatorWithoutGrantSeesNothing()
    {
        using Harness harness = Harness.Create();

        Result<IReadOnlyList<BankSuggestion>> result =
            await harness.SuggestBanksAsync(AuthorizationLevel.BankOperator, OtherUser);

        Assert.AreEqual(0, result.Value.Count);
    }

    [TestMethod]
    public async Task UnregisteredUserSeesNothing()
    {
        using Harness harness = Harness.Create();

        Result<IReadOnlyList<BankSuggestion>> result =
            await harness.SuggestBanksAsync(AuthorizationLevel.Unregistered);

        Assert.AreEqual(0, result.Value.Count);
    }

    [TestMethod]
    public async Task BanksFromAnotherGuildAreNotSuggested()
    {
        using Harness harness = Harness.Create();

        Result<IReadOnlyList<BankSuggestion>> result =
            await harness.SuggestBanksAsync(AuthorizationLevel.GuildOperator);

        Assert.IsFalse(result.Value.Any(static suggestion => suggestion.InstitutionCode == "NUM0005"));
    }

    [TestMethod]
    public async Task NewlyEstablishedBankIsSuggestedWithoutCommandResynchronisation()
    {
        using Harness harness = Harness.Create();

        Result<IReadOnlyList<BankSuggestion>> before =
            await harness.SuggestBanksAsync(AuthorizationLevel.Customer);
        Assert.IsFalse(before.Value.Any(static suggestion => suggestion.InstitutionCode == "NUM0009"));

        harness.AddBank(90, "NUM0009", "新設銀行", "OPERATING", 1);

        Result<IReadOnlyList<BankSuggestion>> after =
            await harness.SuggestBanksAsync(AuthorizationLevel.Customer);

        Assert.IsTrue(after.Value.Any(static suggestion => suggestion.InstitutionCode == "NUM0009"));
    }

    [TestMethod]
    public async Task CurrentCurrencyIsSuggested()
    {
        using Harness harness = Harness.Create();

        Result<IReadOnlyList<CurrencySuggestion>> result = await harness.Service.SuggestCurrenciesAsync(
            new SuggestCurrenciesQuery(
                harness.Scope,
                new AuthorizationContext(AuthorizationLevel.Customer, OtherUser, 900),
                string.Empty),
            CancellationToken.None);

        Assert.AreEqual(1, result.Value.Count);
        Assert.AreEqual("NMR", result.Value[0].Code);
        Assert.AreEqual("ヌメラ円", result.Value[0].Name);
    }

    [TestMethod]
    public async Task UnregisteredUserSeesNoCurrency()
    {
        using Harness harness = Harness.Create();

        Result<IReadOnlyList<CurrencySuggestion>> result = await harness.Service.SuggestCurrenciesAsync(
            new SuggestCurrenciesQuery(
                harness.Scope,
                new AuthorizationContext(AuthorizationLevel.Unregistered, OtherUser, 900),
                string.Empty),
            CancellationToken.None);

        Assert.AreEqual(0, result.Value.Count);
    }

    [TestMethod]
    public void SelectableStatusesNeverIncludeClosedBanks()
    {
        foreach (AuthorizationLevel level in Enum.GetValues<AuthorizationLevel>())
        {
            CollectionAssert.DoesNotContain(
                SuggestionApplicationService.SelectableStatuses(level).ToArray(),
                BankStatus.Closed);
        }
    }

    [TestMethod]
    public void CustomerCannotSeeAdministrativeStatuses()
    {
        IReadOnlyList<BankStatus> customer = SuggestionApplicationService.SelectableStatuses(AuthorizationLevel.Customer);

        CollectionAssert.DoesNotContain(customer.ToArray(), BankStatus.PendingActivation);
        CollectionAssert.DoesNotContain(customer.ToArray(), BankStatus.Resolution);
        CollectionAssert.DoesNotContain(customer.ToArray(), BankStatus.Closing);
    }
}
