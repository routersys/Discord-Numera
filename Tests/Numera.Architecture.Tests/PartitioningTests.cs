using System.Reflection;

namespace Numera.Architecture.Tests;

[TestClass]
public sealed class PartitioningTests
{
    private static readonly (string Project, string File, string Declaration)[] PartitionedDeclarations =
    [
        (
            "Numera.Application",
            Path.Combine("Abstractions", "IBankingUnitOfWork.cs"),
            "public partial interface IBankingUnitOfWork"),
        (
            "Numera.Application",
            Path.Combine("Common", "BankingErrorCodes.cs"),
            "public static partial class BankingErrorCodes"),
        (
            "Numera.Domain",
            Path.Combine("Common", "InvariantViolationException.cs"),
            "public static partial class InvariantViolationCode"),
        (
            "Numera.Persistence.Sqlite",
            Path.Combine("Transactions", "SqliteBankingWriteGateway.cs"),
            "public sealed partial class SqliteBankingUnitOfWork"),
    ];

    [TestMethod]
    public void SharedExtensionPointsStayPartial()
    {
        foreach ((string project, string file, string declaration) in PartitionedDeclarations)
        {
            string path = Path.Combine(ProjectLayout.RepositoryRoot, project, file);

            Assert.IsTrue(File.Exists(path), path);
            Assert.IsTrue(
                File.ReadAllText(path).Contains(declaration, StringComparison.Ordinal),
                $"{file} の宣言が {declaration} ではありません。並列トラックが同じ行で衝突します。");
        }
    }

    [TestMethod]
    public void UnitOfWorkResolvesEveryRepositoryLazily()
    {
        Type unitOfWork = ProjectLayout.Persistence.GetTypes()
            .Single(static type => string.Equals(type.Name, "SqliteBankingUnitOfWork", StringComparison.Ordinal));

        ConstructorInfo[] constructors = unitOfWork.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.AreEqual(1, constructors.Length);
        Assert.AreEqual(1, constructors[0].GetParameters().Length);

        PropertyInfo[] repositories =
        [
            .. unitOfWork.GetProperties(BindingFlags.Instance | BindingFlags.Public),
        ];

        Assert.IsGreaterThan(0, repositories.Length);

        foreach (PropertyInfo repository in repositories)
        {
            Assert.IsNull(repository.SetMethod, repository.Name);
        }
    }

    [TestMethod]
    public void MigrationVersionsAreContiguous()
    {
        string[] names =
        [
            .. Directory
                .EnumerateFiles(
                    Path.Combine(
                        ProjectLayout.RepositoryRoot, "Numera.Persistence.Sqlite", "Migrations"),
                    "*.sql")
                .Select(Path.GetFileName)
                .Select(static name => name!)
                .Order(StringComparer.Ordinal),
        ];

        Assert.IsGreaterThan(0, names.Length);

        for (int index = 0; index < names.Length; index++)
        {
            string expected = (index + 1).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);

            Assert.IsTrue(
                names[index].StartsWith(expected, StringComparison.Ordinal),
                $"Migration の連番が途切れています。{expected} で始まるべき位置に {names[index]} があります。");
        }
    }
}
