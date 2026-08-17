using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite.Migrations;

namespace Numera.Persistence.Sqlite.Tests;

[TestClass]
public sealed class BankOpeningSchemaTests
{
    private static SqliteDatabaseFixture Initialized()
    {
        SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.CreateInitializer([.. EmbeddedMigrationCatalog.Load()]).Initialize(1_776_000_000_000);
        return fixture;
    }

    private static void Execute(SqliteDatabaseFixture fixture, string sql)
    {
        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteException Rejects(SqliteDatabaseFixture fixture, string sql) =>
        Assert.ThrowsExactly<SqliteException>(() => Execute(fixture, sql));

    private static long Scalar(SqliteDatabaseFixture fixture, string sql)
    {
        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

    [TestMethod]
    public void BankOpeningTablesExist()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Assert.IsTrue(fixture.TableExists("account_opening_applications"));
        Assert.IsTrue(fixture.TableExists("prudential_policy_versions"));
    }

    [TestMethod]
    public void BankReferencesItsCurrentFeeScheduleVersion()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Assert.AreEqual(
            1L,
            Scalar(fixture, """
                SELECT COUNT(*) FROM pragma_foreign_key_list('banks')
                WHERE "table" = 'fee_schedule_versions' AND "from" = 'current_fee_schedule_version_id';
                """));
    }

    [TestMethod]
    public void BankReferencesItsCurrentPolicyVersion()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Assert.AreEqual(
            1L,
            Scalar(fixture, """
                SELECT COUNT(*) FROM pragma_foreign_key_list('banks')
                WHERE "table" = 'bank_policy_versions' AND "from" = 'current_policy_version_id';
                """));
    }

    [TestMethod]
    public void RebuiltBankTableKeepsForeignKeyCheckClean()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";

        using SqliteDataReader reader = command.ExecuteReader();
        Assert.IsFalse(reader.Read());
    }

    [TestMethod]
    public void PendingApplicationIndexIsPartialAndUnique()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Assert.AreEqual(
            1L,
            Scalar(fixture, """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'index' AND name = 'ux_account_opening_applications_pending'
                  AND sql LIKE '%WHERE status IN%';
                """));

        Assert.AreEqual(
            1L,
            Scalar(fixture, """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'index' AND name = 'ix_account_opening_applications_recovery';
                """));
    }

    [TestMethod]
    public void SecondPendingApplicationForTheSameCustomerIsRejected()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        Seed(fixture);

        Execute(fixture, Application(70, "SUBMITTED"));

        Rejects(fixture, Application(71, "SUBMITTED"));
    }

    [TestMethod]
    public void TerminalApplicationDoesNotBlockANewOne()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        Seed(fixture);

        Execute(fixture, Application(70, "REJECTED", decidedAt: "2"));
        Execute(fixture, Application(71, "SUBMITTED"));

        Assert.AreEqual(2L, fixture.CountRows("account_opening_applications"));
    }

    [TestMethod]
    public void SubmittedApplicationCannotCarryADepositAccount()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        Seed(fixture);

        Rejects(fixture, Application(70, "SUBMITTED", depositAccount: Blob(60)));
    }

    [TestMethod]
    public void CompletedApplicationRequiresACompletionTimestamp()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        Seed(fixture);

        Rejects(fixture, Application(70, "COMPLETED", depositAccount: Blob(60), decidedAt: "2"));
    }

    [TestMethod]
    public void CardIssueFeeWithoutAutomaticIssueIsRejected()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        Seed(fixture);

        Rejects(fixture, Application(70, "SUBMITTED", cashCardFee: 100));
    }

    [TestMethod]
    public void PrudentialFloorsAreEnforced()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        Seed(fixture);

        Rejects(fixture, Prudential(80, minimumCet1: 449));
        Rejects(fixture, Prudential(81, minimumCapital: 0));
    }

    [TestMethod]
    public void EconomyScopeAcceptsOnlyOnePublishedPrudentialPolicy()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        Seed(fixture);

        Execute(fixture, Prudential(80));

        Rejects(fixture, Prudential(81, version: 2));
    }

    private static string Prudential(
        int seed,
        int minimumCet1 = 450,
        long minimumCapital = 1000,
        int version = 1) =>
        $"""
        INSERT INTO prudential_policy_versions(prudential_policy_version_id, economy_scope_id,
            minimum_cet1_bps, lending_cet1_bps, minimum_leverage_bps, configured_warning_leverage_bps,
            minimum_liquidity_bps, minimum_initial_bank_capital_minor, status, created_at, published_at,
            retired_at, version)
        VALUES({Blob(seed)}, {Blob(1)}, {minimumCet1}, 700, 300, 300, 10000, {minimumCapital},
            'PUBLISHED', 1, 1, NULL, {version});
        """;

    private static string Application(
        int seed,
        string status,
        string depositAccount = "NULL",
        string decidedAt = "NULL",
        long cashCardFee = 0) =>
        $"""
        INSERT INTO account_opening_applications(account_opening_application_id, bank_id,
            customer_account_id, product_version_id, policy_version_id, fee_schedule_version_id,
            deposit_account_id, funding_source_deposit_account_id, funding_payment_order_id,
            minimum_initial_funding_minor, opening_fee_minor, cash_card_issue_fee_minor,
            debit_card_issue_fee_minor, required_funding_minor, automatic_bank_card_issue_mode,
            decision_mode, status, submitted_at, decided_at, decided_by_discord_user_id, completed_at,
            version)
        VALUES({Blob(seed)}, {Blob(5)}, {Blob(10)}, {Blob(9)}, {Blob(50)}, {Blob(51)},
            {depositAccount}, NULL, NULL, 0, 0, {cashCardFee}, 0, {cashCardFee}, 'NONE',
            'AUTOMATIC', '{status}', 1, {decidedAt}, NULL, NULL, 1);
        """;

    private static void Seed(SqliteDatabaseFixture fixture) => Execute(fixture, $"""
        INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
        VALUES({Blob(1)}, '900', 'Asia/Tokyo', 'ACTIVE', 1);

        INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
            base_money_supply_cap_minor, created_at, retired_at, version)
        VALUES({Blob(2)}, {Blob(1)}, 'ACTIVE', 2, NULL, 1, NULL, 1);

        INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
        VALUES({Blob(3)}, 'BANK', '銀行主体', 'ACTIVE', 1, 1),
              ({Blob(4)}, 'CUSTOMER', '利用者', 'ACTIVE', 1, 1);

        INSERT INTO customer_accounts(customer_account_id, party_id, public_handle, display_name, status,
            created_at, last_authenticated_at, version)
        VALUES({Blob(10)}, {Blob(4)}, 'taro', '利用者', 'ACTIVE', 1, 1, 1);

        INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status, created_at, version)
        VALUES({Blob(6)}, {Blob(3)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

        INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
            resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
            current_fee_schedule_version_id, created_at, version)
        VALUES({Blob(5)}, {Blob(1)}, {Blob(3)}, 'NUM0001', 'ヌメラ銀行', 'NORMAL', NULL,
            'OPERATING', {Blob(6)}, NULL, NULL, 1, 1);

        INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
            version_application_policy, status, created_at, version)
        VALUES({Blob(8)}, {Blob(5)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST', 'ACTIVE', 1, 1);

        INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
            effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor, maximum_balance_minor,
            daily_outgoing_limit_minor, per_transaction_limit_minor, transfer_capabilities,
            deposit_insurance_class_code, overdraft_policy, created_at)
        VALUES({Blob(9)}, {Blob(8)}, 1, 1, NULL, 0, 'ACTUAL_365_FIXED', 0, NULL, NULL, NULL,
            'INTERNAL', 'STANDARD', 'NONE', 1);

        INSERT INTO bank_policy_versions(bank_policy_version_id, bank_id, opening_enabled,
            minimum_customer_account_age_days, minimum_initial_funding_minor, requires_manual_approval,
            reopen_closed_account_allowed, public_receiving_enabled_default, cash_card_enabled,
            debit_card_enabled, integrated_cash_debit_default, automatic_bank_card_issue_mode,
            cash_atm_enabled, cash_card_validity_months, debit_card_validity_months,
            per_transfer_limit_minor, daily_outgoing_limit_minor, per_atm_withdrawal_limit_minor,
            daily_atm_withdrawal_limit_minor, daily_atm_transfer_limit_minor,
            daily_debit_purchase_limit_minor, daily_fx_order_notional_limit_minor,
            maximum_active_holds_minor, effective_from, effective_to, version)
        VALUES({Blob(50)}, {Blob(5)}, 1, 0, 0, 0, 1, 1, 0, 0, 0, 'NONE', 0, NULL, 12,
            NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, 1);

        INSERT INTO fee_schedule_versions(fee_schedule_version_id, bank_id, effective_from,
            effective_to, version)
        VALUES({Blob(51)}, {Blob(5)}, 1, NULL, 1);
        """);
}
