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
public sealed class LoanOriginationTests
{
    private const ulong Owner = 810_000_000_000_000_001UL;
    private const ulong Borrower = 810_000_000_000_000_002UL;
    private const ulong Guild = 910UL;
    private const string Institution = "NUM0300";
    private const string Product = "DEMAND01";

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

        public LoanApplicationService Loans { get; private set; } = null!;

        public AuthorizationContext Actor { get; } = new(AuthorizationLevel.SystemOwner, Owner, Guild);

        public static Harness Create(int minimumLiquidityBps = 10000, int lendingCet1Bps = 700)
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-loan", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            new SqliteDatabaseInitializer(
                options, harness.ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_776_000_000_000);
            harness.Seed(minimumLiquidityBps, lendingCet1Bps);

            harness.Coordinator = new SqliteWriteCoordinator(
                harness.ConnectionFactory, new SqliteRetryPolicy(3, 1, static () => 0));
            harness.Coordinator.Start();

            SqliteBankingWriteGateway gateway = new(new FinancialWriteCoordinator(harness.Coordinator));
            SqliteBankingReadGateway read = new(harness.ConnectionFactory);
            SequentialIdGenerator ids = new(30_000);

            harness.Registration = new CustomerAccountApplicationService(
                gateway, read, harness.Clock, ids);
            harness.Accounts = new BankAccountApplicationService(
                gateway,
                new PaymentApplicationService(gateway, read, harness.Clock, ids),
                harness.Clock,
                ids);
            harness.Administration = new BankAdministrationApplicationService(gateway, harness.Clock, ids);
            harness.Loans = new LoanApplicationService(gateway, harness.Clock, ids);

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed(int minimumLiquidityBps, int lendingCet1Bps)
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

                INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                    starts_on, ends_on, status, closed_at, version)
                VALUES({Blob(6)}, {Blob(4)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

                INSERT INTO prudential_policy_versions(prudential_policy_version_id, economy_scope_id,
                    minimum_cet1_bps, lending_cet1_bps, minimum_leverage_bps,
                    configured_warning_leverage_bps, minimum_liquidity_bps,
                    minimum_initial_bank_capital_minor, status, created_at, published_at, retired_at,
                    version)
                VALUES({Blob(5)}, {Blob(1)}, 450, {lendingCet1Bps}, 300, 300, {minimumLiquidityBps},
                    100000, 'PUBLISHED', 1, 1, NULL, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                    account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES({Blob(9)}, {Blob(4)}, NULL, '2901-NMR', 'BASE_MONEY_ISSUANCE_LIABILITY', 'LIABILITY',
                    'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

                INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
                    version, updated_at)
                VALUES({Blob(9)}, 100000000, 0, 1, 1);
                """);
        }

        public void Execute(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public long Scalar(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public async Task<DepositAccountId> OpenOperatingBankAndAccountAsync(long capitalMinor)
        {
            Result<BankView> created = await Administration.CommitCreateBankAsync(
                new CommitCreateBankCommand(
                    Actor,
                    Institution,
                    "ヌメラ銀行",
                    "001",
                    "本店",
                    Product,
                    "普通預金",
                    OpeningEnabled: true,
                    MinimumCustomerAccountAgeDays: 0,
                    MinimumInitialFundingMinor: 0,
                    RequiresManualApproval: false,
                    ReopenClosedAccountAllowed: false,
                    PublicReceivingEnabledDefault: true,
                    SettlementParticipationMode.Direct,
                    SettlementAgentInstitutionCode: null,
                    CentralBankAccountingBookId: null),
                CancellationToken.None);

            Assert.IsTrue(created.IsSuccess, created.Error?.Code);

            Result<BankCapitalView> contributed = await Administration.ContributeBankCapitalAsync(
                new ContributeBankCapitalCommand(Actor, Institution, null, capitalMinor, "loan-capital"),
                CancellationToken.None);

            Assert.IsTrue(contributed.IsSuccess, contributed.Error?.Code);

            Result<BankView> activated = await Administration.ActivateBankAsync(
                new ActivateBankCommand(Actor, Institution, "loan-activate"), CancellationToken.None);

            Assert.IsTrue(activated.IsSuccess, activated.Error?.Code);

            Result<CustomerAccountView> registered = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(Guild, Borrower, "borrower", "借り手"),
                CancellationToken.None);

            Assert.IsTrue(registered.IsSuccess, registered.Error?.Code);
            CustomerAccountId = registered.Value.Id;

            Result<AccountOpeningView> opened = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(Guild, registered.Value.Id, Institution),
                CancellationToken.None);

            Assert.IsTrue(opened.IsSuccess, opened.Error?.Code);
            return opened.Value.Id;
        }

        public CustomerAccountId CustomerAccountId { get; private set; }

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
    public async Task OriginationCreatesTheLoanAssetAndTheDepositTogether()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId deposit = await harness.OpenOperatingBankAndAccountAsync(1_000_000);

        Result<LoanApplicationView> loan = await harness.Loans.ApplyLoanAsync(
            new ApplyLoanCommand(harness.CustomerAccountId, deposit, Institution, Product, 50_000),
            CancellationToken.None);

        Assert.IsTrue(loan.IsSuccess, loan.Error?.Code);
        Assert.AreEqual(LoanContractStatus.Active, loan.Value.Status);
        Assert.AreEqual(50_000L, loan.Value.Principal.Value);
        Assert.AreEqual(1L, harness.Scalar("SELECT COUNT(*) FROM loan_contracts;"));

        Assert.AreEqual(
            50_000L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections p
                JOIN ledger_accounts a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_kind = 'CUSTOMER_LOAN_PRINCIPAL';
                """));

        Assert.AreEqual(
            50_000L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections p
                JOIN deposit_accounts d ON d.ledger_account_id = p.ledger_account_id;
                """));
    }

    [TestMethod]
    public async Task OriginationBeyondTheLendingCet1FloorLeavesNoTrace()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId deposit = await harness.OpenOperatingBankAndAccountAsync(1_000_000);

        Result<LoanApplicationView> loan = await harness.Loans.ApplyLoanAsync(
            new ApplyLoanCommand(harness.CustomerAccountId, deposit, Institution, Product, 20_000_000),
            CancellationToken.None);

        Assert.IsFalse(loan.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.LoanPrudentialFloorUnmet, loan.Error!.Code);
        Assert.AreEqual(0L, harness.Scalar("SELECT COUNT(*) FROM loan_contracts;"));
        Assert.AreEqual(
            0L,
            harness.Scalar("SELECT COUNT(*) FROM ledger_accounts WHERE account_kind = 'CUSTOMER_LOAN_PRINCIPAL';"));
    }

    [TestMethod]
    public async Task TheLiquidityFloorAcceptsTheExactThresholdAndRejectsOneMinorUnitBeyond()
    {
        const long Capital = 1_000_000L;
        const long LargestAdmissiblePrincipal = Capital * 10;

        await using Harness accepted = Harness.Create();
        DepositAccountId acceptedDeposit = await accepted.OpenOperatingBankAndAccountAsync(Capital);

        Result<LoanApplicationView> exact = await accepted.Loans.ApplyLoanAsync(
            new ApplyLoanCommand(
                accepted.CustomerAccountId,
                acceptedDeposit,
                Institution,
                Product,
                LargestAdmissiblePrincipal),
            CancellationToken.None);

        Assert.IsTrue(exact.IsSuccess, exact.Error?.Code);

        await using Harness rejected = Harness.Create();
        DepositAccountId rejectedDeposit = await rejected.OpenOperatingBankAndAccountAsync(Capital);

        Result<LoanApplicationView> tooLarge = await rejected.Loans.ApplyLoanAsync(
            new ApplyLoanCommand(
                rejected.CustomerAccountId,
                rejectedDeposit,
                Institution,
                Product,
                LargestAdmissiblePrincipal + 1),
            CancellationToken.None);

        Assert.IsFalse(tooLarge.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.LoanPrudentialFloorUnmet, tooLarge.Error!.Code);
    }

    [TestMethod]
    public async Task AnUnknownProductIsRejected()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId deposit = await harness.OpenOperatingBankAndAccountAsync(1_000_000);

        Result<LoanApplicationView> loan = await harness.Loans.ApplyLoanAsync(
            new ApplyLoanCommand(harness.CustomerAccountId, deposit, Institution, "UNKNOWN", 1_000),
            CancellationToken.None);

        Assert.IsFalse(loan.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.LoanProductNotFound, loan.Error!.Code);
    }

    [TestMethod]
    public async Task ADepositAccountOfAnotherCustomerIsRejected()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId deposit = await harness.OpenOperatingBankAndAccountAsync(1_000_000);

        Result<LoanApplicationView> loan = await harness.Loans.ApplyLoanAsync(
            new ApplyLoanCommand(
                CustomerAccountId.FromValue(EntityIdValue.FromBits(999)),
                deposit,
                Institution,
                Product,
                1_000),
            CancellationToken.None);

        Assert.IsFalse(loan.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.DepositAccountNotFound, loan.Error!.Code);
    }
}
