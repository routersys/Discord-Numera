namespace Numera.Architecture.Tests;

[TestClass]
public sealed class ConformanceDataTests
{
    private static string Directory =>
        Path.Combine(
            ProjectLayout.RepositoryRoot,
            "Tests",
            "Numera.Architecture.Tests",
            "Conformance");

    [TestMethod]
    public void EveryDataFileIsAnExtractedIdentifierList()
    {
        string[] unexpected =
        [
            .. System.IO.Directory.EnumerateFiles(Directory)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(static name => !name.EndsWith(".txt", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(
            string.Empty,
            string.Join(',', unexpected),
            "抽出結果以外のファイルが置かれています。");
    }

    [TestMethod]
    public void NoDataFileCarriesSpecificationProse()
    {
        List<string> offenders = [];

        foreach (string path in System.IO.Directory.EnumerateFiles(Directory))
        {
            if (File.ReadAllText(path).Any(static character => character > 'ÿ'))
            {
                offenders.Add(Path.GetFileName(path) ?? path);
            }
        }

        Assert.AreEqual(
            string.Empty,
            string.Join(',', offenders),
            "抽出データへ非 Latin-1 文字が含まれています。仕様書の本文を複製していないか確認してください。");
    }

    [TestMethod]
    public void TheExtractedCountsAreStable()
    {
        Assert.AreEqual(292, ClosureCatalog.Invariants.Length);
        Assert.AreEqual(79, ClosureCatalog.StateTransitions.Length);
        Assert.AreEqual(39, ClosureCatalog.Linearizations.Length);
        Assert.AreEqual(25, ClosureCatalog.ExpectedOutcomes.Length);
        Assert.AreEqual(14, ClosureCatalog.ResourceBudgets.Length);
        Assert.AreEqual(159, ClosureCatalog.ApiMembers.Length);
        Assert.AreEqual(30, ClosureCatalog.ApiInterfaces.Length);
        Assert.AreEqual(136, ClosureCatalog.RequiredTables.Length);
    }
}
