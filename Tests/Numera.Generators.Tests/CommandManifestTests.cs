namespace Numera.Generators.Tests;

[TestClass]
public sealed class CommandManifestTests
{
    private const string Prelude = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Numera.Discord.Abstractions;

        """;

    private static GeneratorRun Run(string body) => GeneratorHarness.Run(Prelude + body);

    [TestMethod]
    public void AnEmptySurfaceStillDeclaresTheManifestTypes()
    {
        GeneratorRun run = Run("""
            internal sealed class Empty
            {
            }
            """);

        CollectionAssert.AreEqual(Array.Empty<string>(), run.ErrorIds);
        StringAssert.Contains(run.ManifestSource, "internal sealed record GeneratedCommandDeclaration(");
        StringAssert.Contains(run.ManifestSource, "internal static readonly GeneratedCommandDeclaration[] Declarations =");
    }

    [TestMethod]
    public void TheDeclarationCarriesTheDescriptionAndOptions()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("transfer", "他の利用者へ振り込みます。")]
                public Task<DiscordEndpointResponse> TransferAsync(
                    DiscordEndpointContext context,
                    [EconomyOption("bank", "送金元の銀行を選びます。", true)]
                    [EconomyAutocomplete("bank-suggest")]
                    string bank,
                    [EconomyOption("amount", "振込金額を入力します。", true)] long amount,
                    [EconomyOption("memo", "摘要を入力します。", false)] string memo,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());

                [EconomyAutocompleteProvider("bank-suggest")]
                public Task<IReadOnlyList<DiscordAutocompleteOption>> SuggestBanksAsync(
                    DiscordAutocompleteRequest request,
                    CancellationToken cancellationToken) =>
                    Task.FromResult<IReadOnlyList<DiscordAutocompleteOption>>([]);
            }
            """);

        CollectionAssert.AreEqual(Array.Empty<string>(), run.ErrorIds);
        StringAssert.Contains(run.ManifestSource, "GeneratedCommandKind.Slash");
        StringAssert.Contains(run.ManifestSource, "\"他の利用者へ振り込みます。\"");
        StringAssert.Contains(run.ManifestSource, "\"bank\"");
        StringAssert.Contains(run.ManifestSource, "GeneratedOptionValueKind.String");
        StringAssert.Contains(run.ManifestSource, "GeneratedOptionValueKind.Integer");
    }

    [TestMethod]
    public void AnAutocompleteOptionIsMarkedAndAPlainOptionIsNot()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("transfer", "他の利用者へ振り込みます。")]
                public Task<DiscordEndpointResponse> TransferAsync(
                    DiscordEndpointContext context,
                    [EconomyOption("bank", "送金元の銀行を選びます。", true)]
                    [EconomyAutocomplete("bank-suggest")]
                    string bank,
                    [EconomyOption("memo", "摘要を入力します。", false)] string memo,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());

                [EconomyAutocompleteProvider("bank-suggest")]
                public Task<IReadOnlyList<DiscordAutocompleteOption>> SuggestBanksAsync(
                    DiscordAutocompleteRequest request,
                    CancellationToken cancellationToken) =>
                    Task.FromResult<IReadOnlyList<DiscordAutocompleteOption>>([]);
            }
            """);

        CollectionAssert.AreEqual(Array.Empty<string>(), run.ErrorIds);

        string bank = OptionBlock(run.ManifestSource, "bank");
        string memo = OptionBlock(run.ManifestSource, "memo");

        StringAssert.Contains(bank, "true,\n                    true,".ReplaceLineEndings());
        StringAssert.Contains(memo, "false,\n                    false,".ReplaceLineEndings());
    }

    [TestMethod]
    public void ChoicesAreEmittedAsNameAndValuePairs()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("statement", "取引明細を表示します。")]
                public Task<DiscordEndpointResponse> StatementAsync(
                    DiscordEndpointContext context,
                    [EconomyOption("range", "期間を選びます。", true)]
                    [EconomyChoice("今月", "THIS_MONTH")]
                    [EconomyChoice("先月", "LAST_MONTH")]
                    string range,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        CollectionAssert.AreEqual(Array.Empty<string>(), run.ErrorIds);
        StringAssert.Contains(run.ManifestSource, "new GeneratedCommandChoice(\"今月\", \"THIS_MONTH\")");
        StringAssert.Contains(run.ManifestSource, "new GeneratedCommandChoice(\"先月\", \"LAST_MONTH\")");
    }

    [TestMethod]
    public void TheGroupPathCarriesTheGroupDescription()
    {
        GeneratorRun run = Run("""
            [EconomyCommandGroup("bank", "銀行の機能をまとめます。")]
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "銀行の一覧を表示します。")]
                public Task<DiscordEndpointResponse> ListAsync(
                    DiscordEndpointContext context,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        CollectionAssert.AreEqual(Array.Empty<string>(), run.ErrorIds);
        StringAssert.Contains(
            run.ManifestSource,
            "new GeneratedCommandGroup(\"bank\", \"銀行の機能をまとめます。\")");
        StringAssert.Contains(run.ManifestSource, "\"bank list\"");
    }

    [TestMethod]
    public void AContextCommandKeepsItsJapaneseName()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomyUserCommand("このユーザーへ振込")]
                public Task<DiscordEndpointResponse> TransferToUserAsync(
                    DiscordEndpointContext context,
                    DiscordUserInput input,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        CollectionAssert.AreEqual(Array.Empty<string>(), run.ErrorIds);
        StringAssert.Contains(run.ManifestSource, "GeneratedCommandKind.User");
        StringAssert.Contains(run.ManifestSource, "\"このユーザーへ振込\"");
    }

    private static string OptionBlock(string manifest, string optionName)
    {
        int start = manifest.IndexOf($"\"{optionName}\",", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, optionName);

        int end = manifest.IndexOf("new GeneratedCommandOption(", start, StringComparison.Ordinal);
        return end < 0 ? manifest[start..] : manifest[start..end];
    }
}
