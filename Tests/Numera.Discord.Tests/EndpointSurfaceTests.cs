using Numera.Discord.Commands;
using Numera.Discord.Gateway;
using Numera.Discord.Routing;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class EndpointSurfaceTests
{
    [TestMethod]
    public void TheDeclaredRoutesAreGenerated()
    {
        string[] routes =
        [
            .. EconomyCommandManifest.SlashCommandPaths.Order(StringComparer.Ordinal),
        ];

        CollectionAssert.AreEqual(
            new[]
            {
                "account register",
                "account status",
                "bank open",
                "bank transfer",
                "help",
                "manage currency-burn",
                "manage currency-create",
                "manage currency-issue",
            },
            routes);
    }

    [TestMethod]
    public void EveryRootBecomesOneGeneratedModule()
    {
        string[] modules = [.. EconomyGeneratedModules.All.Select(static type => type.Name).Order(StringComparer.Ordinal)];

        CollectionAssert.AreEqual(
            new[] { "AccountModule", "BankModule", "ManageModule", "NumeraDiscordEndpointsHelpEndpointsModule" },
            modules);
    }

    [TestMethod]
    public void TheManifestProviderExposesEveryRoot()
    {
        GeneratedCommandManifestProvider provider = new();

        string[] primary =
        [
            .. provider.PrimaryCommands().Select(static entry => entry.Name).Order(StringComparer.Ordinal),
        ];

        CollectionAssert.AreEqual(new[] { "account", "bank", "help", "manage" }, primary);
        Assert.AreEqual(0, provider.ControlCommands().Count);
    }

    [TestMethod]
    public void TheTransferCommandCarriesItsOptions()
    {
        GeneratedCommandManifestProvider provider = new();

        CommandManifestEntry bank = provider.PrimaryCommands()
            .Single(static entry => string.Equals(entry.Name, "bank", StringComparison.Ordinal));

        CommandOptionManifest transfer = bank.Options
            .Single(static option => string.Equals(option.Name, "transfer", StringComparison.Ordinal));

        Assert.AreEqual(GeneratedOptionType.SubCommand, transfer.Type);
        CollectionAssert.AreEqual(
            new[] { "source-account", "bank", "branch", "account", "amount", "memo" },
            transfer.Options.Select(static option => option.Name).ToArray());
        Assert.IsFalse(transfer.Options.Single(static o => o.Name == "memo").Required);
    }

    [TestMethod]
    public void BankSelectionIsBackedByAutocomplete()
    {
        GeneratedCommandManifestProvider provider = new();

        CommandManifestEntry bank = provider.PrimaryCommands()
            .Single(static entry => string.Equals(entry.Name, "bank", StringComparison.Ordinal));

        foreach (string subcommand in new[] { "open", "transfer" })
        {
            CommandOptionManifest option = bank.Options
                .Single(entry => string.Equals(entry.Name, subcommand, StringComparison.Ordinal))
                .Options
                .Single(static entry => string.Equals(entry.Name, "bank", StringComparison.Ordinal));

            Assert.IsTrue(option.Autocomplete, subcommand);
            Assert.AreEqual(0, option.Choices.Count, subcommand);
        }
    }
}
