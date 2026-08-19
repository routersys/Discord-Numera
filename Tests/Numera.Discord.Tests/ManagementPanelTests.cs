using Numera.Discord.Commands;
using Numera.Discord.Gateway;
using Numera.Discord.Routing;
using Numera.Discord.Rendering;
using Numera.Discord.Sessions;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class ManagementPanelTests
{
    private static readonly ITextCatalog Catalog = CanonicalTextCatalog.Create();

    [TestMethod]
    public void ThePanelDeclaresTheSeventeenCanonicalCategories()
    {
        int count = ManagementPanelCatalog.Categories.Count;

        Assert.AreEqual(17, count);
    }

    [TestMethod]
    public void TheCategoryOrderFollowsSection27E1()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                ManagementPanelCatalog.EconomyCalendar,
                ManagementPanelCatalog.CurrencyIssuance,
                ManagementPanelCatalog.CurrencyTrust,
                ManagementPanelCatalog.BankBranch,
                ManagementPanelCatalog.BankOperator,
                ManagementPanelCatalog.DepositProduct,
                ManagementPanelCatalog.FeeLimitDormancy,
                ManagementPanelCatalog.CardDesign,
                ManagementPanelCatalog.AtmCash,
                ManagementPanelCatalog.MerchantCommerce,
                ManagementPanelCatalog.PaymentNetwork,
                ManagementPanelCatalog.FxMarket,
                ManagementPanelCatalog.CentralBank,
                ManagementPanelCatalog.DepositInsurance,
                ManagementPanelCatalog.PrudentialResolution,
                ManagementPanelCatalog.Presentation,
                ManagementPanelCatalog.Audit,
            },
            ManagementPanelCatalog.Categories.Select(static category => category.Value).ToArray());
    }

    [TestMethod]
    public void TheCategoryListFitsASingleDiscordSelect()
    {
        int count = ManagementPanelCatalog.Categories.Count;

        Assert.IsLessThanOrEqualTo(
            Numera.Discord.Abstractions.DiscordResponseSelect.MaximumOptionCount, count);
    }

    [TestMethod]
    public void NoCategoryExceedsTheSelectBudget()
    {
        foreach (ManagementPanelCategory category in ManagementPanelCatalog.Categories)
        {
            Assert.IsGreaterThan(0, category.Actions.Count);
            Assert.IsLessThanOrEqualTo(
                Numera.Discord.Abstractions.DiscordResponseSelect.MaximumOptionCount,
                category.Actions.Count);
        }
    }

    [TestMethod]
    public void EveryCategoryAndActionHasCanonicalText()
    {
        foreach (ManagementPanelCategory category in ManagementPanelCatalog.Categories)
        {
            Assert.IsTrue(
                Catalog.TryResolve(ViewKeys.PanelCategoryLabel(category.Value), out string label),
                category.Value);
            Assert.IsNotEmpty(label);

            foreach (ManagementPanelAction action in category.Actions)
            {
                Assert.IsTrue(
                    Catalog.TryResolve(
                        ViewKeys.PanelActionLabel(category.Value, action.Value), out string actionLabel),
                    category.Value + "." + action.Value);
                Assert.IsNotEmpty(actionLabel);
            }
        }
    }

    [TestMethod]
    public void EveryActionValueIsUniqueWithinItsCategory()
    {
        foreach (ManagementPanelCategory category in ManagementPanelCatalog.Categories)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);

            foreach (ManagementPanelAction action in category.Actions)
            {
                Assert.IsTrue(seen.Add(action.Value), category.Value + "." + action.Value);
            }
        }
    }

    [TestMethod]
    public void EveryImplementedRouteIsADeclaredSlashCommand()
    {
        HashSet<string> declared = new(
            EconomyCommandManifest.SlashCommandPaths.Select(static path => "/" + path),
            StringComparer.Ordinal);

        foreach (ManagementPanelCategory category in ManagementPanelCatalog.Categories)
        {
            foreach (ManagementPanelAction action in category.Actions)
            {
                if (!action.IsImplemented)
                {
                    continue;
                }

                Assert.IsTrue(declared.Contains(action.Route), action.Route);
            }
        }
    }

    [TestMethod]
    public void AnUnknownCategoryOrActionResolvesToNothing()
    {
        Assert.IsNull(ManagementPanelCatalog.Find("no-such-category"));

        ManagementPanelCategory category = ManagementPanelCatalog.Categories[0];

        Assert.IsNull(ManagementPanelCatalog.FindAction(category, "no-such-action"));
        Assert.IsNotNull(ManagementPanelCatalog.FindAction(category, category.Actions[0].Value));
    }

    [TestMethod]
    public void ThePanelPayloadSurvivesASerialisationRoundTrip()
    {
        ManagePanelPayload payload = ManagePanelPayloadCodec.Empty with
        {
            Category = ManagementPanelCatalog.CurrencyIssuance,
            Action = "currency-issue",
        };

        ManagePanelPayload restored =
            ManagePanelPayloadCodec.Read(ManagePanelPayloadCodec.Write(payload));

        Assert.AreEqual(payload, restored);
        Assert.AreEqual(ManagePanelPayloadCodec.Empty, ManagePanelPayloadCodec.Read(string.Empty));
        Assert.AreEqual(ManagePanelPayloadCodec.Empty, ManagePanelPayloadCodec.Read("{"));
    }

    [TestMethod]
    public void ThePanelTextFollowsSection27E1()
    {
        Assert.AreEqual("管理メニュー", Catalog.Resolve(ViewKeys.ManagePanel + ".title"));
        Assert.AreEqual(
            "管理する項目を選択してください。", Catalog.Resolve(ViewKeys.ManagePanel + ".description"));
        Assert.AreEqual("管理項目を選択", Catalog.Resolve(ViewKeys.ManagePanelPlaceholder));
    }
}

[TestClass]
public sealed class CommandSyncReplacementTests
{
    [TestMethod]
    public void TheModifyOptionLimitMatchesTheMeasuredLibraryLimit()
    {
        int limit = Numera.Discord.Gateway.RestApplicationCommandGateway.ModifyOptionLimit;

        Assert.AreEqual(10, limit);
    }

    [TestMethod]
    public void ARootWithMoreOptionsThanModifyAllowsIsReplacedInstead()
    {
        GeneratedCommandManifestProvider provider = new();

        CommandManifestEntry manage = provider.PrimaryCommands()
            .Single(static entry => string.Equals(entry.Name, "manage", StringComparison.Ordinal));

        Assert.IsGreaterThan(
            Numera.Discord.Gateway.RestApplicationCommandGateway.ModifyOptionLimit,
            manage.Options.Count);
        Assert.IsTrue(Numera.Discord.Gateway.RestApplicationCommandGateway.RequiresReplacement(manage));
    }

    [TestMethod]
    public void ARootWithinTheModifyLimitIsEditedInPlace()
    {
        GeneratedCommandManifestProvider provider = new();

        foreach (CommandManifestEntry entry in provider.PrimaryCommands())
        {
            if (entry.Options.Count
                <= Numera.Discord.Gateway.RestApplicationCommandGateway.ModifyOptionLimit)
            {
                Assert.IsFalse(
                    Numera.Discord.Gateway.RestApplicationCommandGateway.RequiresReplacement(entry),
                    entry.Name);
            }
        }
    }
}
