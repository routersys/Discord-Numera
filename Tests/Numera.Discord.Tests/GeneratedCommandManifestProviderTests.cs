using Numera.Discord.Commands;
using Numera.Discord.Routing;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class GeneratedCommandManifestProviderTests
{
    private static readonly GeneratedCommandGroup[] NoGroup = [];

    private static readonly GeneratedCommandOption[] NoOption = [];

    private static GeneratedCommandOption Option(
        string name,
        GeneratedOptionValueKind kind,
        bool required = true,
        bool autocomplete = false,
        GeneratedCommandChoice[]? choices = null) =>
        new(name, $"{name} の説明です。", kind, required, autocomplete, choices ?? []);

    [TestMethod]
    public void AnEmptyManifestProducesNoCommands()
    {
        GeneratedCommandManifestProvider provider = new([]);

        Assert.AreEqual(0, provider.PrimaryCommands().Count);
        Assert.AreEqual(0, provider.ControlCommands().Count);
    }

    [TestMethod]
    public void AnUngroupedCommandBecomesATopLevelEntry()
    {
        GeneratedCommandManifestProvider provider = new(
        [
            new GeneratedCommandDeclaration(
                GeneratedCommandKind.Slash,
                "help",
                "使い方を表示します。",
                NoGroup,
                NoOption),
        ]);

        CommandManifestEntry entry = provider.PrimaryCommands().Single();

        Assert.AreEqual(CommandManifestType.Slash, entry.Type);
        Assert.AreEqual("help", entry.Name);
        Assert.AreEqual("使い方を表示します。", entry.Description);
        Assert.AreEqual(0, entry.Options.Count);
    }

    [TestMethod]
    public void GroupedCommandsCollapseIntoOneRootWithSubcommands()
    {
        GeneratedCommandGroup[] bank = [new GeneratedCommandGroup("bank", "銀行の機能です。")];

        GeneratedCommandManifestProvider provider = new(
        [
            new GeneratedCommandDeclaration(
                GeneratedCommandKind.Slash, "transfer", "振り込みます。", bank, NoOption),
            new GeneratedCommandDeclaration(
                GeneratedCommandKind.Slash, "list", "一覧を表示します。", bank, NoOption),
        ]);

        CommandManifestEntry entry = provider.PrimaryCommands().Single();

        Assert.AreEqual("bank", entry.Name);
        Assert.AreEqual("銀行の機能です。", entry.Description);
        CollectionAssert.AreEqual(
            new[] { "list", "transfer" },
            entry.Options.Select(static option => option.Name).ToArray());

        foreach (CommandOptionManifest option in entry.Options)
        {
            Assert.AreEqual(GeneratedOptionType.SubCommand, option.Type);
        }
    }

    [TestMethod]
    public void ATwoLevelGroupBecomesASubcommandGroup()
    {
        GeneratedCommandGroup[] path =
        [
            new GeneratedCommandGroup("bank", "銀行の機能です。"),
            new GeneratedCommandGroup("card", "カードの機能です。"),
        ];

        GeneratedCommandManifestProvider provider = new(
        [
            new GeneratedCommandDeclaration(
                GeneratedCommandKind.Slash, "issue", "発行します。", path, NoOption),
        ]);

        CommandManifestEntry entry = provider.PrimaryCommands().Single();
        CommandOptionManifest group = entry.Options.Single();

        Assert.AreEqual("card", group.Name);
        Assert.AreEqual("カードの機能です。", group.Description);
        Assert.AreEqual(GeneratedOptionType.SubCommandGroup, group.Type);
        Assert.AreEqual("issue", group.Options.Single().Name);
        Assert.AreEqual(GeneratedOptionType.SubCommand, group.Options.Single().Type);
    }

    [TestMethod]
    public void OptionValueKindsMapToTheDiscordOptionTypes()
    {
        GeneratedCommandManifestProvider provider = new(
        [
            new GeneratedCommandDeclaration(
                GeneratedCommandKind.Slash,
                "transfer",
                "振り込みます。",
                NoGroup,
                [
                    Option("memo", GeneratedOptionValueKind.String),
                    Option("amount", GeneratedOptionValueKind.Integer),
                    Option("confirm", GeneratedOptionValueKind.Boolean),
                    Option("kind", GeneratedOptionValueKind.Enum),
                ]),
        ]);

        int[] types = [.. provider.PrimaryCommands().Single().Options.Select(static option => option.Type)];

        CollectionAssert.AreEqual(
            new[]
            {
                GeneratedOptionType.String,
                GeneratedOptionType.Integer,
                GeneratedOptionType.Boolean,
                GeneratedOptionType.String,
            },
            types);
    }

    [TestMethod]
    public void ChoicesAndAutocompleteSurviveTheMapping()
    {
        GeneratedCommandManifestProvider provider = new(
        [
            new GeneratedCommandDeclaration(
                GeneratedCommandKind.Slash,
                "statement",
                "明細を表示します。",
                NoGroup,
                [
                    Option("bank", GeneratedOptionValueKind.String, autocomplete: true),
                    Option(
                        "range",
                        GeneratedOptionValueKind.String,
                        required: false,
                        choices:
                        [
                            new GeneratedCommandChoice("今月", "THIS_MONTH"),
                            new GeneratedCommandChoice("先月", "LAST_MONTH"),
                        ]),
                ]),
        ]);

        IReadOnlyList<CommandOptionManifest> options = provider.PrimaryCommands().Single().Options;

        Assert.IsTrue(options[0].Autocomplete);
        Assert.AreEqual(0, options[0].Choices.Count);
        Assert.IsTrue(options[0].Required);

        Assert.IsFalse(options[1].Autocomplete);
        Assert.IsFalse(options[1].Required);
        CollectionAssert.AreEqual(
            new[] { "THIS_MONTH", "LAST_MONTH" },
            options[1].Choices.Select(static choice => choice.Value).ToArray());
    }

    [TestMethod]
    public void TheSystemCommandIsRoutedToTheControlScope()
    {
        GeneratedCommandManifestProvider provider = new(
        [
            new GeneratedCommandDeclaration(
                GeneratedCommandKind.Slash,
                "panel",
                "管理画面を開きます。",
                [new GeneratedCommandGroup("system", "システム管理です。")],
                NoOption),
            new GeneratedCommandDeclaration(
                GeneratedCommandKind.Slash, "help", "使い方を表示します。", NoGroup, NoOption),
        ]);

        Assert.AreEqual("system", provider.ControlCommands().Single().Name);
        Assert.AreEqual("help", provider.PrimaryCommands().Single().Name);
    }

    [TestMethod]
    public void AContextCommandNamedInJapaneseStaysPrimary()
    {
        GeneratedCommandManifestProvider provider = new(
        [
            new GeneratedCommandDeclaration(
                GeneratedCommandKind.User,
                "このユーザーへ振込",
                string.Empty,
                NoGroup,
                NoOption),
        ]);

        CommandManifestEntry entry = provider.PrimaryCommands().Single();

        Assert.AreEqual(CommandManifestType.User, entry.Type);
        Assert.AreEqual("このユーザーへ振込", entry.Name);
    }

    [TestMethod]
    public void TheGeneratedManifestIsConsumedByDefault()
    {
        GeneratedCommandManifestProvider provider = new();

        Assert.AreEqual(
            EconomyCommandManifest.Declarations.Length,
            provider.PrimaryCommands().Count + provider.ControlCommands().Count);
    }
}
