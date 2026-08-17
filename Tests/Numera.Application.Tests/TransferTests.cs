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
public sealed class TransferTests
{
    private const string Institution = "NUM0001";
    private const string OtherInstitution = "NUM0002";
    private const string Branch = "001";
    private const ulong PayerUser = 710_000_000_000_000_001UL;
    private const ulong PayeeUser = 710_000_000_000_000_002UL;

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string root;

        private Harness(string root, SqliteDatabaseOptions options)
        {
            this.root = root;
            ConnectionFactory = new SqliteConnectionFactory(options);
            Clock = new FixedClock();
        }

        public SqliteConnectionFactory ConnectionFactory { get; }

        public FixedClock Clock { get; }

        public SqliteWriteCoordinator Coordinator { get; private set; } = null!;

        public CustomerAccountApplicationService Registration { get; private set; } = null!;

        public DepositAccountApplicationService Accounts { get; private set; } = null!;

        public PaymentApplicationService Payments { get; private set; } = null!;

        public EconomyScopeId Scope { get; } = EconomyScopeId.FromValue(EntityIdValue.FromBits(1));

        public static Harness Create(bool withPeriod = true, bool withSecondBank = false)
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-transfer", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            new SqliteDatabaseInitializer(
                options, harness.ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_776_000_000_000);
            harness.Seed(withPeriod, withSecondBank);

            harness.Coordinator = new SqliteWriteCoordinator(
                harness.ConnectionFactory, new SqliteRetryPolicy(3, 1, static () => 0));
            harness.Coordinator.Start();

            SqliteBankingWriteGateway gateway = new(new FinancialWriteCoordinator(harness.Coordinator));
            SequentialIdGenerator ids = new(9_000);

            harness.Registration = new CustomerAccountApplicationService(gateway, harness.Clock, ids);
            harness.Accounts = new DepositAccountApplicationService(gateway, harness.Clock, ids);
            harness.Payments = new PaymentApplicationService(gateway, harness.Clock, ids);

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed(bool withPeriod, bool withSecondBank)
        {
            Execute($"""
                INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
                VALUES({Blob(1)}, '900', 'Asia/Tokyo', 'ACTIVE', 1);

                INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                    base_money_supply_cap_minor, created_at, retired_at, version)
                VALUES({Blob(2)}, {Blob(1)}, 'ACTIVE', 2, NULL, 1, NULL, 1);

                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(3)}, 'BANK', '銀行主体', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status, created_at, version)
                VALUES({Blob(4)}, {Blob(3)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

                INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                    resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                    current_fee_schedule_version_id, created_at, version)
                VALUES({Blob(5)}, {Blob(1)}, {Blob(3)}, '{Institution}', 'ヌメラ銀行', 'NORMAL', NULL,
                    'OPERATING', {Blob(4)}, NULL, NULL, 1, 1);

                INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at, version)
                VALUES({Blob(6)}, {Blob(5)}, '{Branch}', '本店', 'ACTIVE', 1, NULL, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                    account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES({Blob(7)}, {Blob(4)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY', 'CREDIT',
                    {Blob(2)}, 0, NULL, NULL, 'ACTIVE', 1, 1);

                INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                    version_application_policy, status, created_at, version)
                VALUES({Blob(8)}, {Blob(5)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST', 'ACTIVE', 1, 1);

                INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                    effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor, maximum_balance_minor,
                    daily_outgoing_limit_minor, per_transaction_limit_minor, transfer_capabilities,
                    deposit_insurance_class_code, overdraft_policy, created_at)
                VALUES({Blob(9)}, {Blob(8)}, 1, 1, NULL, 1000000000, 'ACTUAL_365_FIXED', 0, NULL, NULL, NULL,
                    'INTERNAL', 'STANDARD', 'NONE', 1);
                """);

            if (withPeriod)
            {
                Execute($"""
                    INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                        starts_on, ends_on, status, closed_at, version)
                    VALUES({Blob(10)}, {Blob(4)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);
                    """);
            }

            if (!withSecondBank)
            {
                return;
            }

            Execute($"""
                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(20)}, 'BANK', '第二銀行主体', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status, created_at, version)
                VALUES({Blob(21)}, {Blob(20)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

                INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                    resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                    current_fee_schedule_version_id, created_at, version)
                VALUES({Blob(22)}, {Blob(1)}, {Blob(20)}, '{OtherInstitution}', '第二銀行', 'NORMAL', NULL,
                    'OPERATING', {Blob(21)}, NULL, NULL, 1, 1);

                INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at, version)
                VALUES({Blob(23)}, {Blob(22)}, '{Branch}', '本店', 'ACTIVE', 1, NULL, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                    account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES({Blob(24)}, {Blob(21)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY', 'CREDIT',
                    {Blob(2)}, 0, NULL, NULL, 'ACTIVE', 1, 1);

                INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                    version_application_policy, status, created_at, version)
                VALUES({Blob(25)}, {Blob(22)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST', 'ACTIVE', 1, 1);

                INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                    effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor, maximum_balance_minor,
                    daily_outgoing_limit_minor, per_transaction_limit_minor, transfer_capabilities,
                    deposit_insurance_class_code, overdraft_policy, created_at)
                VALUES({Blob(26)}, {Blob(25)}, 1, 1, NULL, 1000000000, 'ACTUAL_365_FIXED', 0, NULL, NULL, NULL,
                    'INTERNAL', 'STANDARD', 'NONE', 1);
                """);
        }

        public void Execute(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public long Count(string table)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public string ReadText(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar()?.ToString() ?? string.Empty;
        }

        public long Balance(DepositAccountId accountId)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.posted_balance_minor
                FROM ledger_balance_projections p
                JOIN deposit_accounts d ON d.ledger_account_id = p.ledger_account_id
                WHERE d.deposit_account_id = $id;
                """;
            command.Parameters.AddWithValue("$id", accountId.Value.ToByteArray());
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public long Held(DepositAccountId accountId)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.held_minor
                FROM ledger_balance_projections p
                JOIN deposit_accounts d ON d.ledger_account_id = p.ledger_account_id
                WHERE d.deposit_account_id = $id;
                """;
            command.Parameters.AddWithValue("$id", accountId.Value.ToByteArray());
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public void Fund(DepositAccountId accountId, long amount)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ledger_balance_projections
                SET posted_balance_minor = $amount, version = version + 1
                WHERE ledger_account_id = (
                    SELECT ledger_account_id FROM deposit_accounts WHERE deposit_account_id = $id);
                """;
            command.Parameters.AddWithValue("$amount", amount);
            command.Parameters.AddWithValue("$id", accountId.Value.ToByteArray());
            command.ExecuteNonQuery();
        }

        public async Task<CustomerAccountId> RegisterAsync(ulong discordUserId, string handle)
        {
            Result<CustomerAccountView> result = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(Scope, discordUserId, handle, "利用者"),
                CancellationToken.None);

            return result.Value.Id;
        }

        public async Task<DepositAccountView> OpenAsync(
            CustomerAccountId customerAccountId,
            string institutionCode = Institution)
        {
            Result<DepositAccountView> result = await Accounts.OpenAsync(
                new OpenDepositAccountCommand(Scope, customerAccountId, institutionCode),
                CancellationToken.None);

            return result.Value;
        }

        public Task<Result<PaymentOrderView>> TransferAsync(
            CustomerAccountId payer,
            DepositAccountId source,
            string destinationAccountNumber,
            long amount,
            string token = "interaction-1",
            string institution = Institution,
            string branch = Branch,
            string? memo = null) =>
            Payments.CreatePaymentOrderAsync(
                new CreatePaymentOrderCommand(
                    Scope, payer, source, institution, branch, destinationAccountNumber, amount, memo, token),
                CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
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

    private sealed record Parties(
        CustomerAccountId Payer,
        DepositAccountView Source,
        CustomerAccountId Payee,
        DepositAccountView Destination);

    private static async Task<Parties> SetupAsync(Harness harness, long funding = 1_000)
    {
        CustomerAccountId payer = await harness.RegisterAsync(PayerUser, "taro");
        CustomerAccountId payee = await harness.RegisterAsync(PayeeUser, "hanako");

        DepositAccountView source = await harness.OpenAsync(payer);
        DepositAccountView destination = await harness.OpenAsync(payee);

        harness.Fund(source.Id, funding);

        return new Parties(payer, source, payee, destination);
    }

    [TestMethod]
    public async Task TransferMovesMoneyBetweenAccountsInTheSameBank()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentOrderStatus.Completed, result.Value.Status);
        Assert.AreEqual(700L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(300L, harness.Balance(parties.Destination.Id));
    }

    [TestMethod]
    public async Task TransferLeavesNoActiveHold()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(0L, harness.Held(parties.Source.Id));
        Assert.AreEqual("CAPTURED", harness.ReadText("SELECT status FROM holds;"));
        Assert.AreEqual("0", harness.ReadText("SELECT CAST(remaining_minor AS TEXT) FROM holds;"));
    }

    [TestMethod]
    public async Task TransferPostsABalancedJournal()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(1L, harness.Count("accounting_transactions"));
        Assert.AreEqual(2L, harness.Count("journal_entries"));
        Assert.AreEqual(
            "300",
            harness.ReadText("SELECT CAST(SUM(amount_minor) AS TEXT) FROM journal_entries WHERE side = 'DEBIT';"));
        Assert.AreEqual(
            "300",
            harness.ReadText("SELECT CAST(SUM(amount_minor) AS TEXT) FROM journal_entries WHERE side = 'CREDIT';"));
    }

    [TestMethod]
    public async Task CompletedTransferRecordsBothCanonicalFacts()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual("COMPLETED", harness.ReadText("SELECT status FROM payment_orders;"));
        Assert.AreEqual("INTERNAL", harness.ReadText("SELECT settlement_mode FROM payment_orders;"));
        Assert.AreEqual(
            "IMMEDIATE_AFTER_ACCEPTANCE",
            harness.ReadText("SELECT beneficiary_posting_policy FROM payment_orders;"));
        Assert.AreNotEqual(
            string.Empty,
            harness.ReadText("SELECT CAST(beneficiary_posted_at AS TEXT) FROM payment_orders;"));
        Assert.AreEqual(
            string.Empty,
            harness.ReadText("SELECT CAST(settlement_finalized_at AS TEXT) FROM payment_orders;"));
    }

    [TestMethod]
    public async Task TransferEmitsOutboxEventAndCommitsTheOperation()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(
            PaymentApplicationService.CompletedEventType,
            harness.ReadText($"""
                SELECT event_type FROM outbox_events
                WHERE event_type = '{PaymentApplicationService.CompletedEventType}';
                """));
        Assert.AreEqual(
            "COMMITTED",
            harness.ReadText($"""
                SELECT status FROM business_operations
                WHERE idempotency_scope = '{PaymentApplicationService.OperationType}';
                """));
    }

    [TestMethod]
    public async Task InsufficientAvailableBalanceIsRejectedWithoutAnyEffect()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness, funding: 100);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AvailableBalanceInsufficient, result.Error!.Code);
        Assert.AreEqual(100L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(0L, harness.Count("holds"));
        Assert.AreEqual(0L, harness.Count("payment_orders"));
        Assert.AreEqual(0L, harness.Count("journal_entries"));
    }

    [TestMethod]
    public async Task ForeignSourceAccountIsNormalizedToNotFound()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payee, parties.Source.Id, parties.Destination.AccountNumber, 100);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.DepositAccountNotFound, result.Error.Code);
    }

    [TestMethod]
    public async Task UnknownDestinationAccountIsNotFound()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, "0009999999", 100);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.DepositAccountNotFound, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("payment_orders"));
    }

    [TestMethod]
    public async Task TransferToOwnAccountIsRejected()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Source.AccountNumber, 100);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.SelfTransferRejected, result.Error!.Code);
    }

    [TestMethod]
    public async Task InterbankTransferIsRefusedUntilImplemented()
    {
        await using Harness harness = Harness.Create(withSecondBank: true);
        Parties parties = await SetupAsync(harness);

        CustomerAccountId remote = await harness.RegisterAsync(710_000_000_000_000_003UL, "jiro");
        DepositAccountView remoteAccount = await harness.OpenAsync(remote, OtherInstitution);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer,
            parties.Source.Id,
            remoteAccount.AccountNumber,
            100,
            institution: OtherInstitution);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.InterbankTransferUnavailable, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("payment_orders"));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public async Task NonPositiveAmountIsRejected(long amount)
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, amount);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AmountInvalid, result.Error!.Code);
    }

    [TestMethod]
    public async Task OverlongMemoIsRejected()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer,
            parties.Source.Id,
            parties.Destination.AccountNumber,
            100,
            memo: new string('あ', PaymentOrder.MaximumMemoLength + 1));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MemoTooLong, result.Error!.Code);
    }

    [TestMethod]
    public async Task MissingAccountingPeriodStopsPostingAfterTheHold()
    {
        await using Harness harness = Harness.Create(withPeriod: false);
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AccountingPeriodUnavailable, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("journal_entries"));
        Assert.AreEqual(1_000L, harness.Balance(parties.Source.Id));
    }

    [TestMethod]
    public async Task RepeatedInteractionProducesExactlyOneMonetaryEffect()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            Result<PaymentOrderView> result = await harness.TransferAsync(
                parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

            Assert.IsTrue(result.IsSuccess);
        }

        Assert.AreEqual(1L, harness.Count("payment_orders"));
        Assert.AreEqual(2L, harness.Count("journal_entries"));
        Assert.AreEqual(700L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(300L, harness.Balance(parties.Destination.Id));
    }

    [TestMethod]
    public async Task ConcurrentTransfersNeverOverdrawTheSourceAccount()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness, funding: 100);

        Task<Result<PaymentOrderView>>[] attempts =
        [
            harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 80, "a"),
            harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 80, "b"),
            harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 80, "c"),
            harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 80, "d"),
        ];

        Result<PaymentOrderView>[] results = await Task.WhenAll(attempts);

        Assert.AreEqual(1, results.Count(static result => result.IsSuccess));
        Assert.AreEqual(20L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(80L, harness.Balance(parties.Destination.Id));
        Assert.AreEqual(0L, harness.Held(parties.Source.Id));
    }

    [TestMethod]
    public async Task TransferRecordsCustomerActivityOnTheSourceAccount()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        harness.Clock.Advance(60_000);
        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(
            harness.ReadText("SELECT CAST(completed_at AS TEXT) FROM payment_orders;"),
            harness.ReadText($"""
                SELECT CAST(last_customer_activity_at AS TEXT) FROM deposit_accounts
                WHERE account_number = '{parties.Source.AccountNumber}';
                """));
    }
}
