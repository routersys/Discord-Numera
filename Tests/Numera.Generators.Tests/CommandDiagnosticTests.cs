namespace Numera.Generators.Tests;

[TestClass]
public sealed class CommandDiagnosticTests
{
    private const string Prelude = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Numera.Discord.Abstractions;

        """;

    private static GeneratorRun Run(string body) => GeneratorHarness.Run(Prelude + body);

    [TestMethod]
    public void CanonicalCommandProducesNoDiagnostic()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "銀行の一覧を表示します。")]
                public Task<DiscordEndpointResponse> ListAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        CollectionAssert.AreEqual(Array.Empty<string>(), run.ErrorIds);
        StringAssert.Contains(run.ManifestSource, "\"list\"");
    }

    [TestMethod]
    public void DuplicateCommandNameIsRejected()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "銀行の一覧を表示します。")]
                public Task<DiscordEndpointResponse> ListAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());

                [EconomySlashCommand("list", "別の説明です。")]
                public Task<DiscordEndpointResponse> ListAgainAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD001"));
    }

    [TestMethod]
    [DataRow("List")]
    [DataRow("銀行")]
    [DataRow("-list")]
    [DataRow("list-")]
    [DataRow("li st")]
    public void InvalidCommandNameFormatIsRejected(string name)
    {
        GeneratorRun run = Run($$"""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("{{name}}", "銀行の一覧を表示します。")]
                public Task<DiscordEndpointResponse> ListAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD002"));
    }

    [TestMethod]
    public void OverlongCommandNameIsRejected()
    {
        GeneratorRun run = Run($$"""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("{{new string('a', 33)}}", "銀行の一覧を表示します。")]
                public Task<DiscordEndpointResponse> ListAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD003"));
    }

    [TestMethod]
    public void EmptyAndOverlongDescriptionIsRejected()
    {
        Assert.IsTrue(Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "")]
                public Task<DiscordEndpointResponse> ListAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """).HasError("ECONCMD004"));

        Assert.IsTrue(Run($$"""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "{{new string('あ', 101)}}")]
                public Task<DiscordEndpointResponse> ListAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """).HasError("ECONCMD004"));
    }

    [TestMethod]
    public void RequiredOptionAfterOptionalIsRejected()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("transfer", "振込を行います。")]
                public Task<DiscordEndpointResponse> TransferAsync(
                    [EconomyOption("memo", "振込メモです。", false)] string memo,
                    [EconomyOption("amount", "振込金額です。", true)] long amount,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD007"));
    }

    [TestMethod]
    public void RequiredBeforeOptionalIsAccepted()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("transfer", "振込を行います。")]
                public Task<DiscordEndpointResponse> TransferAsync(
                    [EconomyOption("amount", "振込金額です。", true)] long amount,
                    [EconomyOption("memo", "振込メモです。", false)] string memo,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsFalse(run.HasError("ECONCMD007"));
    }

    [TestMethod]
    public void DuplicateOptionNameIsRejected()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("transfer", "振込を行います。")]
                public Task<DiscordEndpointResponse> TransferAsync(
                    [EconomyOption("amount", "振込金額です。", true)] long first,
                    [EconomyOption("amount", "重複した名前です。", true)] long second,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD006"));
    }

    [TestMethod]
    public void ChoiceAndAutocompleteTogetherIsRejected()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomyAutocompleteProvider("bank")]
                public Task<IReadOnlyList<DiscordAutocompleteOption>> SuggestAsync(CancellationToken cancellationToken) =>
                    Task.FromResult<IReadOnlyList<DiscordAutocompleteOption>>([]);

                [EconomySlashCommand("open", "口座を開設します。")]
                public Task<DiscordEndpointResponse> OpenAsync(
                    [EconomyOption("bank", "銀行を選択します。", true)]
                    [EconomyChoice("第一銀行", "first")]
                    [EconomyAutocomplete("bank")] string bank,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD009"));
    }

    [TestMethod]
    public void UnknownAutocompleteProviderIsRejected()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("open", "口座を開設します。")]
                public Task<DiscordEndpointResponse> OpenAsync(
                    [EconomyOption("bank", "銀行を選択します。", true)]
                    [EconomyAutocomplete("missing")] string bank,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD010"));
    }

    [TestMethod]
    public void DuplicateComponentActionIsRejected()
    {
        GeneratorRun run = Run("""
            internal sealed class PanelEndpoints
            {
                [EconomyComponent(EconomyComponentKind.Button, "confirm")]
                public Task<DiscordEndpointResponse> ConfirmAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());

                [EconomyComponent(EconomyComponentKind.Select, "confirm")]
                public Task<DiscordEndpointResponse> ConfirmSelectAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD011"));
    }

    [TestMethod]
    public void InvalidReturnTypeIsRejected()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "銀行の一覧を表示します。")]
                public Task<string> ListAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(string.Empty);
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD014"));
    }

    [TestMethod]
    public void MissingCancellationTokenIsRejected()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "銀行の一覧を表示します。")]
                public Task<DiscordEndpointResponse> ListAsync() =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD015"));
    }

    [TestMethod]
    public void MisplacedCancellationTokenIsRejected()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "銀行の一覧を表示します。")]
                public Task<DiscordEndpointResponse> ListAsync(
                    CancellationToken cancellationToken,
                    [EconomyOption("amount", "金額です。", true)] long amount) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD015"));
    }

    [TestMethod]
    public void EmojiInDescriptionIsRejected()
    {
        GeneratorRun run = Run("""
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "銀行の一覧を表示します。\U0001F600")]
                public Task<DiscordEndpointResponse> ListAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD017"));
    }

    [TestMethod]
    public void ContextCommandWithDescriptionIsRejected()
    {
        GeneratorRun run = Run("""
            internal sealed class ContextEndpoints
            {
                [EconomyUserCommand("残高照会")]
                public Task<DiscordEndpointResponse> BalanceAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsFalse(run.HasError("ECONCMD016"));
    }

    [TestMethod]
    public void GroupPathIsProjectedIntoManifest()
    {
        GeneratorRun run = Run("""
            [EconomyCommandGroup("bank", "銀行機能です。")]
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "銀行の一覧を表示します。")]
                public Task<DiscordEndpointResponse> ListAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        CollectionAssert.AreEqual(Array.Empty<string>(), run.ErrorIds);
        StringAssert.Contains(run.ManifestSource, "\"bank list\"");
    }

    [TestMethod]
    public void ManifestOutputIsDeterministic()
    {
        const string source = """
            internal sealed class BankEndpoints
            {
                [EconomySlashCommand("zebra", "説明です。")]
                public Task<DiscordEndpointResponse> ZebraAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());

                [EconomySlashCommand("alpha", "説明です。")]
                public Task<DiscordEndpointResponse> AlphaAsync(CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """;

        string first = Run(source).ManifestSource;
        string second = Run(source).ManifestSource;

        Assert.AreEqual(first, second);
        Assert.IsLessThan(first.IndexOf("\"zebra\"", StringComparison.Ordinal), first.IndexOf("\"alpha\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ModalFieldCustomIdDuplicationIsRejected()
    {
        GeneratorRun run = Run("""
            [EconomyModalForm("振込")]
            internal sealed class TransferForm
            {
                [EconomyModalField("amount", "金額", EconomyModalFieldStyle.Short, true, 1, 20, "")]
                public string Amount { get; set; } = string.Empty;

                [EconomyModalField("amount", "メモ", EconomyModalFieldStyle.Short, false, 0, 100, "")]
                public string Memo { get; set; } = string.Empty;
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD022"));
    }

    [TestMethod]
    public void OverlongModalTitleIsRejected()
    {
        GeneratorRun run = Run($$"""
            [EconomyModalForm("{{new string('あ', 46)}}")]
            internal sealed class TransferForm
            {
                [EconomyModalField("amount", "金額", EconomyModalFieldStyle.Short, true, 1, 20, "")]
                public string Amount { get; set; } = string.Empty;
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD023"));
    }
}
