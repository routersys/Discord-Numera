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
public sealed class BankAdministrationTests
{
    private const ulong Owner = 800_000_000_000_000_001UL;
    private const ulong Guild = 900UL;
    private const ulong Customer = 800_000_000_000_000_002UL;
    private const string Institution = "NUM0100";

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

        public BankAdministrationApplicationService Administration { get; private set; } = null!;

        public EconomyScopeId Scope { get; } = EconomyScopeId.FromValue(EntityIdValue.FromBits(1));

        public AuthorizationContext Actor { get; } =
            new(AuthorizationLevel.SystemOwner, Owner, Guild);

        public AccountingBookId CentralBankBook { get; } =
            AccountingBookId.FromValue(EntityIdValue.FromBits(4));

        public static Harness Create(bool withPrudentialPolicy = true)
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-bank", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            new SqliteDatabaseInitializer(
                options, harness.ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_776_000_000_000);
            harness.Seed(withPrudentialPolicy);

            harness.Coordinator = new SqliteWriteCoordinator(
                harness.ConnectionFactory, new SqliteRetryPolicy(3, 1, static () => 0));
            harness.Coordinator.Start();

            SqliteBankingWriteGateway gateway = new(new FinancialWriteCoordinator(harness.Coordinator));
            SequentialIdGenerator ids = new(20_000);

            harness.Registration = new CustomerAccountApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);
            harness.Accounts = new BankAccountApplicationService(
                gateway,
                new PaymentApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids),
                harness.Clock,
                ids);
            harness.Administration = new BankAdministrationApplicationService(gateway, harness.Clock, ids);

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed(bool withPrudentialPolicy)
        {
            Execute($"""
                INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
                VALUES({Blob(1)}, '{Guild}', 'Asia/Tokyo', 'ACTIVE', 1);

                INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                    base_money_supply_cap_minor, created_at, retired_at, version)
                VALUES({Blob(2)}, {Blob(1)}, 'ACTIVE', 2, NULL, 1, NULL, 1);

                INSERT INTO system_owner_identities(discord_user_id, created_at)
                VALUES('{Owner}', 1);

                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(3)}, 'SYSTEM', '中央銀行', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                    created_at, version)
                VALUES({Blob(4)}, {Blob(3)}, 'CENTRAL_BANK', 'OPEN', 1, 1);
                """);

            if (withPrudentialPolicy)
            {
                Execute($"""
                    INSERT INTO prudential_policy_versions(prudential_policy_version_id, economy_scope_id,
                        minimum_cet1_bps, lending_cet1_bps, minimum_leverage_bps,
                        configured_warning_leverage_bps, minimum_liquidity_bps,
                        minimum_initial_bank_capital_minor, status, created_at, published_at, retired_at,
                        version)
                    VALUES({Blob(5)}, {Blob(1)}, 450, 700, 300, 300, 10000, 100000, 'PUBLISHED', 1, 1, NULL, 1);
                    """);
            }
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
            return command.ExecuteScalar() as string ?? string.Empty;
        }

        public CommitCreateBankCommand CreateCommand(
            string institutionCode = Institution,
            bool requiresManualApproval = false,
            bool openingEnabled = true,
            long minimumInitialFunding = 0,
            int minimumCustomerAccountAgeDays = 0,
            SettlementParticipationMode mode = SettlementParticipationMode.Direct,
            string? agentInstitutionCode = null) =>
            new(
                Actor,
                institutionCode,
                "ヌメラ銀行",
                "001",
                "本店",
                "DEMAND01",
                "普通預金",
                openingEnabled,
                minimumCustomerAccountAgeDays,
                minimumInitialFunding,
                requiresManualApproval,
                ReopenClosedAccountAllowed: false,
                PublicReceivingEnabledDefault: true,
                mode,
                agentInstitutionCode,
                mode == SettlementParticipationMode.Direct ? CentralBankBook : null);

        public Task<Result<BankView>> CreateBankAsync(CommitCreateBankCommand command) =>
            Administration.CommitCreateBankAsync(command, CancellationToken.None);

        public async Task<BankView> CreateOperatingBankAsync(CommitCreateBankCommand command)
        {
            Result<BankView> created = await CreateBankAsync(command);
            Assert.IsTrue(created.IsSuccess);

            Execute($"""
                UPDATE banks SET status = 'OPERATING', version = version + 1
                WHERE bank_id = x'{Convert.ToHexString(created.Value.Id.Value.ToByteArray())}';

                UPDATE settlement_participations SET status = 'ACTIVE', version = version + 1
                WHERE bank_id = x'{Convert.ToHexString(created.Value.Id.Value.ToByteArray())}';
                """);

            return created.Value;
        }

        public void OpenAccountingPeriods() => Execute("""
            INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                starts_on, ends_on, status, closed_at, version)
            SELECT randomblob(16), accounting_book_id, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1
            FROM accounting_books
            WHERE NOT EXISTS(
                SELECT 1 FROM accounting_periods p
                WHERE p.accounting_book_id = accounting_books.accounting_book_id);
            """);

        public void FundDeposit(DepositAccountId depositAccountId, long amount) => Execute($"""
            INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
                version, updated_at)
            SELECT ledger_account_id, {amount}, 0, 1, 1 FROM deposit_accounts
            WHERE deposit_account_id = x'{Convert.ToHexString(depositAccountId.Value.ToByteArray())}'
            ON CONFLICT(ledger_account_id) DO UPDATE SET
                posted_balance_minor = {amount}, version = version + 1;
            """);

        public void FundReserves(long amount) => Execute($"""
            INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
                version, updated_at)
            SELECT ledger_account_id, {amount}, 0, 1, 1 FROM ledger_accounts
            WHERE account_kind IN ('CENTRAL_BANK_RESERVE_ASSET', 'CENTRAL_BANK_SETTLEMENT_LIABILITY')
            ON CONFLICT(ledger_account_id) DO UPDATE SET
                posted_balance_minor = {amount}, version = version + 1;
            """);

        public async Task<CustomerAccountId> RegisterAsync(ulong discordUserId, string handle)
        {
            Result<CustomerAccountView> result = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(Guild, discordUserId, handle, "利用者"),
                CancellationToken.None);

            return result.Value.Id;
        }

        public Task<Result<AccountOpeningView>> OpenAsync(
            CustomerAccountId customerAccountId,
            string institutionCode = Institution) =>
            Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(Guild, customerAccountId, institutionCode),
                CancellationToken.None);

        public AccountOpeningApplicationId PendingApplicationId()
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT account_opening_application_id FROM account_opening_applications
                WHERE status = 'SUBMITTED';
                """;

            return AccountOpeningApplicationId.FromValue(
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

    [TestMethod]
    public async Task BankCreationPublishesEveryRequiredEntityInOneCommit()
    {
        await using Harness harness = Harness.Create();

        Result<BankView> created = await harness.CreateBankAsync(harness.CreateCommand());

        Assert.IsTrue(created.IsSuccess);
        Assert.AreEqual(BankStatus.PendingActivation, created.Value.Status);
        Assert.AreEqual(1L, harness.Count("banks"));
        Assert.AreEqual(1L, harness.Count("branches"));
        Assert.AreEqual(1L, harness.Count("account_products"));
        Assert.AreEqual(1L, harness.Count("account_product_versions"));
        Assert.AreEqual(1L, harness.Count("bank_policy_versions"));
        Assert.AreEqual(1L, harness.Count("fee_schedule_versions"));
        Assert.AreEqual(1L, harness.Count("settlement_participations"));
        Assert.AreEqual(1L, harness.Count("central_bank_settlement_accounts"));
        Assert.AreEqual(1L, harness.Count("audit_records"));
        Assert.IsGreaterThan(0L, harness.Count("fee_rules"));
    }

    [TestMethod]
    public async Task NewBankStartsPendingActivation()
    {
        await using Harness harness = Harness.Create();

        await harness.CreateBankAsync(harness.CreateCommand());

        Assert.AreEqual("PENDING_ACTIVATION", harness.ReadText("SELECT status FROM banks;"));
    }

    [TestMethod]
    public async Task EconomyWithoutPublishedPrudentialPolicyRejectsBankCreation()
    {
        await using Harness harness = Harness.Create(withPrudentialPolicy: false);

        Result<BankView> created = await harness.CreateBankAsync(harness.CreateCommand());

        Assert.IsFalse(created.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.PrudentialPolicyUnavailable, created.Error!.Code);
        Assert.AreEqual(0L, harness.Count("banks"));
    }

    [TestMethod]
    public async Task DuplicateInstitutionCodeIsRejected()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateBankAsync(harness.CreateCommand());

        Result<BankView> second = await harness.CreateBankAsync(harness.CreateCommand());

        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.BankAlreadyExists, second.Error!.Code);
        Assert.AreEqual(1L, harness.Count("banks"));
    }

    [TestMethod]
    public async Task MalformedIdentityIsRejected()
    {
        await using Harness harness = Harness.Create();

        Result<BankView> created = await harness.CreateBankAsync(
            harness.CreateCommand(institutionCode: "ab"));

        Assert.IsFalse(created.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.BankIdentityInvalid, created.Error!.Code);
    }

    [TestMethod]
    public async Task NonOwnerCannotCreateABank()
    {
        await using Harness harness = Harness.Create();

        CommitCreateBankCommand command = harness.CreateCommand() with
        {
            Actor = new AuthorizationContext(AuthorizationLevel.Customer, Customer, Guild),
        };

        Result<BankView> created = await harness.CreateBankAsync(command);

        Assert.IsFalse(created.IsSuccess);
        Assert.AreEqual(ErrorCategory.Forbidden, created.Error!.Category);
        Assert.AreEqual(0L, harness.Count("banks"));
    }

    [TestMethod]
    public async Task NonzeroInitialFundingWithoutAnyFundingRailIsRejected()
    {
        await using Harness harness = Harness.Create();

        Result<BankView> created = await harness.CreateBankAsync(
            harness.CreateCommand(minimumInitialFunding: 500));

        Assert.IsFalse(created.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.OpeningFundingSourceUnavailable, created.Error!.Code);
        Assert.AreEqual(0L, harness.Count("banks"));
    }

    [TestMethod]
    public async Task IndirectParticipationRequiresAnExistingAgentBank()
    {
        await using Harness harness = Harness.Create();

        Result<BankView> created = await harness.CreateBankAsync(harness.CreateCommand(
            mode: SettlementParticipationMode.Indirect, agentInstitutionCode: "NUM9999"));

        Assert.IsFalse(created.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.SettlementAgentBankNotFound, created.Error!.Code);
        Assert.AreEqual(0L, harness.Count("banks"));
    }

    [TestMethod]
    public async Task AutomaticOpeningCompletesTheApplicationInTheSameCommit()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand());
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");

        Result<AccountOpeningView> opened = await harness.OpenAsync(customer);

        Assert.IsTrue(opened.IsSuccess);
        Assert.AreEqual(DepositAccountStatus.Active, opened.Value.Status);
        Assert.AreEqual(1L, harness.Count("account_opening_applications"));
        Assert.AreEqual("COMPLETED", harness.ReadText("SELECT status FROM account_opening_applications;"));
        Assert.AreEqual("AUTOMATIC", harness.ReadText("SELECT decision_mode FROM account_opening_applications;"));
    }

    [TestMethod]
    public async Task ManualApprovalDoesNotCreateADepositAccount()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand(requiresManualApproval: true));
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");

        Result<AccountOpeningView> opened = await harness.OpenAsync(customer);

        Assert.IsTrue(opened.IsSuccess);
        Assert.AreEqual(0L, harness.Count("deposit_accounts"));
        Assert.AreEqual("SUBMITTED", harness.ReadText("SELECT status FROM account_opening_applications;"));
        Assert.AreEqual("MANUAL", harness.ReadText("SELECT decision_mode FROM account_opening_applications;"));
    }

    [TestMethod]
    public async Task ApprovalCreatesTheDepositAccount()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand(requiresManualApproval: true));
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");
        await harness.OpenAsync(customer);

        Result<AccountOpeningApplicationView> approved =
            await harness.Administration.ApproveAccountOpeningAsync(
                new ApproveAccountOpeningCommand(harness.Actor, harness.PendingApplicationId()),
                CancellationToken.None);

        Assert.IsTrue(approved.IsSuccess);
        Assert.AreEqual(AccountOpeningApplicationStatus.Completed, approved.Value.Status);
        Assert.AreEqual(1L, harness.Count("deposit_accounts"));
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM deposit_accounts;"));
        Assert.AreEqual(
            "800000000000000001",
            harness.ReadText("SELECT decided_by_discord_user_id FROM account_opening_applications;"));
    }

    [TestMethod]
    public async Task RejectionLeavesNoDepositAccount()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand(requiresManualApproval: true));
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");
        await harness.OpenAsync(customer);

        Result<AccountOpeningApplicationView> rejected =
            await harness.Administration.RejectAccountOpeningAsync(
                new RejectAccountOpeningCommand(harness.Actor, harness.PendingApplicationId(), "POLICY"),
                CancellationToken.None);

        Assert.IsTrue(rejected.IsSuccess);
        Assert.AreEqual(AccountOpeningApplicationStatus.Rejected, rejected.Value.Status);
        Assert.AreEqual(0L, harness.Count("deposit_accounts"));
        Assert.AreEqual(
            "POLICY",
            harness.ReadText("SELECT reason FROM audit_records WHERE action = 'ACCOUNT_OPENING_REJECT';"));
    }

    [TestMethod]
    public async Task RejectedApplicationCannotBeApproved()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand(requiresManualApproval: true));
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");
        await harness.OpenAsync(customer);
        AccountOpeningApplicationId applicationId = harness.PendingApplicationId();

        await harness.Administration.RejectAccountOpeningAsync(
            new RejectAccountOpeningCommand(harness.Actor, applicationId, "POLICY"), CancellationToken.None);

        Result<AccountOpeningApplicationView> approved =
            await harness.Administration.ApproveAccountOpeningAsync(
                new ApproveAccountOpeningCommand(harness.Actor, applicationId), CancellationToken.None);

        Assert.IsFalse(approved.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AccountOpeningApplicationNotSubmitted, approved.Error!.Code);
    }

    [TestMethod]
    public async Task ApprovalRequiresManagementAuthority()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand(requiresManualApproval: true));
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");
        await harness.OpenAsync(customer);

        Result<AccountOpeningApplicationView> approved =
            await harness.Administration.ApproveAccountOpeningAsync(
                new ApproveAccountOpeningCommand(
                    new AuthorizationContext(AuthorizationLevel.Customer, Customer, Guild),
                    harness.PendingApplicationId()),
                CancellationToken.None);

        Assert.IsFalse(approved.IsSuccess);
        Assert.AreEqual(ErrorCategory.Forbidden, approved.Error!.Category);
        Assert.AreEqual(0L, harness.Count("deposit_accounts"));
    }

    [TestMethod]
    public async Task UnknownApplicationIsNotFound()
    {
        await using Harness harness = Harness.Create();

        Result<AccountOpeningApplicationView> approved =
            await harness.Administration.ApproveAccountOpeningAsync(
                new ApproveAccountOpeningCommand(
                    harness.Actor, AccountOpeningApplicationId.FromValue(EntityIdValue.FromBits(999))),
                CancellationToken.None);

        Assert.IsFalse(approved.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AccountOpeningApplicationNotFound, approved.Error!.Code);
    }

    [TestMethod]
    public async Task DisabledOpeningPolicyRejectsTheRequest()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand(openingEnabled: false));
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");

        Result<AccountOpeningView> opened = await harness.OpenAsync(customer);

        Assert.IsFalse(opened.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AccountOpeningDisabled, opened.Error!.Code);
        Assert.AreEqual(0L, harness.Count("account_opening_applications"));
    }

    [TestMethod]
    public async Task CustomerBelowTheMinimumAgeIsRejected()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand(minimumCustomerAccountAgeDays: 7));
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");

        Result<AccountOpeningView> opened = await harness.OpenAsync(customer);

        Assert.IsFalse(opened.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CustomerAccountTooNew, opened.Error!.Code);
    }

    [TestMethod]
    public async Task CustomerReachingTheMinimumAgeIsAccepted()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand(minimumCustomerAccountAgeDays: 7));
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");
        harness.Clock.Advance(7L * 86_400_000);

        Result<AccountOpeningView> opened = await harness.OpenAsync(customer);

        Assert.IsTrue(opened.IsSuccess);
        Assert.AreEqual(DepositAccountStatus.Active, opened.Value.Status);
    }

    [TestMethod]
    public async Task SubmittedApplicationBlocksASecondRequest()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand(requiresManualApproval: true));
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");
        await harness.OpenAsync(customer);

        Result<AccountOpeningView> second = await harness.OpenAsync(customer);

        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AccountOpeningApplicationAlreadyPending, second.Error!.Code);
        Assert.AreEqual(1L, harness.Count("account_opening_applications"));
    }

    [TestMethod]
    public async Task NonzeroInitialFundingWithoutACustomerFundingAccountIsRejected()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand());
        await harness.CreateOperatingBankAsync(
            harness.CreateCommand(institutionCode: "NUM0200", minimumInitialFunding: 500));
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");

        Result<AccountOpeningView> opened = await harness.OpenAsync(customer, "NUM0200");

        Assert.IsFalse(opened.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.OpeningFundingSourceUnavailable, opened.Error!.Code);
        Assert.AreEqual(0L, harness.Count("account_opening_applications"));
    }

    [TestMethod]
    public async Task NonzeroInitialFundingIsRejectedWhenTheRailHasNoBalance()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand());
        await harness.CreateOperatingBankAsync(
            harness.CreateCommand(institutionCode: "NUM0200", minimumInitialFunding: 500));
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");
        await harness.OpenAsync(customer);
        harness.OpenAccountingPeriods();

        Result<AccountOpeningView> opened = await harness.OpenAsync(customer, "NUM0200");

        Assert.IsFalse(opened.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AvailableBalanceInsufficient, opened.Error!.Code);
        Assert.AreEqual(0L, harness.Count("account_opening_applications WHERE required_funding_minor = 500"));
    }

    [TestMethod]
    public async Task NonzeroInitialFundingCompletesThroughTheFundingRail()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand());
        await harness.CreateOperatingBankAsync(
            harness.CreateCommand(institutionCode: "NUM0200", minimumInitialFunding: 500));
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");
        Result<AccountOpeningView> rail = await harness.OpenAsync(customer);

        harness.OpenAccountingPeriods();
        harness.FundDeposit(rail.Value.Id, 5_000);
        harness.FundReserves(50_000);

        Result<AccountOpeningView> opened = await harness.OpenAsync(customer, "NUM0200");

        Assert.IsTrue(opened.IsSuccess, opened.Error?.Code);
        Assert.AreEqual(DepositAccountStatus.Active, opened.Value.Status);
        Assert.AreEqual(500L, opened.Value.PostedBalance.Value);
        Assert.AreEqual(
            "COMPLETED",
            harness.ReadText("""
                SELECT status FROM account_opening_applications WHERE required_funding_minor = 500;
                """));
        Assert.AreEqual(
            1L,
            harness.Count("""
                account_opening_applications WHERE funding_payment_order_id IS NOT NULL
                """));
    }

    [TestMethod]
    public async Task OpeningSnapshotsThePolicyAndFeeSchedule()
    {
        await using Harness harness = Harness.Create();
        BankView bank = await harness.CreateOperatingBankAsync(harness.CreateCommand());
        CustomerAccountId customer = await harness.RegisterAsync(Customer, "taro");
        await harness.OpenAsync(customer);

        string policy = harness.ReadText("""
            SELECT hex(policy_version_id) FROM account_opening_applications;
            """);

        Assert.AreEqual(Convert.ToHexString(bank.PolicyVersionId.Value.ToByteArray()), policy);
    }

    [TestMethod]
    public async Task StartingTheWizardReturnsTheCanonicalStepOrder()
    {
        await using Harness harness = Harness.Create();

        Result<BankDraftView> result = await harness.Administration.StartCreateBankAsync(
            new StartCreateBankCommand(harness.Actor, Institution), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(Institution, result.Value.InstitutionCode);
        Assert.AreEqual("IDENTITY", result.Value.Steps[0]);
        Assert.AreEqual("COMMIT", result.Value.Steps[^1]);
        Assert.AreEqual(12, result.Value.Steps.Count);
    }

    [TestMethod]
    public async Task StartingTheWizardForAnExistingCodeIsRejected()
    {
        await using Harness harness = Harness.Create();
        Assert.IsTrue((await harness.CreateBankAsync(harness.CreateCommand())).IsSuccess);

        Result<BankDraftView> result = await harness.Administration.StartCreateBankAsync(
            new StartCreateBankCommand(harness.Actor, Institution), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Conflict, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.BankAlreadyExists, result.Error.Code);
    }

    [TestMethod]
    public async Task ACustomerCannotStartTheWizard()
    {
        await using Harness harness = Harness.Create();

        Result<BankDraftView> result = await harness.Administration.StartCreateBankAsync(
            new StartCreateBankCommand(
                new AuthorizationContext(AuthorizationLevel.Customer, Customer, Guild), Institution),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Forbidden, result.Error!.Category);
    }

    [TestMethod]
    public async Task UpdatingThePolicyPublishesANewImmutableVersion()
    {
        await using Harness harness = Harness.Create();
        BankView bank = await harness.CreateOperatingBankAsync(harness.CreateCommand());
        long before = harness.Count("bank_policy_versions");

        Result<BankView> result = await harness.Administration.UpdateBankPolicyAsync(
            new UpdateBankPolicyCommand(
                harness.Actor, Institution, ExpectedBankVersion(harness, bank),
                OpeningEnabled: false, 30, 1_000, true, true, false),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(before + 1, harness.Count("bank_policy_versions"));
        Assert.AreEqual(
            "0",
            harness.ReadText("""
                SELECT CAST(p.opening_enabled AS TEXT) FROM bank_policy_versions p
                INNER JOIN banks b ON b.current_policy_version_id = p.bank_policy_version_id;
                """));
    }

    [TestMethod]
    public async Task UpdatingThePolicyWithAStaleVersionConflicts()
    {
        await using Harness harness = Harness.Create();
        BankView bank = await harness.CreateOperatingBankAsync(harness.CreateCommand());

        Result<BankView> result = await harness.Administration.UpdateBankPolicyAsync(
            new UpdateBankPolicyCommand(
                harness.Actor, Institution, ExpectedBankVersion(harness, bank) - 1,
                OpeningEnabled: false, 0, 0, false, false, true),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.ConcurrencyConflict, result.Error!.Category);
    }

    [TestMethod]
    public async Task AnOperatingBankCannotBeRetiredDirectly()
    {
        await using Harness harness = Harness.Create();
        await harness.CreateOperatingBankAsync(harness.CreateCommand());

        Result<BankView> result = await harness.Administration.RetireBankAsync(
            new RetireBankCommand(harness.Actor, Institution), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Conflict, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.BankNotRetirable, result.Error.Code);
    }

    [TestMethod]
    public async Task ARestrictedBankWithoutCustomersEntersClosing()
    {
        await using Harness harness = Harness.Create();
        BankView bank = await harness.CreateOperatingBankAsync(harness.CreateCommand());

        harness.Execute($"""
            UPDATE banks SET status = 'RESTRICTED', version = version + 1
            WHERE bank_id = x'{Convert.ToHexString(bank.Id.Value.ToByteArray())}';
            """);

        Result<BankView> result = await harness.Administration.RetireBankAsync(
            new RetireBankCommand(harness.Actor, Institution), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(BankStatus.Closing, result.Value.Status);
        Assert.AreEqual("CLOSING", harness.ReadText("SELECT status FROM banks;"));
    }

    private static long ExpectedBankVersion(Harness harness, BankView bank) =>
        long.Parse(
            harness.ReadText($"""
                SELECT CAST(version AS TEXT) FROM banks
                WHERE bank_id = x'{Convert.ToHexString(bank.Id.Value.ToByteArray())}';
                """),
            System.Globalization.CultureInfo.InvariantCulture);
}
