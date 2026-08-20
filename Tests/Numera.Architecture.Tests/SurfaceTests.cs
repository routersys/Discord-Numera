using System.Reflection;

namespace Numera.Architecture.Tests;

[TestClass]
public sealed class SurfaceTests
{
    private const int DomainBudget = 405;
    private const int ApplicationBudget = 548;
    private const int PersistenceBudget = 86;
    private const int DiscordBudget = 73;

    private static void AssertWithinBudget(Assembly assembly, int budget)
    {
        int count = assembly.GetExportedTypes().Length;

        Assert.IsLessThanOrEqualTo(
            budget,
            count,
            $"{assembly.GetName().Name} の公開型が {count} 件へ増えました。予算の見直しを意識的に行ってください。");
    }

    [TestMethod]
    public void DomainPublicSurfaceStaysWithinBudget() =>
        AssertWithinBudget(ProjectLayout.Domain, DomainBudget);

    [TestMethod]
    public void ApplicationPublicSurfaceStaysWithinBudget() =>
        AssertWithinBudget(ProjectLayout.Application, ApplicationBudget);

    [TestMethod]
    public void PersistencePublicSurfaceStaysWithinBudget() =>
        AssertWithinBudget(ProjectLayout.Persistence, PersistenceBudget);

    [TestMethod]
    public void DiscordPublicSurfaceStaysWithinBudget() =>
        AssertWithinBudget(ProjectLayout.Discord, DiscordBudget);

    [TestMethod]
    public void EveryLayerExposesTypes()
    {
        foreach (Assembly assembly in ProjectLayout.Assemblies)
        {
            Assert.IsGreaterThan(0, assembly.GetExportedTypes().Length, assembly.GetName().Name);
        }
    }
}
