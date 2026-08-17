using System.Reflection;

namespace Numera.Architecture.Tests;

[TestClass]
public sealed class LayeringTests
{
    private const string DiscordNetPrefix = "Discord.Net";
    private const string DiscordAssembly = "Discord";
    private const string SqliteAssembly = "Microsoft.Data.Sqlite";
    private const string PersistenceAssembly = "Numera.Persistence.Sqlite";

    private static Assembly Domain => typeof(Numera.Domain.Common.EntityIdValue).Assembly;

    private static Assembly Application => typeof(Numera.Application.Common.Result).Assembly;

    private static Assembly Persistence => typeof(Numera.Persistence.Sqlite.SqliteConnectionFactory).Assembly;

    private static Assembly Discord => typeof(Numera.Discord.Rendering.TextCatalog).Assembly;

    private static string[] ReferenceNames(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(static reference => reference.Name ?? string.Empty)];

    private static void AssertDoesNotReference(Assembly assembly, string referencedName)
    {
        string[] references = ReferenceNames(assembly);

        Assert.IsFalse(
            references.Any(name => name.StartsWith(referencedName, StringComparison.Ordinal)),
            $"{assembly.GetName().Name} が {referencedName} を参照しています。");
    }

    [TestMethod]
    public void DomainDoesNotReferenceDiscordNet()
    {
        AssertDoesNotReference(Domain, DiscordNetPrefix);
        AssertDoesNotReference(Domain, DiscordAssembly);
    }

    [TestMethod]
    public void DomainDoesNotReferenceSqlite() => AssertDoesNotReference(Domain, SqliteAssembly);

    [TestMethod]
    public void ApplicationDoesNotReferenceDiscordNet()
    {
        AssertDoesNotReference(Application, DiscordNetPrefix);
        AssertDoesNotReference(Application, DiscordAssembly);
    }

    [TestMethod]
    public void ApplicationDoesNotReferenceSqlite() => AssertDoesNotReference(Application, SqliteAssembly);

    [TestMethod]
    public void PersistenceDoesNotReferenceDiscordNet()
    {
        AssertDoesNotReference(Persistence, DiscordNetPrefix);
        AssertDoesNotReference(Persistence, DiscordAssembly);
    }

    [TestMethod]
    public void DiscordDoesNotReferenceConcreteRepositories() =>
        AssertDoesNotReference(Discord, PersistenceAssembly);

    [TestMethod]
    public void DomainReferencesNoOtherNumeraAssembly()
    {
        string[] numeraReferences =
        [
            .. ReferenceNames(Domain).Where(static name => name.StartsWith("Numera.", StringComparison.Ordinal)),
        ];

        CollectionAssert.AreEqual(Array.Empty<string>(), numeraReferences);
    }

    [TestMethod]
    public void DomainReferencesOnlyTheBaseClassLibrary()
    {
        string[] foreign =
        [
            .. ReferenceNames(Domain).Where(static name =>
                !name.StartsWith("System", StringComparison.Ordinal) &&
                !string.Equals(name, "netstandard", StringComparison.Ordinal)),
        ];

        CollectionAssert.AreEqual(Array.Empty<string>(), foreign);
    }

    [TestMethod]
    public void ApplicationReferencesOnlyDomainAmongNumeraAssemblies()
    {
        string[] numeraReferences =
        [
            .. ReferenceNames(Application)
                .Where(static name => name.StartsWith("Numera.", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        CollectionAssert.AreEqual(new[] { "Numera.Domain" }, numeraReferences);
    }

    [TestMethod]
    public void PersistenceReferencesOnlyApplicationAndDomainAmongNumeraAssemblies()
    {
        string[] numeraReferences =
        [
            .. ReferenceNames(Persistence)
                .Where(static name => name.StartsWith("Numera.", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        CollectionAssert.AreEqual(new[] { "Numera.Application", "Numera.Domain" }, numeraReferences);
    }
}
