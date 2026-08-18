using Microsoft.Data.Sqlite;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Repositories;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Application.Tests;

[TestClass]
public sealed class PaymentPreferenceApplicationTests
{
    private const ulong GuildId = 900UL;

    private const string Institution = "NUM0001";
    private const string OtherInstitution = "NUM0002";
    private const ulong OwnerUser = 720_000_000_000_000_001UL;
    private const ulong OtherUser = 720_000_000_000_000_002UL;

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

        public PaymentApplicationService Payments { get; private set; } = null!;

        public EconomyScopeId Scope { get; } = EconomyScopeId.FromValue(EntityIdValue.FromBits(1));

        public static Harness Create(bool withSecondBank = false)
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-preference", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            new SqliteDatabaseInitializer(
                options, harness.ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_776_000_000_000);
            harness.Seed(withSecondBank);

            harness.Coordinator = new SqliteWriteCoordinator(
                harness.ConnectionFactory, new SqliteRetryPolicy(3, 1, static () => 0));
            harness.Coordinator.Start();

            SqliteBankingWriteGateway gateway = new(new FinancialWriteCoordinator(harness.Coordinator));
            SequentialIdGenerator ids = new(11_000);

            harness.Registration = new CustomerAccountApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);
            harness.Accounts = new BankAccountApplicationService(gateway, harness.Clock, ids);
            harness.Payments = new PaymentApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed(bool withSecondBank)
        {
            Execute($"""
                INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
                VALUES({Blob(1)}, '900', 'Asia/Tokyo', 'ACTIVE', 1);

                INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                    base_money_supply_cap_minor, created_at, retired_at, version)
                VALUES({Blob(2)}, {Blob(1)}, 'ACTIVE', 2, NULL, 1, NULL, 1);
                """);

            SeedBank(3, Institution, 'ヌ');

            if (withSecondBank)
            {
                SeedBank(20, OtherInstitution, '第');
            }
        }

        private void SeedBank(int seed, string institutionCode, char nameLead) => Execute($"""
            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(seed)}, 'BANK', '{nameLead}銀行主体', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status, created_at, version)
            VALUES({Blob(seed + 1)}, {Blob(seed)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

            INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                current_fee_schedule_version_id, created_at, version)
            VALUES({Blob(seed + 2)}, {Blob(1)}, {Blob(seed)}, '{institutionCode}', '{nameLead}銀行', 'NORMAL',
                NULL, 'OPERATING', {Blob(seed + 1)}, NULL, NULL, 1, 1);

            INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at, version)
            VALUES({Blob(seed + 3)}, {Blob(seed + 2)}, '001', '本店', 'ACTIVE', 1, NULL, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES({Blob(seed + 4)}, {Blob(seed + 1)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY',
                'CREDIT', {Blob(2)}, 0, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                version_application_policy, status, created_at, version)
            VALUES({Blob(seed + 5)}, {Blob(seed + 2)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST',
                'ACTIVE', 1, 1);

            INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor, maximum_balance_minor,
                daily_outgoing_limit_minor, per_transaction_limit_minor, transfer_capabilities,
                deposit_insurance_class_code, overdraft_policy, created_at)
            VALUES({Blob(seed + 6)}, {Blob(seed + 5)}, 1, 1, NULL, 1000000000, 'ACTUAL_365_FIXED', 0, NULL,
                NULL, NULL, 'INTERNAL', 'STANDARD', 'NONE', 1);
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
            return command.ExecuteScalar()?.ToString() ?? string.Empty;
        }

        public async Task<CustomerAccountId> RegisterAsync(ulong discordUserId, string handle)
        {
            Result<CustomerAccountView> result = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(GuildId, discordUserId, handle, "利用者"),
                CancellationToken.None);

            return result.Value.Id;
        }

        public async Task<AccountOpeningView> OpenAsync(
            CustomerAccountId customerAccountId,
            string institutionCode = Institution)
        {
            Result<AccountOpeningView> result = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(Scope, customerAccountId, institutionCode),
                CancellationToken.None);

            return result.Value;
        }

        public Task<Result<TransferPreparationView>> PrepareAsync(
            CustomerAccountId payer,
            DepositAccountId source,
            ulong beneficiaryDiscordUserId) =>
            Payments.PrepareTransferToCustomerAsync(
                new PrepareTransferToCustomerQuery(Scope, payer, source, beneficiaryDiscordUserId),
                CancellationToken.None);

        public Task<Result<PaymentPreferenceView>> SetAsync(
            CustomerAccountId customerAccountId,
            DepositAccountId depositAccountId,
            PaymentPreferenceKind kind = PaymentPreferenceKind.DefaultPayment) =>
            Payments.SetPaymentPreferenceAsync(
                new SetPaymentPreferenceCommand(customerAccountId, kind, depositAccountId),
                CancellationToken.None);

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

    [TestMethod]
    public async Task PreparationListsTheBeneficiaryPublicReceivingAccounts()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId payer = await harness.RegisterAsync(OwnerUser, "taro");
        CustomerAccountId payee = await harness.RegisterAsync(OtherUser, "hanako");
        AccountOpeningView source = await harness.OpenAsync(payer);
        AccountOpeningView destination = await harness.OpenAsync(payee);

        Result<TransferPreparationView> result = await harness.PrepareAsync(payer, source.Id, OtherUser);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Candidates.Count);
        Assert.AreEqual(destination.Id, result.Value.Candidates[0].DepositAccountId);
        Assert.AreEqual(Institution, result.Value.Candidates[0].InstitutionCode);
    }

    [TestMethod]
    public async Task PreparationExposesOnlyTheAccountNumberSuffixForDisplay()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId payer = await harness.RegisterAsync(OwnerUser, "taro");
        CustomerAccountId payee = await harness.RegisterAsync(OtherUser, "hanako");
        AccountOpeningView source = await harness.OpenAsync(payer);
        AccountOpeningView destination = await harness.OpenAsync(payee);

        Result<TransferPreparationView> result = await harness.PrepareAsync(payer, source.Id, OtherUser);
        TransferDestinationCandidate candidate = result.Value.Candidates[0];

        Assert.AreEqual(AccountNumber.SuffixLength, candidate.AccountNumberSuffix.Length);
        Assert.IsTrue(destination.AccountNumber.EndsWith(candidate.AccountNumberSuffix, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AccountsThatDoNotPubliclyReceiveAreNotListed()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId payer = await harness.RegisterAsync(OwnerUser, "taro");
        CustomerAccountId payee = await harness.RegisterAsync(OtherUser, "hanako");
        AccountOpeningView source = await harness.OpenAsync(payer);
        AccountOpeningView destination = await harness.OpenAsync(payee);
        harness.Execute($"""
            UPDATE deposit_accounts SET public_receiving_enabled = 0, version = version + 1
            WHERE account_number = '{destination.AccountNumber}';
            """);

        Result<TransferPreparationView> result = await harness.PrepareAsync(payer, source.Id, OtherUser);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.DepositAccountNotFound, result.Error.Code);
    }

    [TestMethod]
    public async Task ClosedBeneficiaryAccountsAreNotListed()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId payer = await harness.RegisterAsync(OwnerUser, "taro");
        CustomerAccountId payee = await harness.RegisterAsync(OtherUser, "hanako");
        AccountOpeningView source = await harness.OpenAsync(payer);
        AccountOpeningView destination = await harness.OpenAsync(payee);
        harness.Execute($"""
            UPDATE deposit_accounts
            SET status = 'CLOSED_USER', closure_reason = 'USER', closed_at = 1, version = version + 1
            WHERE account_number = '{destination.AccountNumber}';
            """);

        Result<TransferPreparationView> result = await harness.PrepareAsync(payer, source.Id, OtherUser);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.DepositAccountNotFound, result.Error!.Code);
    }

    [TestMethod]
    public async Task UnknownDiscordUserIsNotFound()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId payer = await harness.RegisterAsync(OwnerUser, "taro");
        AccountOpeningView source = await harness.OpenAsync(payer);

        Result<TransferPreparationView> result = await harness.PrepareAsync(payer, source.Id, OtherUser);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CustomerAccountNotFound, result.Error!.Code);
    }

    [TestMethod]
    public async Task ForeignSourceAccountIsNormalizedToNotFoundDuringPreparation()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId payer = await harness.RegisterAsync(OwnerUser, "taro");
        CustomerAccountId payee = await harness.RegisterAsync(OtherUser, "hanako");
        AccountOpeningView source = await harness.OpenAsync(payer);
        await harness.OpenAsync(payee);

        Result<TransferPreparationView> result = await harness.PrepareAsync(payee, source.Id, OwnerUser);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.DepositAccountNotFound, result.Error.Code);
    }

    [TestMethod]
    public async Task PreparationNeverListsTheSourceAccountItself()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId payer = await harness.RegisterAsync(OwnerUser, "taro");
        AccountOpeningView source = await harness.OpenAsync(payer);

        Result<TransferPreparationView> result = await harness.PrepareAsync(payer, source.Id, OwnerUser);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.DepositAccountNotFound, result.Error!.Code);
    }

    [TestMethod]
    public async Task PreferenceIsStoredForTheChosenAccount()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId owner = await harness.RegisterAsync(OwnerUser, "taro");
        AccountOpeningView account = await harness.OpenAsync(owner);

        Result<PaymentPreferenceView> result = await harness.SetAsync(owner, account.Id);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentPreferenceKind.DefaultPayment, result.Value.Kind);
        Assert.AreEqual(1L, harness.Count("payment_preferences"));
        Assert.AreEqual(
            "DEFAULT_PAYMENT", harness.ReadText("SELECT preference_kind FROM payment_preferences;"));
    }

    [TestMethod]
    public async Task OnlyTheAccountNumberSuffixIsReturned()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId owner = await harness.RegisterAsync(OwnerUser, "taro");
        AccountOpeningView account = await harness.OpenAsync(owner);

        Result<PaymentPreferenceView> result = await harness.SetAsync(owner, account.Id);

        Assert.AreEqual(AccountNumber.SuffixLength, result.Value.AccountNumberSuffix.Length);
        Assert.IsTrue(account.AccountNumber.EndsWith(result.Value.AccountNumberSuffix, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReselectingTheSameKindUpdatesTheExistingRow()
    {
        await using Harness harness = Harness.Create(withSecondBank: true);
        CustomerAccountId owner = await harness.RegisterAsync(OwnerUser, "taro");
        AccountOpeningView first = await harness.OpenAsync(owner);
        AccountOpeningView second = await harness.OpenAsync(owner, OtherInstitution);

        await harness.SetAsync(owner, first.Id);
        Result<PaymentPreferenceView> result = await harness.SetAsync(owner, second.Id);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1L, harness.Count("payment_preferences"));
        Assert.AreEqual(second.Id, result.Value.DepositAccountId);
    }

    [TestMethod]
    public async Task DifferentKindsAreStoredSideBySide()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId owner = await harness.RegisterAsync(OwnerUser, "taro");
        AccountOpeningView account = await harness.OpenAsync(owner);

        await harness.SetAsync(owner, account.Id, PaymentPreferenceKind.DefaultPayment);
        await harness.SetAsync(owner, account.Id, PaymentPreferenceKind.SalaryReceipt);

        Assert.AreEqual(2L, harness.Count("payment_preferences"));
    }

    [TestMethod]
    public async Task ReselectingClearsAnEarlierDisabledMarker()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId owner = await harness.RegisterAsync(OwnerUser, "taro");
        AccountOpeningView account = await harness.OpenAsync(owner);

        await harness.SetAsync(owner, account.Id);
        harness.Execute("UPDATE payment_preferences SET disabled_at = 1, version = version + 1;");

        Result<PaymentPreferenceView> result = await harness.SetAsync(owner, account.Id);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            string.Empty, harness.ReadText("SELECT CAST(disabled_at AS TEXT) FROM payment_preferences;"));
    }

    [TestMethod]
    public async Task ForeignAccountIsNormalizedToNotFound()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId owner = await harness.RegisterAsync(OwnerUser, "taro");
        CustomerAccountId other = await harness.RegisterAsync(OtherUser, "hanako");
        AccountOpeningView account = await harness.OpenAsync(owner);

        Result<PaymentPreferenceView> result = await harness.SetAsync(other, account.Id);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.DepositAccountNotFound, result.Error.Code);
        Assert.AreEqual(0L, harness.Count("payment_preferences"));
    }

    [TestMethod]
    public async Task FrozenAccountCannotBecomeThePaymentDefault()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId owner = await harness.RegisterAsync(OwnerUser, "taro");
        AccountOpeningView account = await harness.OpenAsync(owner);
        harness.Execute("UPDATE deposit_accounts SET status = 'FROZEN', version = version + 1;");

        Result<PaymentPreferenceView> result = await harness.SetAsync(owner, account.Id);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.AccountRestricted, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.DepositAccountNotOperable, result.Error.Code);
    }

    [TestMethod]
    public async Task DormantAccountCanStillBeChosenForReceipts()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId owner = await harness.RegisterAsync(OwnerUser, "taro");
        AccountOpeningView account = await harness.OpenAsync(owner);
        harness.Execute("UPDATE deposit_accounts SET status = 'DORMANT', version = version + 1;");

        Result<PaymentPreferenceView> result = await harness.SetAsync(
            owner, account.Id, PaymentPreferenceKind.SalaryReceipt);

        Assert.IsTrue(result.IsSuccess);
    }
}
