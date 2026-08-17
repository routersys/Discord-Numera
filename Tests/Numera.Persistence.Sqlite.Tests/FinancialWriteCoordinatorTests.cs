using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Tests;

[TestClass]
public sealed class FinancialWriteCoordinatorTests
{
    private static SqliteDatabaseFixture Initialized()
    {
        SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.CreateInitializer([.. EmbeddedMigrationCatalog.Load()]).Initialize(1_776_000_000_000);
        Seed(fixture);
        return fixture;
    }

    private static SqliteRetryPolicy Policy() =>
        new(maximumAttempts: 3, baseDelayMilliseconds: 1, jitterMillisecondsProvider: static () => 0);

    private static string Blob(int seed) => $"x'{seed:x2}{new string('0', 30)}'";

    private static void Execute(SqliteDatabaseFixture fixture, string sql)
    {
        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Seed(SqliteDatabaseFixture fixture)
    {
        Execute(fixture, $"""
            INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
            VALUES({Blob(1)}, '900', 'Asia/Tokyo', 'ACTIVE', 1);

            INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                base_money_supply_cap_minor, created_at, retired_at, version)
            VALUES({Blob(2)}, {Blob(1)}, 'ACTIVE', 2, NULL, 1, NULL, 1);

            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(3)}, 'BANK', '銀行主体', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status, created_at, version)
            VALUES({Blob(4)}, {Blob(3)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

            INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key, starts_on,
                ends_on, status, closed_at, version)
            VALUES({Blob(5)}, {Blob(4)}, '2026-08', '2026-08-01', '2026-08-31', 'OPEN', NULL, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                account_kind, accounting_type, normal_side, currency_id, posting_allowed, owner_reference_type,
                owner_reference_id, status, created_at, version)
            VALUES({Blob(6)}, {Blob(4)}, NULL, '1000', 'CASH_ASSET', 'ASSET', 'DEBIT', {Blob(2)}, 1, NULL, NULL,
                'ACTIVE', 1, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                account_kind, accounting_type, normal_side, currency_id, posting_allowed, owner_reference_type,
                owner_reference_id, status, created_at, version)
            VALUES({Blob(7)}, {Blob(4)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY', 'CREDIT', {Blob(2)},
                1, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor, version, updated_at)
            VALUES({Blob(6)}, 0, 0, 1, 1);

            INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor, version, updated_at)
            VALUES({Blob(7)}, 0, 0, 1, 1);

            INSERT INTO business_operations(business_operation_id, operation_type, economy_scope_id, actor_party_id,
                correlation_id, idempotency_scope, idempotency_key, status, created_at, committed_at, version)
            VALUES({Blob(8)}, 'TRANSFER', {Blob(1)}, NULL, {Blob(9)}, 'TRANSFER', 'key-1', 'STARTED', 1, NULL, 1);

            INSERT INTO accounting_transactions(accounting_transaction_id, accounting_book_id, accounting_period_id,
                business_operation_id, currency_id, transaction_type, business_date, occurred_at, posted_at,
                reverses_transaction_id, status, version)
            VALUES({Blob(10)}, {Blob(4)}, {Blob(5)}, {Blob(8)}, {Blob(2)}, 'INTERNAL_TRANSFER', '2026-08-17', 1, 1,
                NULL, 'POSTED', 1);
            """);
    }

    private static long InsertEntry(SqliteUnitOfWork unitOfWork, int seed, int account, string side, long amount)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO journal_entries(journal_entry_id, accounting_transaction_id, ledger_account_id,
                entry_sequence, side, amount_minor, created_at)
            VALUES({Blob(seed)}, {Blob(10)}, {Blob(account)}, {seed}, '{side}', {amount}, 1);
            """);
        return command.ExecuteNonQuery();
    }

    private static long CountJournalEntries(SqliteDatabaseFixture fixture)
    {
        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM journal_entries;";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    [TestMethod]
    public async Task BalancedPostingCommits()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        await using SqliteWriteCoordinator inner = new(fixture.ConnectionFactory, Policy());
        FinancialWriteCoordinator coordinator = new(inner);
        inner.Start();

        WriteOutcome<int> outcome = await coordinator.ExecuteAsync(
            unitOfWork =>
            {
                InsertEntry(unitOfWork, 20, 6, "DEBIT", 5_000);
                InsertEntry(unitOfWork, 21, 7, "CREDIT", 5_000);
                return 2;
            },
            CancellationToken.None);

        Assert.IsTrue(outcome.IsCommitted);
        Assert.AreEqual(2L, CountJournalEntries(fixture));
    }

    [TestMethod]
    public async Task UnbalancedPostingIsRejectedBeforeCommit()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        await using SqliteWriteCoordinator inner = new(fixture.ConnectionFactory, Policy());
        FinancialWriteCoordinator coordinator = new(inner);
        inner.Start();

        PersistenceFailureException exception = await Assert.ThrowsExactlyAsync<PersistenceFailureException>(
            async () => await coordinator.ExecuteAsync(
                unitOfWork =>
                {
                    InsertEntry(unitOfWork, 20, 6, "DEBIT", 5_000);
                    InsertEntry(unitOfWork, 21, 7, "CREDIT", 4_999);
                    return 2;
                },
                CancellationToken.None));

        Assert.AreEqual(PersistenceFailureCode.LedgerUnbalanced, exception.Code);
        Assert.AreEqual(0L, CountJournalEntries(fixture));
    }

    [TestMethod]
    public async Task SingleSidedPostingIsRejected()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        await using SqliteWriteCoordinator inner = new(fixture.ConnectionFactory, Policy());
        FinancialWriteCoordinator coordinator = new(inner);
        inner.Start();

        await Assert.ThrowsExactlyAsync<PersistenceFailureException>(
            async () => await coordinator.ExecuteAsync(
                unitOfWork => InsertEntry(unitOfWork, 20, 6, "DEBIT", 5_000),
                CancellationToken.None));

        Assert.AreEqual(0L, CountJournalEntries(fixture));
    }

    [TestMethod]
    public async Task MaintenanceLaneAppliesTheSameInvariant()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        await using SqliteWriteCoordinator inner = new(fixture.ConnectionFactory, Policy());
        FinancialWriteCoordinator coordinator = new(inner);
        inner.Start();

        await Assert.ThrowsExactlyAsync<PersistenceFailureException>(
            async () => await coordinator.ExecuteMaintenanceAsync(
                unitOfWork => InsertEntry(unitOfWork, 20, 6, "DEBIT", 5_000),
                CancellationToken.None));

        Assert.AreEqual(0L, CountJournalEntries(fixture));
    }

    [TestMethod]
    public async Task InvariantHoldsAcrossSeparateCommittedTransactions()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        await using SqliteWriteCoordinator inner = new(fixture.ConnectionFactory, Policy());
        FinancialWriteCoordinator coordinator = new(inner);
        inner.Start();

        await coordinator.ExecuteAsync(
            unitOfWork =>
            {
                InsertEntry(unitOfWork, 20, 6, "DEBIT", 5_000);
                InsertEntry(unitOfWork, 21, 7, "CREDIT", 5_000);
                return 2;
            },
            CancellationToken.None);

        await Assert.ThrowsExactlyAsync<PersistenceFailureException>(
            async () => await coordinator.ExecuteAsync(
                unitOfWork => InsertEntry(unitOfWork, 22, 6, "DEBIT", 1),
                CancellationToken.None));

        Assert.AreEqual(2L, CountJournalEntries(fixture));
    }
}
