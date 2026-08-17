namespace Numera.Architecture.Tests;

[TestClass]
public sealed class FeatureClosureTests
{
    private const int CanonicalFeatureCount = 23;

    private static readonly string[] NonTableDataContractReferences =
    [
        "ATM_BANNER/PUBLIC_BANNER",
        "ResolvedPresentationSnapshot",
    ];

    private static readonly string[] ColumnsWithoutCanonicalReference =
    [
        "FX_SETTLEMENT_LEGS/command-permission",
    ];

    [TestMethod]
    public void TheDeclarationCarriesEveryCanonicalFeatureExactlyOnce()
    {
        string[] ids = [.. ClosureCatalog.Features.Select(static feature => feature.FeatureId)];

        Assert.AreEqual(CanonicalFeatureCount, ids.Length, string.Join(',', ids));
        Assert.AreEqual(ids.Length, ids.Distinct(StringComparer.Ordinal).Count(), string.Join(',', ids));

        foreach (string id in ids)
        {
            Assert.IsTrue(
                id.Length > 0 && id.All(static character => char.IsAsciiLetterUpper(character) || character == '_'),
                id);
        }
    }

    [TestMethod]
    public void EveryFeatureDeclaresTheEightCanonicalColumnsInOrder()
    {
        foreach (FeatureClosureDeclaration feature in ClosureCatalog.Features)
        {
            string[] keys = [.. feature.Columns.Select(static column => column.Key)];

            CollectionAssert.AreEqual(
                ClosureCatalog.CanonicalColumnOrder,
                keys,
                $"{feature.FeatureId} の欄が §54.10 の並びと一致しません。[{string.Join(',', keys)}]");
        }
    }

    [TestMethod]
    public void EveryInvariantReferenceResolvesToSection47()
    {
        AssertColumnResolves(
            "invariant",
            static reference => ClosureCatalog.ExpandInvariantReference(reference)
                .All(static id => ClosureCatalog.Invariants.Contains(id, StringComparer.Ordinal)));
    }

    [TestMethod]
    public void EveryStateTransitionReferenceResolvesToSection48() =>
        AssertColumnResolves(
            "state-transition",
            static reference => ClosureCatalog.StateTransitions.Contains(reference, StringComparer.Ordinal));

    [TestMethod]
    public void EveryLinearizationReferenceResolvesToSection49() =>
        AssertColumnResolves(
            "linearization",
            static reference => ClosureCatalog.Linearizations.Contains(reference, StringComparer.Ordinal));

    [TestMethod]
    public void EveryDataContractReferenceResolvesToTheRequiredTableCatalog() =>
        AssertColumnResolves(
            "canonical-data-contract",
            static reference =>
            {
                if (NonTableDataContractReferences.Contains(reference, StringComparer.Ordinal))
                {
                    return true;
                }

                int column = reference.IndexOf('.');
                string table = column < 0 ? reference : reference[..column];

                return ClosureCatalog.RequiredTables.Contains(table, StringComparer.Ordinal);
            });

    [TestMethod]
    public void EveryCommandReferenceResolvesToTheCanonicalCommandSurface() =>
        AssertColumnResolves(
            "command-permission",
            static reference => ClosureCatalog.CommandRoutes.Contains(reference, StringComparer.Ordinal));

    [TestMethod]
    public void EveryPublicApiReferenceResolvesToSection52() =>
        AssertColumnResolves(
            "public-application-api",
            static reference => ClosureCatalog.ApiInterfaces.Contains(reference, StringComparer.Ordinal));

    [TestMethod]
    public void EveryExpectedOutcomeReferenceResolvesToSection53() =>
        AssertColumnResolves(
            "expected-outcome-test",
            static reference => ClosureCatalog.ExpectedOutcomes.Contains(reference, StringComparer.Ordinal));

    [TestMethod]
    public void EveryResourceBudgetReferenceResolvesToSection54() =>
        AssertColumnResolves(
            "resource-budget",
            static reference => ClosureCatalog.ResourceBudgets.Contains(reference, StringComparer.Ordinal));

    [TestMethod]
    public void NoColumnIsEmptyOutsideTheDeclaredExemptions()
    {
        List<string> empty = [];
        List<string> stale = [];

        foreach (FeatureClosureDeclaration feature in ClosureCatalog.Features)
        {
            foreach (FeatureClosureColumn column in feature.Columns)
            {
                string key = $"{feature.FeatureId}/{column.Key}";
                bool exempt = ColumnsWithoutCanonicalReference.Contains(key, StringComparer.Ordinal);
                bool resolvable = column.References.Length > 0;

                if (!resolvable && !exempt)
                {
                    empty.Add(key);
                }

                if (resolvable && exempt)
                {
                    stale.Add(key);
                }
            }
        }

        Assert.AreEqual(string.Empty, string.Join(',', empty), "参照先の無い欄があります。");
        Assert.AreEqual(string.Empty, string.Join(',', stale), "保留一覧が古くなっています。");
    }

    [TestMethod]
    public void TheCanonicalCatalogsAreNotEmpty()
    {
        Assert.IsGreaterThan(0, ClosureCatalog.Invariants.Length);
        Assert.IsGreaterThan(0, ClosureCatalog.StateTransitions.Length);
        Assert.IsGreaterThan(0, ClosureCatalog.Linearizations.Length);
        Assert.IsGreaterThan(0, ClosureCatalog.ExpectedOutcomes.Length);
        Assert.IsGreaterThan(0, ClosureCatalog.ResourceBudgets.Length);
        Assert.IsGreaterThan(0, ClosureCatalog.ApiInterfaces.Length);
        Assert.IsGreaterThan(0, ClosureCatalog.RequiredTables.Length);
        Assert.IsGreaterThan(0, ClosureCatalog.CommandRoutes.Length);
    }

    private static void AssertColumnResolves(string key, Func<string, bool> resolves)
    {
        List<string> unresolved = [];

        foreach (FeatureClosureDeclaration feature in ClosureCatalog.Features)
        {
            foreach (FeatureClosureColumn column in feature.Columns)
            {
                if (!string.Equals(column.Key, key, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (string reference in column.References.Where(reference => !resolves(reference)))
                {
                    unresolved.Add($"{feature.FeatureId}:{reference}");
                }
            }
        }

        Assert.AreEqual(
            string.Empty,
            string.Join(',', unresolved),
            $"{key} の参照が Canonical 定義へ解決できません。");
    }
}
