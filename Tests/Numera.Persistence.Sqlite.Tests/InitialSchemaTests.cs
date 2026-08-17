using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;

namespace Numera.Persistence.Sqlite.Tests;

[TestClass]
public sealed class InitialSchemaTests
{
    private static readonly string[] RequiredTables =
    [
        "host_settings",
        "system_owner_identities",
        "parties",
        "customer_accounts",
        "discord_identity_links",
        "guild_economies",
        "currencies",
        "currency_metadata_versions",
        "accounting_books",
        "accounting_periods",
        "banks",
        "branches",
        "bank_customer_relationships",
        "account_products",
        "account_product_versions",
        "ledger_accounts",
        "ledger_balance_projections",
        "deposit_accounts",
        "business_operations",
        "accounting_transactions",
        "journal_entries",
        "holds",
        "payment_orders",
        "outbox_events",
        "idempotency_records",
        "interaction_sessions",
        "bank_operator_grants",
        "audit_records",
        "economy_calendar_overrides",
        "fee_schedule_versions",
        "fee_rules",
        "fee_waiver_usage_counters",
        "fee_assessments",
        "bank_policy_versions",
        "account_limit_preferences",
        "central_bank_settlement_accounts",
        "settlement_participations",
        "settlement_instructions",
        "payment_preferences",
    ];

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

    private const string EmptyJson = "{}";

    private static string Blob(int seed) =>
        $"x'{new string('0', 30)}{seed:x2}'";

    [TestMethod]
    public void EmbeddedCatalogContainsInitialMigration()
    {
        IReadOnlyList<SqlMigration> migrations = EmbeddedMigrationCatalog.Load();

        Assert.IsGreaterThanOrEqualTo(1, migrations.Count);
        Assert.AreEqual(1, migrations[0].Version);
        Assert.AreEqual("initial", migrations[0].Name);
    }

    [TestMethod]
    public void EveryRequiredTableExists()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        foreach (string table in RequiredTables)
        {
            Assert.IsTrue(fixture.TableExists(table), $"{table} が作成されていません。");
        }
    }

    [TestMethod]
    public void EveryTableIsStrict()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND sql NOT LIKE '%STRICT%';
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        List<string> lenient = [];
        while (reader.Read())
        {
            lenient.Add(reader.GetString(0));
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), lenient);
    }

    [TestMethod]
    public void IdentifierColumnsRejectWrongLength()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Rejects(fixture, """
            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES(x'00', 'CUSTOMER', '利用者', 'ACTIVE', 1, 1);
            """);
    }

    [TestMethod]
    public void UnknownEnumTokenIsRejected()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Rejects(fixture, $"""
            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(1)}, 'UNKNOWN', '利用者', 'ACTIVE', 1, 1);
            """);
    }

    [TestMethod]
    public void SameDiscordUserCannotHoldTwoActiveLinks()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedTwoCustomers(fixture);

        Execute(fixture, $"""
            INSERT INTO discord_identity_links(discord_identity_link_id, customer_account_id, discord_user_id,
                is_primary, status, linked_at, unlinked_at, last_authenticated_at, version)
            VALUES({Blob(20)}, {Blob(10)}, '111', 1, 'ACTIVE', 1, NULL, 1, 1);
            """);

        Rejects(fixture, $"""
            INSERT INTO discord_identity_links(discord_identity_link_id, customer_account_id, discord_user_id,
                is_primary, status, linked_at, unlinked_at, last_authenticated_at, version)
            VALUES({Blob(21)}, {Blob(11)}, '111', 1, 'ACTIVE', 1, NULL, 1, 1);
            """);
    }

    [TestMethod]
    public void UnlinkedHistoryDoesNotBlockRelink()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedTwoCustomers(fixture);

        Execute(fixture, $"""
            INSERT INTO discord_identity_links(discord_identity_link_id, customer_account_id, discord_user_id,
                is_primary, status, linked_at, unlinked_at, last_authenticated_at, version)
            VALUES({Blob(20)}, {Blob(10)}, '111', 0, 'UNLINKED', 1, 2, 1, 1);
            """);

        Execute(fixture, $"""
            INSERT INTO discord_identity_links(discord_identity_link_id, customer_account_id, discord_user_id,
                is_primary, status, linked_at, unlinked_at, last_authenticated_at, version)
            VALUES({Blob(21)}, {Blob(11)}, '111', 1, 'ACTIVE', 3, NULL, 3, 1);
            """);
    }

    [TestMethod]
    public void CustomerAccountCannotHoldTwoActivePrimaryLinks()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedTwoCustomers(fixture);

        Execute(fixture, $"""
            INSERT INTO discord_identity_links(discord_identity_link_id, customer_account_id, discord_user_id,
                is_primary, status, linked_at, unlinked_at, last_authenticated_at, version)
            VALUES({Blob(20)}, {Blob(10)}, '111', 1, 'ACTIVE', 1, NULL, 1, 1);
            """);

        Rejects(fixture, $"""
            INSERT INTO discord_identity_links(discord_identity_link_id, customer_account_id, discord_user_id,
                is_primary, status, linked_at, unlinked_at, last_authenticated_at, version)
            VALUES({Blob(21)}, {Blob(10)}, '222', 1, 'ACTIVE', 1, NULL, 1, 1);
            """);
    }

    [TestMethod]
    public void UnlinkedLinkCannotBePrimary()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedTwoCustomers(fixture);

        Rejects(fixture, $"""
            INSERT INTO discord_identity_links(discord_identity_link_id, customer_account_id, discord_user_id,
                is_primary, status, linked_at, unlinked_at, last_authenticated_at, version)
            VALUES({Blob(20)}, {Blob(10)}, '111', 1, 'UNLINKED', 1, 2, 1, 1);
            """);
    }

    [TestMethod]
    public void GuildCannotHoldTwoCurrentCurrencies()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedEconomy(fixture);

        Rejects(fixture, $"""
            INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                base_money_supply_cap_minor, created_at, retired_at, version)
            VALUES({Blob(31)}, {Blob(30)}, 'SUSPENDED', 2, NULL, 1, NULL, 1);
            """);
    }

    [TestMethod]
    public void RetiredCurrencyDoesNotBlockNewCurrency()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedEconomy(fixture);

        Execute(fixture, $"UPDATE currencies SET status = 'RETIRED', retired_at = 2 WHERE currency_id = {Blob(32)};");

        Execute(fixture, $"""
            INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                base_money_supply_cap_minor, created_at, retired_at, version)
            VALUES({Blob(33)}, {Blob(30)}, 'ACTIVE', 2, NULL, 3, NULL, 1);
            """);
    }

    [TestMethod]
    public void MinorUnitDigitsOutsideCanonicalRangeIsRejected()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedGuildOnly(fixture);

        Rejects(fixture, $"""
            INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                base_money_supply_cap_minor, created_at, retired_at, version)
            VALUES({Blob(32)}, {Blob(30)}, 'ACTIVE', 7, NULL, 1, NULL, 1);
            """);
    }

    [TestMethod]
    public void NormalBankCannotCarryResolutionCase()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedEconomy(fixture);
        SeedBankPrerequisites(fixture);

        Rejects(fixture, $"""
            INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                current_fee_schedule_version_id, created_at, version)
            VALUES({Blob(40)}, {Blob(30)}, {Blob(41)}, 'NUM0001', '銀行', 'NORMAL',
                {Blob(99)}, 'OPERATING', {Blob(42)}, NULL, NULL, 1, 1);
            """);
    }

    [TestMethod]
    public void ClosedDepositAccountRequiresMatchingClosureReason()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Rejects(fixture, $"""
            INSERT INTO deposit_accounts(deposit_account_id, bank_id, branch_id, relationship_id,
                customer_account_id, currency_id, product_id, current_product_version_id, ledger_account_id,
                account_number, public_receiving_enabled, last_customer_activity_at, next_dormancy_fee_at,
                status, opened_at, closing_requested_at, closure_reason, closed_at, version)
            VALUES({Blob(50)}, {Blob(40)}, {Blob(51)}, {Blob(52)}, {Blob(10)}, {Blob(32)}, {Blob(53)},
                {Blob(54)}, {Blob(55)}, '0012345678', 1, 1, NULL, 'CLOSED_USER', 1, 1, 'DORMANCY', 2, 1);
            """);
    }

    [TestMethod]
    public void JournalEntryAmountMustBePositive()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Rejects(fixture, $"""
            INSERT INTO journal_entries(journal_entry_id, accounting_transaction_id, ledger_account_id,
                entry_sequence, side, amount_minor, created_at)
            VALUES({Blob(60)}, {Blob(61)}, {Blob(62)}, 1, 'DEBIT', 0, 1);
            """);
    }

    [TestMethod]
    public void ActiveHoldMustRetainRemainingAmount()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Rejects(fixture, $"""
            INSERT INTO holds(hold_id, hold_scope_kind, deposit_account_id, ledger_account_id,
                business_operation_id, amount_minor, remaining_minor, reason, status, created_at,
                expires_at, terminal_at, version)
            VALUES({Blob(70)}, 'CUSTOMER_DEPOSIT', {Blob(50)}, NULL, {Blob(71)}, 100, 0, 'TRANSFER',
                'ACTIVE', 1, NULL, NULL, 1);
            """);
    }

    [TestMethod]
    public void HoldScopeRequiresExactlyOneReference()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Rejects(fixture, $"""
            INSERT INTO holds(hold_id, hold_scope_kind, deposit_account_id, ledger_account_id,
                business_operation_id, amount_minor, remaining_minor, reason, status, created_at,
                expires_at, terminal_at, version)
            VALUES({Blob(70)}, 'CUSTOMER_DEPOSIT', {Blob(50)}, {Blob(62)}, {Blob(71)}, 100, 100, 'TRANSFER',
                'ACTIVE', 1, NULL, NULL, 1);
            """);
    }

    [TestMethod]
    public void InternalPaymentOrderCannotCarryNetworkPolicy()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Rejects(fixture, $"""
            INSERT INTO payment_orders(payment_order_id, business_operation_id, payer_customer_account_id,
                source_deposit_account_id, destination_deposit_account_id, currency_id, amount_minor, method,
                settlement_mode, beneficiary_posting_policy, payment_network_policy_version_id, memo, status,
                beneficiary_posted_at, settlement_finalized_at, created_at, completed_at, version)
            VALUES({Blob(80)}, {Blob(81)}, {Blob(10)}, {Blob(50)}, {Blob(56)}, {Blob(32)}, 100, 'TRANSFER',
                'INTERNAL', 'IMMEDIATE_AFTER_ACCEPTANCE', {Blob(82)}, NULL, 'CREATED', NULL, NULL, 1, NULL, 1);
            """);
    }

    [TestMethod]
    public void PaymentOrderCannotTargetItsOwnSourceAccount()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Rejects(fixture, $"""
            INSERT INTO payment_orders(payment_order_id, business_operation_id, payer_customer_account_id,
                source_deposit_account_id, destination_deposit_account_id, currency_id, amount_minor, method,
                settlement_mode, beneficiary_posting_policy, payment_network_policy_version_id, memo, status,
                beneficiary_posted_at, settlement_finalized_at, created_at, completed_at, version)
            VALUES({Blob(80)}, {Blob(81)}, {Blob(10)}, {Blob(50)}, {Blob(50)}, {Blob(32)}, 100, 'TRANSFER',
                'INTERNAL', 'IMMEDIATE_AFTER_ACCEPTANCE', NULL, NULL, 'CREATED', NULL, NULL, 1, NULL, 1);
            """);
    }

    [TestMethod]
    public void ClaimedOutboxEventRequiresClaimMetadata()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Rejects(fixture, $"""
            INSERT INTO outbox_events(outbox_event_id, business_operation_id, event_type, payload_json, status,
                claim_token, claimed_at, claim_expires_at, next_attempt_at, created_at, published_at,
                attempt_count, last_error_code, version)
            VALUES({Blob(90)}, NULL, 'TRANSFER_COMPLETED', '{EmptyJson}', 'CLAIMED', NULL, NULL, NULL, NULL, 1, NULL, 0, NULL, 1);
            """);
    }

    [TestMethod]
    public void IdempotencyKeyIsUniqueWithinScope()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Execute(fixture, $"""
            INSERT INTO idempotency_records(idempotency_record_id, idempotency_scope, idempotency_key,
                business_operation_id, operation_result_id, created_at, completed_at)
            VALUES({Blob(100)}, 'TRANSFER', 'key-1', NULL, NULL, 1, NULL);
            """);

        Rejects(fixture, $"""
            INSERT INTO idempotency_records(idempotency_record_id, idempotency_scope, idempotency_key,
                business_operation_id, operation_result_id, created_at, completed_at)
            VALUES({Blob(101)}, 'TRANSFER', 'key-1', NULL, NULL, 1, NULL);
            """);
    }

    [TestMethod]
    public void SameKeyInAnotherScopeIsAccepted()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Execute(fixture, $"""
            INSERT INTO idempotency_records(idempotency_record_id, idempotency_scope, idempotency_key,
                business_operation_id, operation_result_id, created_at, completed_at)
            VALUES({Blob(100)}, 'TRANSFER', 'key-1', NULL, NULL, 1, NULL);
            """);

        Execute(fixture, $"""
            INSERT INTO idempotency_records(idempotency_record_id, idempotency_scope, idempotency_key,
                business_operation_id, operation_result_id, created_at, completed_at)
            VALUES({Blob(101)}, 'ACCOUNT_OPEN', 'key-1', NULL, NULL, 1, NULL);
            """);
    }

    [TestMethod]
    public void SessionTokenHashIsUnique()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedGuildOnly(fixture);
        string hash = $"x'{new string('a', 64)}'";

        Execute(fixture, $"""
            INSERT INTO interaction_sessions(interaction_session_id, discord_user_id, guild_id, economy_scope_id,
                flow_type, state, token_hash, payload_json, state_version, status, created_at, expires_at, completed_at)
            VALUES({Blob(110)}, '111', '900', {Blob(30)}, 'BANK_TRANSFER', 'AMOUNT_INPUT', {hash},
                '{EmptyJson}', 0, 'ACTIVE', 1, 2, NULL);
            """);

        Rejects(fixture, $"""
            INSERT INTO interaction_sessions(interaction_session_id, discord_user_id, guild_id, economy_scope_id,
                flow_type, state, token_hash, payload_json, state_version, status, created_at, expires_at, completed_at)
            VALUES({Blob(111)}, '222', '900', {Blob(30)}, 'BANK_TRANSFER', 'AMOUNT_INPUT', {hash},
                '{EmptyJson}', 0, 'ACTIVE', 1, 2, NULL);
            """);
    }

    [TestMethod]
    public void SupersededSessionStatusIsAccepted()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedGuildOnly(fixture);

        Execute(fixture, $"""
            INSERT INTO interaction_sessions(interaction_session_id, discord_user_id, guild_id, economy_scope_id,
                flow_type, state, token_hash, payload_json, state_version, status, created_at, expires_at, completed_at)
            VALUES({Blob(112)}, '111', '900', {Blob(30)}, 'BANK_TRANSFER', 'AMOUNT_INPUT',
                x'{new string('b', 64)}', '{EmptyJson}', 0, 'SUPERSEDED', 1, 2, 2);
            """);
    }

    [TestMethod]
    public void ActiveSessionCannotCarryCompletionTimestamp()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedGuildOnly(fixture);

        Rejects(fixture, $"""
            INSERT INTO interaction_sessions(interaction_session_id, discord_user_id, guild_id, economy_scope_id,
                flow_type, state, token_hash, payload_json, state_version, status, created_at, expires_at, completed_at)
            VALUES({Blob(113)}, '111', '900', {Blob(30)}, 'BANK_TRANSFER', 'AMOUNT_INPUT',
                x'{new string('c', 64)}', '{EmptyJson}', 0, 'ACTIVE', 1, 2, 2);
            """);
    }

    [TestMethod]
    public void SessionExpiryMustFollowCreation()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedGuildOnly(fixture);

        Rejects(fixture, $"""
            INSERT INTO interaction_sessions(interaction_session_id, discord_user_id, guild_id, economy_scope_id,
                flow_type, state, token_hash, payload_json, state_version, status, created_at, expires_at, completed_at)
            VALUES({Blob(114)}, '111', '900', {Blob(30)}, 'BANK_TRANSFER', 'AMOUNT_INPUT',
                x'{new string('d', 64)}', '{EmptyJson}', 0, 'ACTIVE', 5, 5, NULL);
            """);
    }

    [TestMethod]
    public void ForeignKeyDeletionIsRestricted()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedTwoCustomers(fixture);

        Rejects(fixture, $"DELETE FROM parties WHERE party_id = {Blob(1)};");
    }

    private static void SeedGuildOnly(SqliteDatabaseFixture fixture) =>
        Execute(fixture, $"""
            INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
            VALUES({Blob(30)}, '900', 'Asia/Tokyo', 'ACTIVE', 1);
            """);

    private static void SeedEconomy(SqliteDatabaseFixture fixture)
    {
        SeedGuildOnly(fixture);
        Execute(fixture, $"""
            INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                base_money_supply_cap_minor, created_at, retired_at, version)
            VALUES({Blob(32)}, {Blob(30)}, 'ACTIVE', 2, NULL, 1, NULL, 1);
            """);
    }

    private static void SeedBankPrerequisites(SqliteDatabaseFixture fixture)
    {
        Execute(fixture, $"""
            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(41)}, 'BANK', '銀行主体', 'ACTIVE', 1, 1);
            """);
        Execute(fixture, $"""
            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status, created_at, version)
            VALUES({Blob(42)}, {Blob(41)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);
            """);
    }

    private static void SeedTwoCustomers(SqliteDatabaseFixture fixture)
    {
        Execute(fixture, $"""
            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(1)}, 'CUSTOMER', '利用者1', 'ACTIVE', 1, 1);
            """);
        Execute(fixture, $"""
            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(2)}, 'CUSTOMER', '利用者2', 'ACTIVE', 1, 1);
            """);
        Execute(fixture, $"""
            INSERT INTO customer_accounts(customer_account_id, party_id, public_handle, display_name, status,
                created_at, last_authenticated_at, version)
            VALUES({Blob(10)}, {Blob(1)}, 'taro', '利用者1', 'ACTIVE', 1, 1, 1);
            """);
        Execute(fixture, $"""
            INSERT INTO customer_accounts(customer_account_id, party_id, public_handle, display_name, status,
                created_at, last_authenticated_at, version)
            VALUES({Blob(11)}, {Blob(2)}, 'hanako', '利用者2', 'ACTIVE', 1, 1, 1);
            """);
    }
}
