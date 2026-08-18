namespace Numera.Architecture.Tests;

[TestClass]
public sealed class SchemaConformanceTests
{
    private static readonly string[] TablesOutsideTheRequiredCatalog =
    [
        "schema_migrations",
    ];

    private static readonly string[] ForbiddenTables =
    [
        "account_access_grants",
        "account_holders",
        "party_accesses",
    ];

    private static readonly string[] TablesAwaitingImplementation =
    [

        "account_product_version_assignments",
        "account_restrictions",
        "atm_cash_cassettes",
        "atm_discord_installations",
        "atm_network_participations",
        "atm_networks",
        "atm_placement_agreements",
        "atm_terminal_currency_services",
        "atm_terminals",
        "atm_transactions",
        "authorization_decisions",
        "bank_assets",
        "bank_cash_vaults",
        "bank_treasury_fx_accounts",
        "cash_holders",
        "cash_movements",
        "cash_positions",
        "cash_wallets",
        "currency_denominations",
        "deposit_insurance_claims",
        "deposit_insurance_enrollments",
        "deposit_insurance_funds",
        "deposit_insurance_premium_payments",
        "deposit_insurance_reservations",
        "deposit_insurance_scheme_versions",
        "deposit_insurance_schemes",
        "inbox_events",
        "insurance_settlement_wallet_payouts",
        "insurance_settlement_wallets",
        "interest_accruals",
        "interest_posting_batches",
        "operation_results",
        "reconciliation_issues",
        "reconciliation_runs",
        "resolution_transfers",
        "routing_aliases",
    ];

    [TestMethod]
    public void EveryPhysicalTableIsDeclaredByTheRequiredTableCatalog()
    {
        using SchemaFixture schema = SchemaFixture.Create();

        string[] undeclared =
        [
            .. schema.TableNames()
                .Where(static table => !ClosureCatalog.RequiredTables.Contains(table, StringComparer.Ordinal))
                .Where(static table => !TablesOutsideTheRequiredCatalog.Contains(table, StringComparer.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(
            string.Empty,
            string.Join(',', undeclared),
            "K.10.2 の Table Catalog に無い Table が存在します。");
    }

    [TestMethod]
    public void EveryRequiredTableIsImplementedOrExplicitlyPending()
    {
        using SchemaFixture schema = SchemaFixture.Create();
        string[] physical = [.. schema.TableNames()];

        string[] missing =
        [
            .. ClosureCatalog.RequiredTables
                .Where(table => !physical.Contains(table, StringComparer.Ordinal))
                .Where(static table => !TablesAwaitingImplementation.Contains(table, StringComparer.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(
            string.Empty,
            string.Join(',', missing),
            "必須 Table が未定義かつ保留一覧にもありません。");
    }

    [TestMethod]
    public void NoPendingTableIsAlreadyImplemented()
    {
        using SchemaFixture schema = SchemaFixture.Create();
        string[] physical = [.. schema.TableNames()];

        string[] stale =
        [
            .. TablesAwaitingImplementation
                .Where(table => physical.Contains(table, StringComparer.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(
            string.Empty,
            string.Join(',', stale),
            "実装済み Table が保留一覧に残っています。");
    }

    [TestMethod]
    public void EveryPendingTableIsDeclaredByTheRequiredTableCatalog()
    {
        string[] unknown =
        [
            .. TablesAwaitingImplementation
                .Where(static table => !ClosureCatalog.RequiredTables.Contains(table, StringComparer.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(
            string.Empty,
            string.Join(',', unknown),
            "保留一覧に K.10.2 へ無い Table 名があります。");

        Assert.AreEqual(
            TablesAwaitingImplementation.Length,
            TablesAwaitingImplementation.Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void ForbiddenOwnershipTablesAreAbsent()
    {
        using SchemaFixture schema = SchemaFixture.Create();
        string[] physical = [.. schema.TableNames()];

        string[] present = [.. ForbiddenTables.Where(table => physical.Contains(table, StringComparer.Ordinal))];

        Assert.AreEqual(string.Empty, string.Join(',', present), "K.10.2 が禁止する Table が存在します。");

        string[] declared =
        [
            .. ForbiddenTables.Where(static table => ClosureCatalog.RequiredTables.Contains(table, StringComparer.Ordinal)),
        ];

        Assert.AreEqual(string.Empty, string.Join(',', declared));
    }

    [TestMethod]
    public void TheRequiredTableCatalogIsWellFormed()
    {
        Assert.IsGreaterThan(100, ClosureCatalog.RequiredTables.Length);

        string[] duplicates =
        [
            .. ClosureCatalog.RequiredTables
                .GroupBy(static table => table, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(string.Empty, string.Join(',', duplicates), "K.10.2 に重複 Table 名があります。");
    }

    [TestMethod]
    public void TablesOutsideTheCatalogRemainPresent()
    {
        using SchemaFixture schema = SchemaFixture.Create();
        string[] physical = [.. schema.TableNames()];

        string[] absent =
        [
            .. TablesOutsideTheRequiredCatalog.Where(table => !physical.Contains(table, StringComparer.Ordinal)),
        ];

        Assert.AreEqual(string.Empty, string.Join(',', absent), "Catalog 外として除外した Table が存在しません。");
    }
}
