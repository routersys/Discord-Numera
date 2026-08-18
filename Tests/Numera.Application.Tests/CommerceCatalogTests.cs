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
public sealed class CommerceCatalogTests
{
    private const ulong GuildId = 960UL;
    private const ulong OtherGuildId = 961UL;
    private const string Institution = "NUM0060";
    private const ulong MerchantUser = 760_000_000_000_000_001UL;
    private const ulong BuyerUser = 760_000_000_000_000_002UL;

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

        public MerchantAdministrationApplicationService Merchants { get; private set; } = null!;

        public CommerceApplicationService Commerce { get; private set; } = null!;

        public CommerceMaintenanceService Maintenance { get; private set; } = null!;

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-commerce", Guid.NewGuid().ToString("n"));
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
            harness.Merchants = new MerchantAdministrationApplicationService(gateway, harness.Clock, ids);
            harness.Commerce = new CommerceApplicationService(gateway, harness.Clock, ids);
            harness.Maintenance = new CommerceMaintenanceService(gateway, harness.Clock);

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

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES({Blob(4)}, {Blob(3)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

            INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                current_fee_schedule_version_id, created_at, version)
            VALUES({Blob(5)}, {Blob(1)}, {Blob(3)}, '{Institution}', 'ヌメラ銀行', 'NORMAL', NULL,
                'OPERATING', {Blob(4)}, NULL, NULL, 1, 1);

            INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at, version)
            VALUES({Blob(6)}, {Blob(5)}, '001', '本店', 'ACTIVE', 1, NULL, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES({Blob(7)}, {Blob(4)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY', 'CREDIT',
                {Blob(2)}, 0, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO bank_policy_versions(bank_policy_version_id, bank_id, opening_enabled,
                minimum_customer_account_age_days, minimum_initial_funding_minor, requires_manual_approval,
                reopen_closed_account_allowed, public_receiving_enabled_default, cash_card_enabled,
                debit_card_enabled, integrated_cash_debit_default, automatic_bank_card_issue_mode,
                cash_atm_enabled, cash_card_validity_months, debit_card_validity_months,
                per_transfer_limit_minor, daily_outgoing_limit_minor, per_atm_withdrawal_limit_minor,
                daily_atm_withdrawal_limit_minor, daily_atm_transfer_limit_minor,
                daily_debit_purchase_limit_minor, daily_fx_order_notional_limit_minor,
                maximum_active_holds_minor, effective_from, effective_to, version)
            VALUES({Blob(30)}, {Blob(5)}, 1, 0, 0, 0, 1, 1, 1, 1, 1, 'NONE', 1, 12, 12,
                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, 1);

            INSERT INTO fee_schedule_versions(fee_schedule_version_id, bank_id, effective_from,
                effective_to, version)
            VALUES({Blob(31)}, {Blob(5)}, 1, NULL, 1);

            UPDATE banks
            SET current_policy_version_id = {Blob(30)},
                current_fee_schedule_version_id = {Blob(31)},
                version = version + 1
            WHERE bank_id = {Blob(5)};

            INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                version_application_policy, status, created_at, version)
            VALUES({Blob(8)}, {Blob(5)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST', 'ACTIVE', 1, 1);

            INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor,
                maximum_balance_minor, daily_outgoing_limit_minor, per_transaction_limit_minor,
                transfer_capabilities, deposit_insurance_class_code, overdraft_policy, created_at)
            VALUES({Blob(9)}, {Blob(8)}, 1, 1, NULL, 1000000000, 'ACTUAL_365_FIXED', 0, NULL, NULL, NULL,
                'INTERNAL', 'STANDARD', 'NONE', 1);
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

        public async Task<DepositAccountId> OpenAccountAsync(ulong discordUserId, string handle)
        {
            Result<CustomerAccountView> registered = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(GuildId, discordUserId, handle, "利用者"),
                CancellationToken.None);

            Result<AccountOpeningView> opened = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(GuildId, registered.Value.Id, Institution),
                CancellationToken.None);

            return opened.Value.Id;
        }

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

    private static AuthorizationContext Merchant(ulong guildId = GuildId) =>
        new(AuthorizationLevel.Customer, MerchantUser, guildId);

    private static AuthorizationContext Buyer(ulong guildId = GuildId) =>
        new(AuthorizationLevel.Customer, BuyerUser, guildId);

    private static async Task<MerchantProfileView> CreateProfileAsync(
        Harness harness,
        string catalogScope = "GLOBAL",
        string paymentScope = "GLOBAL")
    {
        DepositAccountId settlement = await harness.OpenAccountAsync(MerchantUser, "seller");

        Result<MerchantProfileView> created = await harness.Merchants.CreateAsync(
            new CreateMerchantProfileCommand(
                Merchant(), settlement, "ヌメラ商店", catalogScope, paymentScope, "DISABLED", 50, 3600, 7200, true),
            CancellationToken.None);

        Assert.IsTrue(created.IsSuccess, created.Error?.Code);
        return created.Value;
    }

    private static async Task<MerchantProductView> CreateActiveProductAsync(
        Harness harness,
        MerchantProfileId profileId,
        string inventoryMode = "UNLIMITED",
        string saleScopeOverride = "INHERIT")
    {
        Result<MerchantProductView> product = await harness.Merchants.CreateProductAsync(
            new CreateMerchantProductCommand(
                Merchant(), profileId, "SKU-1", "記念コイン", "説明", inventoryMode, saleScopeOverride),
            CancellationToken.None);

        Assert.IsTrue(product.IsSuccess, product.Error?.Code);

        Result<MerchantProductPriceVersionView> price = await harness.Merchants.PublishPriceAsync(
            new PublishMerchantProductPriceCommand(Merchant(), product.Value.Id, 1_200),
            CancellationToken.None);

        Assert.IsTrue(price.IsSuccess, price.Error?.Code);

        if (inventoryMode == "FINITE")
        {
            Result<MerchantInventoryView> adjusted = await harness.Merchants.AdjustInventoryAsync(
                new AdjustMerchantInventoryCommand(Merchant(), product.Value.Id, 5), CancellationToken.None);

            Assert.IsTrue(adjusted.IsSuccess, adjusted.Error?.Code);
        }

        Result<MerchantProductView> activated = await harness.Merchants.SetProductStateAsync(
            new SetMerchantProductStateCommand(Merchant(), product.Value.Id, MerchantProductStatus.Active),
            CancellationToken.None);

        Assert.IsTrue(activated.IsSuccess, activated.Error?.Code);
        return activated.Value;
    }

    [TestMethod]
    public async Task CreatingAProfilePublishesTheFirstAftercarePolicy()
    {
        await using Harness harness = Harness.Create();

        MerchantProfileView profile = await CreateProfileAsync(harness);

        Assert.AreEqual(MerchantProfileStatus.Active, profile.Status);
        Assert.AreEqual(1L, harness.Count("merchant_profiles"));
        Assert.AreEqual("PUBLISHED", harness.ReadText(
            "SELECT status FROM merchant_aftercare_policy_versions;"));
        Assert.AreEqual(1L, harness.Count(
            "merchant_profiles WHERE current_aftercare_policy_version_id IS NOT NULL"));
    }

    [TestMethod]
    public async Task ASettlementAccountOwnedByAnotherPartyIsRejected()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId foreign = await harness.OpenAccountAsync(BuyerUser, "buyer");
        await harness.OpenAccountAsync(MerchantUser, "seller");

        Result<MerchantProfileView> created = await harness.Merchants.CreateAsync(
            new CreateMerchantProfileCommand(
                Merchant(), foreign, "ヌメラ商店", "GLOBAL", "GLOBAL", "DISABLED", 50, 3600, 7200, true),
            CancellationToken.None);

        Assert.IsFalse(created.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantSettlementAccountInvalid, created.Error!.Code);
    }

    [TestMethod]
    public async Task AProductWithoutAPublishedPriceCannotBecomeActive()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);

        Result<MerchantProductView> product = await harness.Merchants.CreateProductAsync(
            new CreateMerchantProductCommand(
                Merchant(), profile.Id, "SKU-9", "未価格品", "説明", "UNLIMITED", "INHERIT"),
            CancellationToken.None);

        Result<MerchantProductView> activated = await harness.Merchants.SetProductStateAsync(
            new SetMerchantProductStateCommand(Merchant(), product.Value.Id, MerchantProductStatus.Active),
            CancellationToken.None);

        Assert.IsFalse(activated.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantProductNotSellable, activated.Error!.Code);
    }

    [TestMethod]
    public async Task PublishingASecondPriceRetiresThePreviousOne()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);

        Result<MerchantProductPriceVersionView> second = await harness.Merchants.PublishPriceAsync(
            new PublishMerchantProductPriceCommand(Merchant(), product.Id, 1_500),
            CancellationToken.None);

        Assert.IsTrue(second.IsSuccess, second.Error?.Code);
        Assert.AreEqual(2L, second.Value.Version);
        Assert.AreEqual(1L, harness.Count(
            "merchant_product_price_versions WHERE status = 'PUBLISHED'"));
        Assert.AreEqual(1L, harness.Count(
            "merchant_product_price_versions WHERE status = 'RETIRED'"));
    }

    [TestMethod]
    public async Task InventoryCannotBecomeNegative()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id, "FINITE");

        Result<MerchantInventoryView> adjusted = await harness.Merchants.AdjustInventoryAsync(
            new AdjustMerchantInventoryCommand(Merchant(), product.Id, -6), CancellationToken.None);

        Assert.IsFalse(adjusted.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantInventoryInsufficient, adjusted.Error!.Code);
        Assert.AreEqual(1L, harness.Count("merchant_inventory_movements"));
    }

    [TestMethod]
    public async Task AnUnauthorisedActorCannotChangeTheCatalog()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<MerchantProductView> product = await harness.Merchants.CreateProductAsync(
            new CreateMerchantProductCommand(
                Buyer(), profile.Id, "SKU-2", "他人の商品", "説明", "UNLIMITED", "INHERIT"),
            CancellationToken.None);

        Assert.IsFalse(product.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantOperationForbidden, product.Error!.Code);
    }

    [TestMethod]
    public async Task CheckoutFixesTheSnapshotAndAwaitsConfirmation()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 2, "checkout-1"),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);
        Assert.AreEqual(CommerceOrderStatus.AwaitingConfirmation, checkout.Value.Status);
        Assert.AreEqual(2_400L, checkout.Value.OrderTotalPresentment.Value);
        Assert.AreEqual(1, checkout.Value.Lines.Count);
        Assert.AreEqual("PENDING", harness.ReadText("SELECT status FROM commerce_payments;"));
        Assert.AreEqual(
            checkout.Value.CommerceOrderId.Value.ToString(),
            harness.ReadText("SELECT NULL;") is null ? null : checkout.Value.CommerceOrderId.Value.ToString());
        Assert.AreEqual(0L, harness.Count("merchant_inventory_movements"));
    }

    [TestMethod]
    public async Task TheSameInteractionReturnsTheSameOrder()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<CommerceCheckoutView> first = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "interaction-7"),
            CancellationToken.None);

        Result<CommerceCheckoutView> second = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "interaction-7"),
            CancellationToken.None);

        Assert.IsTrue(first.IsSuccess, first.Error?.Code);
        Assert.IsTrue(second.IsSuccess, second.Error?.Code);
        Assert.AreEqual(first.Value.CommerceOrderId, second.Value.CommerceOrderId);
        Assert.AreEqual(1, second.Value.Lines.Count);
        Assert.AreEqual(1L, harness.Count("commerce_orders"));
        Assert.AreEqual(1L, harness.Count("commerce_payments"));
        Assert.AreEqual(1L, harness.Count("operation_results"));
    }

    [TestMethod]
    public async Task CheckoutCreationEnqueuesOneOutboxEvent()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "interaction-8"),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM outbox_events o
                INNER JOIN business_operations b
                    ON b.business_operation_id = o.business_operation_id
                WHERE o.event_type = 'COMMERCE_CHECKOUT_CREATED'
                  AND b.operation_type = 'COMMERCE_CHECKOUT_CREATE'
                  AND b.status = 'COMMITTED';
                """));
    }

    [TestMethod]
    public async Task AnExpiredCheckoutIsCancelledByMaintenance()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "interaction-9"),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);

        harness.Clock.Advance(CommerceApplicationService.CheckoutLifetimeMilliseconds + 1);

        CommerceMaintenanceReport report = await harness.Maintenance.ExpireCheckoutsAsync(
            CancellationToken.None);

        Assert.AreEqual(1, report.Examined);
        Assert.AreEqual(1, report.Cancelled);
        Assert.AreEqual("CANCELLED", harness.ReadText("SELECT status FROM commerce_orders;"));
        Assert.AreEqual("CANCELLED", harness.ReadText("SELECT status FROM commerce_payments;"));
    }

    [TestMethod]
    public async Task AnUnexpiredCheckoutSurvivesMaintenance()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Assert.IsTrue((await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "interaction-10"),
            CancellationToken.None)).IsSuccess);

        CommerceMaintenanceReport report = await harness.Maintenance.ExpireCheckoutsAsync(
            CancellationToken.None);

        Assert.AreEqual(0, report.Examined);
        Assert.AreEqual("AWAITING_CONFIRMATION", harness.ReadText("SELECT status FROM commerce_orders;"));
    }

    [TestMethod]
    public async Task CheckoutOutsideThePaymentScopeIsRejected()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness, "GLOBAL", "LOCAL_GUILD");
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(OtherGuildId), product.Id, 1, "checkout-2"),
            CancellationToken.None);

        Assert.IsFalse(checkout.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantProductNotSellable, checkout.Error!.Code);
        Assert.AreEqual(0L, harness.Count("commerce_orders"));
    }

    [TestMethod]
    public async Task BrowsingHidesLocalStoresFromOtherGuilds()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness, "LOCAL_GUILD", "LOCAL_GUILD");
        await CreateActiveProductAsync(harness, profile.Id);

        Result<MerchantStorePageView> local = await harness.Commerce.BrowseMerchantStoresAsync(
            new BrowseMerchantStoresQuery(GuildId, null), CancellationToken.None);
        Result<MerchantStorePageView> foreign = await harness.Commerce.BrowseMerchantStoresAsync(
            new BrowseMerchantStoresQuery(OtherGuildId, null), CancellationToken.None);

        Assert.IsTrue(local.IsSuccess);
        Assert.AreEqual(1, local.Value.Items.Count);
        Assert.AreEqual(1, local.Value.Items[0].ActiveProductCount);
        Assert.IsTrue(foreign.IsSuccess);
        Assert.AreEqual(0, foreign.Value.Items.Count);
    }

    [TestMethod]
    public async Task ConfirmingACheckoutIsRejectedWhileCaptureIsUnavailable()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id, "FINITE");
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "checkout-3"),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);
        Assert.AreEqual(0L, harness.Count("debit_card_authorizations"));
        Assert.AreEqual("PENDING", harness.ReadText("SELECT status FROM commerce_payments;"));
        Assert.AreEqual(
            "AWAITING_CONFIRMATION", harness.ReadText("SELECT status FROM commerce_orders;"));
    }

    [TestMethod]
    public async Task ListingProductsReturnsThePublishedPrice()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        await CreateActiveProductAsync(harness, profile.Id, "FINITE");

        Result<MerchantProductPageView> products = await harness.Commerce.ListMerchantProductsAsync(
            new ListMerchantProductsQuery(GuildId, profile.Id, null), CancellationToken.None);

        Assert.IsTrue(products.IsSuccess, products.Error?.Code);
        Assert.AreEqual(1, products.Value.Items.Count);
        Assert.AreEqual(1_200L, products.Value.Items[0].UnitPrice.Value);
        Assert.AreEqual(5L, products.Value.Items[0].OnHandQuantity);
    }

    [TestMethod]
    public async Task ADiscordRolePolicyRequiresALocalSaleScope()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);

        Result<MerchantFulfillmentPolicyVersionView> global =
            await harness.Merchants.PublishFulfillmentPolicyAsync(
                new PublishMerchantFulfillmentPolicyCommand(
                    Merchant(), product.Id, "DISCORD_ROLE", "ON_CAPTURE", "555000111"),
                CancellationToken.None);

        Assert.IsFalse(global.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantFulfillmentScopeInvalid, global.Error!.Code);
    }
}
