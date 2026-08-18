using System.Security.AccessControl;
using System.Security.Principal;
using Numera.Host.Startup;

namespace Numera.Host.Tests;

[TestClass]
public sealed class DataDirectoryProtectionTests
{
    private static string CreateRoot() =>
        Path.Combine(Path.GetTempPath(), "numera-protect", Guid.NewGuid().ToString("n"));

    [TestMethod]
    public void ProtectionCreatesAndRestrictsEveryDirectory()
    {
        string root = CreateRoot();
        string backups = Path.Combine(root, "backups");

        DirectoryProtectionResult result = DataDirectoryProtection.Apply(root, backups);

        try
        {
            Assert.IsTrue(result.IsApplied, result.Detail);
            Assert.IsTrue(Directory.Exists(root));
            Assert.IsTrue(Directory.Exists(backups));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void TheProcessOwnerKeepsFullAccess()
    {
        string root = CreateRoot();

        Assert.IsTrue(DataDirectoryProtection.Apply(root).IsApplied);

        try
        {
            string probe = Path.Combine(root, "probe.txt");
            File.WriteAllText(probe, "ok");

            Assert.AreEqual("ok", File.ReadAllText(probe));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void InheritedAccessIsRemovedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive();
            return;
        }

        string root = CreateRoot();

        Assert.IsTrue(DataDirectoryProtection.Apply(root).IsApplied);

        try
        {
            DirectorySecurity security = new DirectoryInfo(root).GetAccessControl();

            Assert.IsTrue(security.AreAccessRulesProtected);

            AuthorizationRuleCollection rules = security.GetAccessRules(
                includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

            Assert.AreEqual(1, rules.Count);
            Assert.AreEqual(
                WindowsIdentity.GetCurrent().User!.Value,
                ((FileSystemAccessRule)rules[0]!).IdentityReference.Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void AnUnusableDirectoryPathIsReportedAsFailure()
    {
        DirectoryProtectionResult result = DataDirectoryProtection.Apply(
            Path.Combine(CreateRoot(), new string('x', 400), "nested"));

        Assert.AreEqual(DirectoryProtectionStatus.Failed, result.Status);
        Assert.IsNotEmpty(result.Detail);
    }
}
