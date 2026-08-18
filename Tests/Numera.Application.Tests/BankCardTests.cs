using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Repositories;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Application.Tests;

[TestClass]
public sealed class BankCardTests
{
    private const ulong GuildId = 960UL;
    private const string Institution = "NUM0001";
    private const ulong FirstUser = 760_000_000_000_000_001UL;
    private const ulong SecondUser = 760_000_000_000_000_002UL;

    private sealed class StubCardImageRenderer : IBankCardImageRenderer
    {
        public BankCardImage? TryRender(BankCardRenderModel model) =>
            new("bank-card.png", 1026, 647, [0x89, 0x50, 0x4E, 0x47, 0x0D]);
    }

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

        public BankAccountApplicationService Accounts { get; private set; } = null!;

        public BankCardApplicationService Cards { get; private set; } = null!;

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-card", Guid.NewGuid().ToString("n"));
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

            harness.Registration = new CustomerAccountApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);
            harness.Accounts = new BankAccountApplicationService(
                gateway,
                new PaymentApplicationService(
                    gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids),
                harness.Clock,
                ids);
            harness.Cards = new BankCardApplicationService(
                gateway, harness.Clock, ids, new StubCardImageRenderer());

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
            VALUES({Blob(3)}, 'BANK', '銀行主体', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status, created_at, version)
            VALUES({Blob(4)}, {Blob(3)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

            INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                current_fee_schedule_version_id, created_at, version)
            VALUES({Blob(5)}, {Blob(1)}, {Blob(3)}, '{Institution}', 'ヌメラ銀行', 'NORMAL', NULL,
                'OPERATING', {Blob(4)}, NULL, NULL, 1, 1);

            INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at, version)
            VALUES({Blob(6)}, {Blob(5)}, '001', '本店', 'ACTIVE', 1, NULL, 1);

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

            INSERT INTO bank_policy_versions(bank_policy_version_id, bank_id, opening_enabled,
                minimum_customer_account_age_days, minimum_initial_funding_minor, requires_manual_approval,
                reopen_closed_account_allowed, public_receiving_enabled_default, cash_card_enabled,
                debit_card_enabled, integrated_cash_debit_default, automatic_bank_card_issue_mode,
                cash_atm_enabled, cash_card_validity_months, debit_card_validity_months,
                per_transfer_limit_minor, daily_outgoing_limit_minor, per_atm_withdrawal_limit_minor,
                daily_atm_withdrawal_limit_minor, daily_atm_transfer_limit_minor,
                daily_debit_purchase_limit_minor, daily_fx_order_notional_limit_minor,
                maximum_active_holds_minor, effective_from, effective_to, version)
            VALUES({Blob(30)}, {Blob(5)}, 1, 0, 0, 0, 1, 1, 1, 1, 0, 'NONE', 1, NULL, 12,
                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, 1);

            INSERT INTO fee_schedule_versions(fee_schedule_version_id, bank_id, effective_from,
                effective_to, version)
            VALUES({Blob(31)}, {Blob(5)}, 1, NULL, 1);

            UPDATE banks
            SET current_policy_version_id = {Blob(30)},
                current_fee_schedule_version_id = {Blob(31)},
                version = version + 1
            WHERE bank_id = {Blob(5)};
            """);

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
            return command.ExecuteScalar() as string ?? string.Empty;
        }

        public async Task<DepositAccountId> OpenAsync(ulong discordUserId, string handle)
        {
            Result<CustomerAccountView> customer = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(GuildId, discordUserId, handle, "利用者"),
                CancellationToken.None);

            Result<AccountOpeningView> opened = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(GuildId, customer.Value.Id, Institution),
                CancellationToken.None);

            return opened.Value.Id;
        }

        public CustomerAccountId CustomerOf(DepositAccountId depositAccountId)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT customer_account_id FROM deposit_accounts WHERE deposit_account_id = $id;";
            command.Parameters.AddWithValue(
                "$id", depositAccountId.Value.ToByteArray());

            return CustomerAccountId.FromValue(
                EntityIdValue.FromBytes((byte[])command.ExecuteScalar()!));
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
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

    private static IdempotencyKey Key(string value) => IdempotencyKey.Create("bank-card", value);

    private static Task<Result<BankCardView>> IssueAsync(
        Harness harness,
        CustomerAccountId customer,
        DepositAccountId account,
        BankCardForm form = BankCardForm.IntegratedCashDebit,
        string key = "card-1") =>
        harness.Cards.IssueBankCardAsync(
            new IssueBankCardCommand(customer, account, form, Key(key)), CancellationToken.None);

    [TestMethod]
    public async Task IssuingAnIntegratedCardCreatesBothCapabilities()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAsync(FirstUser, "taro");
        CustomerAccountId customer = harness.CustomerOf(account);

        Result<BankCardView> result = await IssueAsync(harness, customer, account);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(BankCardForm.IntegratedCashDebit, result.Value.Form);
        Assert.AreEqual(BankCardStatus.Active, result.Value.Status);
        Assert.AreEqual(CashCardStatus.Active, result.Value.CashCardStatus);
        Assert.AreEqual(DebitCardStatus.Active, result.Value.DebitCardStatus);
        Assert.AreEqual(1L, harness.Count("bank_cards"));
        Assert.AreEqual(1L, harness.Count("cash_cards"));
        Assert.AreEqual(1L, harness.Count("debit_cards"));
    }

    [TestMethod]
    public async Task ACashOnlyCardHasNoDebitCapabilityAndNoExpiry()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAsync(FirstUser, "taro");
        CustomerAccountId customer = harness.CustomerOf(account);

        Result<BankCardView> result = await IssueAsync(
            harness, customer, account, BankCardForm.CashOnly);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(result.Value.ExpiresAt);
        Assert.AreEqual(CashCardStatus.Active, result.Value.CashCardStatus);
        Assert.IsNull(result.Value.DebitCardStatus);
        Assert.AreEqual(0L, harness.Count("debit_cards"));
    }

    [TestMethod]
    public async Task ADebitOnlyCardCarriesAnExpiry()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAsync(FirstUser, "taro");
        CustomerAccountId customer = harness.CustomerOf(account);

        Result<BankCardView> result = await IssueAsync(
            harness, customer, account, BankCardForm.DebitOnly);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value.ExpiresAt);
        Assert.IsNull(result.Value.CashCardStatus);
        Assert.AreEqual(DebitCardStatus.Active, result.Value.DebitCardStatus);
        Assert.AreEqual(0L, harness.Count("cash_cards"));
    }

    [TestMethod]
    public async Task ASecondCardForTheSameAccountIsRejected()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAsync(FirstUser, "taro");
        CustomerAccountId customer = harness.CustomerOf(account);

        Assert.IsTrue((await IssueAsync(harness, customer, account)).IsSuccess);

        Result<BankCardView> second = await IssueAsync(
            harness, customer, account, BankCardForm.CashOnly, "card-2");

        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(ErrorCategory.Conflict, second.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.BankCardAlreadyIssued, second.Error.Code);
        Assert.AreEqual(1L, harness.Count("bank_cards"));
    }

    [TestMethod]
    public async Task AnotherCustomerCannotSeeTheCard()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAsync(FirstUser, "taro");
        CustomerAccountId owner = harness.CustomerOf(account);
        DepositAccountId other = await harness.OpenAsync(SecondUser, "jiro");
        CustomerAccountId intruder = harness.CustomerOf(other);

        Assert.IsTrue((await IssueAsync(harness, owner, account)).IsSuccess);

        Result<BankCardView> result = await harness.Cards.GetBankCardAsync(
            new GetBankCardQuery(intruder, account), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
    }

    [TestMethod]
    public async Task LockingTheCardDoesNotCloseItsCapabilities()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAsync(FirstUser, "taro");
        CustomerAccountId customer = harness.CustomerOf(account);
        await IssueAsync(harness, customer, account);

        Result locked = await harness.Cards.SetBankCardLockAsync(
            new SetBankCardLockCommand(customer, account, Locked: true), CancellationToken.None);

        Assert.IsTrue(locked.IsSuccess);
        Assert.AreEqual("LOCKED", harness.ReadText("SELECT status FROM bank_cards;"));
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM cash_cards;"));

        Result unlocked = await harness.Cards.SetBankCardLockAsync(
            new SetBankCardLockCommand(customer, account, Locked: false), CancellationToken.None);

        Assert.IsTrue(unlocked.IsSuccess);
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM bank_cards;"));
    }

    [TestMethod]
    public async Task CapabilitiesLockIndependentlyOfTheCard()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAsync(FirstUser, "taro");
        CustomerAccountId customer = harness.CustomerOf(account);
        await IssueAsync(harness, customer, account);

        Result cash = await harness.Cards.SetCashCardLockAsync(
            new SetCashCardLockCommand(customer, account, Locked: true), CancellationToken.None);

        Assert.IsTrue(cash.IsSuccess);
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM bank_cards;"));
        Assert.AreEqual("LOCKED", harness.ReadText("SELECT status FROM cash_cards;"));
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM debit_cards;"));

        Result debit = await harness.Cards.SetDebitCardLockAsync(
            new SetDebitCardLockCommand(customer, account, Locked: true), CancellationToken.None);

        Assert.IsTrue(debit.IsSuccess);
        Assert.AreEqual("LOCKED", harness.ReadText("SELECT status FROM debit_cards;"));
    }

    [TestMethod]
    public async Task LockingAnAbsentCashCapabilityIsNotFound()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAsync(FirstUser, "taro");
        CustomerAccountId customer = harness.CustomerOf(account);
        await IssueAsync(harness, customer, account, BankCardForm.DebitOnly);

        Result result = await harness.Cards.SetCashCardLockAsync(
            new SetCashCardLockCommand(customer, account, Locked: true), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.CashCardNotFound, result.Error.Code);
    }

    [TestMethod]
    public async Task ReplacementRetiresTheOldCardAndBothCapabilities()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAsync(FirstUser, "taro");
        CustomerAccountId customer = harness.CustomerOf(account);
        Result<BankCardView> first = await IssueAsync(harness, customer, account);

        Result<BankCardView> replaced = await harness.Cards.ReplaceBankCardAsync(
            new ReplaceBankCardCommand(customer, account, Key("replace-1")), CancellationToken.None);

        Assert.IsTrue(replaced.IsSuccess);
        Assert.AreNotEqual(first.Value.BankCardId, replaced.Value.BankCardId);
        Assert.AreEqual(BankCardForm.IntegratedCashDebit, replaced.Value.Form);
        Assert.AreEqual(2L, harness.Count("bank_cards"));
        Assert.AreEqual(2L, harness.Count("cash_cards"));
        Assert.AreEqual(2L, harness.Count("debit_cards"));
        Assert.AreEqual(
            "REPLACED", harness.ReadText("SELECT status FROM bank_cards WHERE status = 'REPLACED';"));
        Assert.AreEqual(
            1L, harness.Count("cash_cards WHERE status = 'CLOSED'"));
        Assert.AreEqual(
            1L, harness.Count("debit_cards WHERE status = 'CLOSED'"));
    }

    [TestMethod]
    public async Task ReplacingWithoutACardIsNotFound()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAsync(FirstUser, "taro");
        CustomerAccountId customer = harness.CustomerOf(account);

        Result<BankCardView> result = await harness.Cards.ReplaceBankCardAsync(
            new ReplaceBankCardCommand(customer, account, Key("replace-1")), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.BankCardNotFound, result.Error.Code);
    }

    [TestMethod]
    public async Task TheRenderedImageIsACanonicalPng()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAsync(FirstUser, "taro");
        CustomerAccountId customer = harness.CustomerOf(account);
        await IssueAsync(harness, customer, account);

        Result<BankCardImage> result = await harness.Cards.RenderBankCardAsync(
            new RenderBankCardCommand(customer, account), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("bank-card.png", result.Value.FileName);
        Assert.AreEqual(1026, result.Value.Width);
        Assert.AreEqual(647, result.Value.Height);
        CollectionAssert.AreEqual(
            new byte[] { 0x89, 0x50, 0x4E, 0x47 }, result.Value.Content.Take(4).ToArray());
    }

    [TestMethod]
    public async Task ACashOnlyCardStillRenders()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAsync(FirstUser, "taro");
        CustomerAccountId customer = harness.CustomerOf(account);
        await IssueAsync(harness, customer, account, BankCardForm.CashOnly);

        Result<BankCardImage> result = await harness.Cards.RenderBankCardAsync(
            new RenderBankCardCommand(customer, account), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value.Content.Length > 0);
    }
}
