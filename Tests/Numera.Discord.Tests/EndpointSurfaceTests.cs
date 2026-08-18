using Numera.Discord.Abstractions;
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
                "account link",
                "account register",
                "account status",
                "account unlink",
                "bank accounts",
                "bank atm",
                "bank card",
                "bank close",
                "bank list",
                "bank open",
                "bank payments",
                "bank statement",
                "bank transfer",
                "fx board",
                "fx cancel",
                "fx chart",
                "fx history",
                "fx market",
                "fx order",
                "fx orders",
                "fx rate",
                "help",
                "manage bank-asset",
                "manage bank-create",
                "manage bank-edit",
                "manage bank-retire",
                "manage currency-burn",
                "manage currency-create",
                "manage currency-edit",
                "manage currency-issue",
                "manage currency-retire",
                "manage fx-market",
                "manage panel",
                "shop browse",
                "shop orders",
                "system commands-sync",
                "system guild",
                "system panel",
                "system reconcile",
            },
            routes);
    }

    [TestMethod]
    public void EveryRootBecomesOneGeneratedModule()
    {
        string[] modules = [.. EconomyGeneratedModules.All.Select(static type => type.Name).Order(StringComparer.Ordinal)];

        CollectionAssert.AreEqual(
            new[]
            {
                "AccountModule",
                "BankModule",
                "FxModule",
                "ManageModule",
                "NumeraDiscordEndpointsBankEndpointsInteractionsModule",
                "NumeraDiscordEndpointsHelpEndpointsModule",
                "ShopModule",
                "SystemModule",
            },
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

        CollectionAssert.AreEqual(
            new[] { "account", "bank", "fx", "help", "manage", "shop" }, primary);
        Assert.AreEqual("system", provider.ControlCommands().Single().Name);
    }

    [TestMethod]
    public void TheTransferCommandStartsTheSelectFlowWithoutOptions()
    {
        GeneratedCommandManifestProvider provider = new();

        CommandManifestEntry bank = provider.PrimaryCommands()
            .Single(static entry => string.Equals(entry.Name, "bank", StringComparison.Ordinal));

        CommandOptionManifest transfer = bank.Options
            .Single(static option => string.Equals(option.Name, "transfer", StringComparison.Ordinal));

        Assert.AreEqual(GeneratedOptionType.SubCommand, transfer.Type);
        Assert.AreEqual(0, transfer.Options.Count);
    }

    [TestMethod]
    public void BankSelectionIsBackedByAutocomplete()
    {
        GeneratedCommandManifestProvider provider = new();

        CommandManifestEntry bank = provider.PrimaryCommands()
            .Single(static entry => string.Equals(entry.Name, "bank", StringComparison.Ordinal));

        CommandOptionManifest option = bank.Options
            .Single(static entry => string.Equals(entry.Name, "open", StringComparison.Ordinal))
            .Options
            .Single(static entry => string.Equals(entry.Name, "bank", StringComparison.Ordinal));

        Assert.IsTrue(option.Autocomplete);
        Assert.AreEqual(0, option.Choices.Count);
    }

    [TestMethod]
    public void TheTransferFlowDeclaresItsComponentsAndModal()
    {
        CollectionAssert.AreEqual(
            new[] { "transfer-execute", "transfer-input", "transfer-source" },
            EconomyCommandManifest.ComponentActions.Order(StringComparer.Ordinal).ToArray());

        CollectionAssert.AreEqual(
            new[] { "transfer" },
            EconomyCommandManifest.ModalActions.ToArray());
    }

    [TestMethod]
    public void TheTransferModalCarriesTheCanonicalFields()
    {
        EconomyGeneratedModalFormCatalog catalog = new();

        CollectionAssert.AreEqual(
            new[] { "bank-code", "branch-code", "account-number", "amount", "memo" },
            catalog.Resolve("transfer").Select(static field => field.CustomId).ToArray());

        DiscordModalFieldDefinition memo = catalog.Resolve("transfer")[4];

        Assert.AreEqual(EconomyModalFieldStyle.Paragraph, memo.Style);
        Assert.IsFalse(memo.Required);
        Assert.AreEqual(100, memo.MaximumLength);
        Assert.IsEmpty(catalog.Resolve("unknown"));
    }
}
