namespace Numera.Generators.Tests;

[TestClass]
public sealed class GeneratedAdapterTests
{
    private const string Prelude = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Numera.Discord.Abstractions;

        namespace Sample;

        """;

    private static GeneratorRun Run(string body) => GeneratorHarness.Run(Prelude + body);

    private static void AssertCompiles(GeneratorRun run)
    {
        CollectionAssert.AreEqual(Array.Empty<string>(), run.ErrorIds);
        CollectionAssert.AreEqual(Array.Empty<string>(), run.CompilationErrors);
    }

    [TestMethod]
    public void AnEmptySurfaceProducesAnEmptyModuleList()
    {
        GeneratorRun run = Run("""
            public sealed class Empty
            {
            }
            """);

        AssertCompiles(run);
        StringAssert.Contains(run.AdapterSource, "internal static class EconomyGeneratedModules");
        StringAssert.Contains(run.AdapterSource, "internal static readonly Type[] All =");
        Assert.IsFalse(run.AdapterSource.Contains("typeof(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AnUngroupedSlashCommandGeneratesACompilingModule()
    {
        GeneratorRun run = Run("""
            public sealed class HelpEndpoints
            {
                [EconomySlashCommand("help", "使い方を表示します。")]
                public Task<DiscordEndpointResponse> ShowAsync(
                    DiscordEndpointContext context,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        AssertCompiles(run);
        StringAssert.Contains(run.AdapterSource, "public sealed class SampleHelpEndpointsModule : InteractionModuleBase<SocketInteractionContext>");
        StringAssert.Contains(run.AdapterSource, "[SlashCommand(\"help\", \"使い方を表示します。\")]");
        StringAssert.Contains(run.AdapterSource, "typeof(SampleHelpEndpointsModule)");
        StringAssert.Contains(run.AdapterSource, "dispatcher.CreateContextAsync(Context, \"help\", string.Empty, CancellationToken.None)");
        StringAssert.Contains(run.AdapterSource, "DiscordInteractionKind.SlashCommand");
    }

    [TestMethod]
    public void AGroupedSlashCommandCarriesTheGroupAttribute()
    {
        GeneratorRun run = Run("""
            [EconomyCommandGroup("bank", "銀行の機能です。")]
            public sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "銀行の一覧を表示します。")]
                public Task<DiscordEndpointResponse> ListAsync(
                    DiscordEndpointContext context,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        AssertCompiles(run);
        StringAssert.Contains(run.AdapterSource, "[Group(\"bank\", \"銀行の機能です。\")]");
        StringAssert.Contains(run.AdapterSource, "public sealed class BankModule");
        StringAssert.Contains(run.AdapterSource, "dispatcher.CreateContextAsync(Context, \"bank list\", string.Empty, CancellationToken.None)");
    }

    [TestMethod]
    public void TwoEndpointClassesUnderOneRootShareASingleModule()
    {
        GeneratorRun run = Run("""
            [EconomyCommandGroup("bank", "銀行の機能です。")]
            public sealed class BankQueryEndpoints
            {
                [EconomySlashCommand("list", "銀行の一覧を表示します。")]
                public Task<DiscordEndpointResponse> ListAsync(
                    DiscordEndpointContext context,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }

            [EconomyCommandGroup("bank", "銀行の機能です。")]
            public sealed class BankTransferEndpoints
            {
                [EconomySlashCommand("transfer", "振り込みます。")]
                public Task<DiscordEndpointResponse> TransferAsync(
                    DiscordEndpointContext context,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        AssertCompiles(run);
        Assert.AreEqual(1, Occurrences(run.AdapterSource, "public sealed class BankModule"));
        Assert.AreEqual(1, Occurrences(run.AdapterSource, "typeof("));
        StringAssert.Contains(run.AdapterSource, "global::Sample.BankQueryEndpoints bankQueryEndpoints");
        StringAssert.Contains(run.AdapterSource, "global::Sample.BankTransferEndpoints bankTransferEndpoints");
    }

    [TestMethod]
    public void ADepthTwoGroupBecomesANestedModule()
    {
        GeneratorRun run = Run("""
            [EconomyCommandGroup("bank", "銀行の機能です。")]
            public sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "銀行の一覧を表示します。")]
                public Task<DiscordEndpointResponse> ListAsync(
                    DiscordEndpointContext context,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());

                [EconomyCommandGroup("card", "カードの機能です。")]
                public sealed class CardEndpoints
                {
                    [EconomySlashCommand("issue", "カードを発行します。")]
                    public Task<DiscordEndpointResponse> IssueAsync(
                        DiscordEndpointContext context,
                        CancellationToken cancellationToken) =>
                        Task.FromResult(DiscordEndpointResponse.NoContent());
                }
            }
            """);

        AssertCompiles(run);
        StringAssert.Contains(run.AdapterSource, "[Group(\"bank\", \"銀行の機能です。\")]");
        StringAssert.Contains(run.AdapterSource, "[Group(\"card\", \"カードの機能です。\")]");
        StringAssert.Contains(run.AdapterSource, "public sealed class BankCardModule");
        StringAssert.Contains(run.AdapterSource, "dispatcher.CreateContextAsync(Context, \"bank card issue\", string.Empty, CancellationToken.None)");
        Assert.AreEqual(1, Occurrences(run.AdapterSource, "typeof("));
    }

    [TestMethod]
    public void OptionsBecomeSummaryAnnotatedParameters()
    {
        GeneratorRun run = Run("""
            public sealed class TransferEndpoints
            {
                [EconomySlashCommand("transfer", "振り込みます。")]
                public Task<DiscordEndpointResponse> TransferAsync(
                    DiscordEndpointContext context,
                    [EconomyOption("bank-code", "銀行を選びます。", true)] string bankCode,
                    [EconomyOption("amount", "金額を入力します。", true)] long amount,
                    [EconomyOption("memo", "摘要を入力します。", false)] string memo,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        AssertCompiles(run);
        StringAssert.Contains(run.AdapterSource, "[Summary(\"bank-code\", \"銀行を選びます。\")] string @bankCode");
        StringAssert.Contains(run.AdapterSource, "[Summary(\"amount\", \"金額を入力します。\")] long @amount");
        StringAssert.Contains(run.AdapterSource, "[Summary(\"memo\", \"摘要を入力します。\")] string @memo = default!");
    }

    [TestMethod]
    public void AnInvalidOptionOrderReportsEcondcmd007WithoutACompilerCascade()
    {
        GeneratorRun run = Run("""
            public sealed class TransferEndpoints
            {
                [EconomySlashCommand("transfer", "振り込みます。")]
                public Task<DiscordEndpointResponse> TransferAsync(
                    DiscordEndpointContext context,
                    [EconomyOption("memo", "摘要を入力します。", false)] string memo,
                    [EconomyOption("amount", "金額を入力します。", true)] long amount,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD007"));
        CollectionAssert.AreEqual(Array.Empty<string>(), run.CompilationErrors);

        int amount = run.AdapterSource.IndexOf("@amount", StringComparison.Ordinal);
        int memo = run.AdapterSource.IndexOf("@memo", StringComparison.Ordinal);

        Assert.IsLessThan(memo, amount, "任意 Option を末尾へ寄せないと CS1737 が重ねて出ます。");

        int memoArgument = run.AdapterSource.IndexOf("                @memo,", StringComparison.Ordinal);
        int amountArgument = run.AdapterSource.IndexOf("                @amount,", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, memoArgument);
        Assert.IsLessThan(amountArgument, memoArgument, "呼び出し引数は Endpoint の宣言順を保つ必要があります。");
    }

    [TestMethod]
    public void AnEnumOptionKeepsItsDeclaredType()
    {
        GeneratorRun run = Run("""
            public enum StatementRange
            {
                ThisMonth = 1,
                LastMonth = 2,
            }

            public sealed class StatementEndpoints
            {
                [EconomySlashCommand("statement", "明細を表示します。")]
                public Task<DiscordEndpointResponse> StatementAsync(
                    DiscordEndpointContext context,
                    [EconomyOption("range", "期間を選びます。", true)] StatementRange range,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        AssertCompiles(run);
        StringAssert.Contains(run.AdapterSource, "global::Sample.StatementRange @range");
    }

    [TestMethod]
    public void AContextCommandGeneratesTheDiscordInput()
    {
        GeneratorRun run = Run("""
            public sealed class ContextEndpoints
            {
                [EconomyUserCommand("このユーザーへ振込")]
                public Task<DiscordEndpointResponse> TransferToUserAsync(
                    DiscordEndpointContext context,
                    DiscordUserInput input,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());

                [EconomyMessageCommand("送信者へ振込")]
                public Task<DiscordEndpointResponse> TransferToAuthorAsync(
                    DiscordEndpointContext context,
                    DiscordMessageInput input,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        AssertCompiles(run);
        StringAssert.Contains(run.AdapterSource, "[UserCommand(\"このユーザーへ振込\")]");
        StringAssert.Contains(run.AdapterSource, "[MessageCommand(\"送信者へ振込\")]");
        StringAssert.Contains(run.AdapterSource, "dispatcher.CreateUserInput(user)");
        StringAssert.Contains(run.AdapterSource, "dispatcher.CreateMessageInput(message)");
        StringAssert.Contains(run.AdapterSource, "DiscordInteractionKind.UserCommand");
        StringAssert.Contains(run.AdapterSource, "DiscordInteractionKind.MessageCommand");
    }

    [TestMethod]
    public void TheAdapterCallsTheEndpointExactlyOnce()
    {
        GeneratorRun run = Run("""
            public sealed class HelpEndpoints
            {
                [EconomySlashCommand("help", "使い方を表示します。")]
                public Task<DiscordEndpointResponse> ShowAsync(
                    DiscordEndpointContext context,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        AssertCompiles(run);
        Assert.AreEqual(1, Occurrences(run.AdapterSource, "helpEndpoints.ShowAsync("));
        Assert.AreEqual(1, Occurrences(run.AdapterSource, "dispatcher.DispatchAsync("));
    }

    [TestMethod]
    public void TheAdapterCarriesNoBusinessLogic()
    {
        GeneratorRun run = Run("""
            [EconomyCommandGroup("bank", "銀行の機能です。")]
            public sealed class BankEndpoints
            {
                [EconomySlashCommand("list", "銀行の一覧を表示します。")]
                public Task<DiscordEndpointResponse> ListAsync(
                    DiscordEndpointContext context,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        AssertCompiles(run);

        foreach (string forbidden in new[] { "Repository", "IBankingUnitOfWork", "SELECT", "MoneyMinor", "Ledger" })
        {
            Assert.IsFalse(
                run.AdapterSource.Contains(forbidden, StringComparison.Ordinal),
                forbidden);
        }
    }


    [TestMethod]
    public void AnAutocompleteOptionGeneratesAnAutocompleteCommand()
    {
        GeneratorRun run = Run("""
            [EconomyCommandGroup("bank", "銀行の機能です。")]
            public sealed class BankEndpoints
            {
                [EconomySlashCommand("open", "口座を開設します。")]
                public Task<DiscordEndpointResponse> OpenAsync(
                    DiscordEndpointContext context,
                    [EconomyOption("bank", "銀行を選びます。", true)]
                    [EconomyAutocomplete("bank-suggest")]
                    string bank,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());

                [EconomyAutocompleteProvider("bank-suggest")]
                public Task<IReadOnlyList<DiscordAutocompleteOption>> SuggestBanksAsync(
                    DiscordAutocompleteRequest request,
                    CancellationToken cancellationToken) =>
                    Task.FromResult<IReadOnlyList<DiscordAutocompleteOption>>([]);
            }
            """);

        AssertCompiles(run);
        StringAssert.Contains(run.AdapterSource, "[AutocompleteCommand(\"bank\", \"open\")]");
        StringAssert.Contains(run.AdapterSource, "dispatcher.CreateAutocompleteRequest(Context, \"bank open\")");
        StringAssert.Contains(run.AdapterSource, "dispatcher.DispatchAutocompleteAsync(");
        StringAssert.Contains(run.AdapterSource, "bankEndpoints.SuggestBanksAsync(");
    }

    [TestMethod]
    public void AnOptionWithoutAutocompleteGeneratesNoAutocompleteCommand()
    {
        GeneratorRun run = Run("""
            public sealed class HelpEndpoints
            {
                [EconomySlashCommand("help", "使い方を表示します。")]
                public Task<DiscordEndpointResponse> ShowAsync(
                    DiscordEndpointContext context,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        AssertCompiles(run);
        Assert.IsFalse(run.AdapterSource.Contains("AutocompleteCommand", StringComparison.Ordinal));
    }
    private static int Occurrences(string text, string value)
    {
        int count = 0;

        for (int index = text.IndexOf(value, StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [TestMethod]
    public void AComponentEndpointGeneratesAWildcardAdapter()
    {
        GeneratorRun run = Run("""
            public sealed class PanelEndpoints
            {
                [EconomyComponent(EconomyComponentKind.Button, "transfer-confirm")]
                public Task<DiscordEndpointResponse> ConfirmAsync(
                    DiscordEndpointContext context,
                    DiscordComponentInput input,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());

                [EconomyComponent(EconomyComponentKind.Select, "source-account")]
                public Task<DiscordEndpointResponse> SelectAsync(
                    DiscordEndpointContext context,
                    DiscordComponentInput input,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        AssertCompiles(run);
        StringAssert.Contains(
            run.AdapterSource,
            "public sealed class SamplePanelEndpointsInteractionsModule : InteractionModuleBase<SocketInteractionContext>");
        StringAssert.Contains(run.AdapterSource, "[ComponentInteraction(\"bank:v1:btn:transfer-confirm:*\", true)]");
        StringAssert.Contains(run.AdapterSource, "[ComponentInteraction(\"bank:v1:sel:source-account:*\", true)]");
        StringAssert.Contains(
            run.AdapterSource,
            "public async Task SourceAccountSelectAdapter(string sessionToken, string[] values)");
        StringAssert.Contains(run.AdapterSource, "public async Task TransferConfirmButtonAdapter(string sessionToken)");
        StringAssert.Contains(run.AdapterSource, "new DiscordComponentInput(\"source-account\", sessionToken, values),");
        StringAssert.Contains(run.AdapterSource, "DiscordInteractionKind.SelectMenu");
        StringAssert.Contains(run.AdapterSource, "DiscordInteractionKind.Button");
        StringAssert.Contains(run.AdapterSource, "typeof(SamplePanelEndpointsInteractionsModule)");
    }

    [TestMethod]
    public void AModalEndpointGeneratesATransportFormAndACatalog()
    {
        GeneratorRun run = Run("""
            [EconomyModalForm("振込内容の入力")]
            public sealed class TransferForm
            {
                [EconomyModalField("bank-code", "金融機関コード", EconomyModalFieldStyle.Short, true, 1, 16, "金融機関コードを入力してください")]
                public string BankCode { get; set; } = string.Empty;

                [EconomyModalField("memo", "メモ", EconomyModalFieldStyle.Paragraph, false, 0, 100, "必要な場合だけ入力してください")]
                public string Memo { get; set; } = string.Empty;
            }

            public sealed class PanelEndpoints
            {
                [EconomyModal("transfer", typeof(TransferForm))]
                public Task<DiscordEndpointResponse> SubmitAsync(
                    DiscordEndpointContext context,
                    TransferForm form,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        AssertCompiles(run);
        StringAssert.Contains(run.AdapterSource, "public sealed class TransferFormTransport : IModal");
        StringAssert.Contains(run.AdapterSource, "public string Title => \"振込内容の入力\";");
        StringAssert.Contains(
            run.AdapterSource,
            "[ModalTextInput(\"memo\", global::Discord.TextInputStyle.Paragraph, \"必要な場合だけ入力してください\", 0, 100)]");
        StringAssert.Contains(run.AdapterSource, "[RequiredInput(false)]");
        StringAssert.Contains(run.AdapterSource, "[ModalInteraction(\"bank:v1:modal:transfer:*\", true)]");
        StringAssert.Contains(
            run.AdapterSource,
            "public async Task TransferModalAdapter(string sessionToken, TransferFormTransport transport)");
        StringAssert.Contains(run.AdapterSource, "BankCode = transport.BankCode,");
        StringAssert.Contains(run.AdapterSource, "DiscordInteractionKind.ModalSubmit");
        StringAssert.Contains(run.AdapterSource, "internal sealed class EconomyGeneratedModalFormCatalog : IModalFormCatalog");
        StringAssert.Contains(run.AdapterSource, "\"transfer\" => TransferFields,");
        StringAssert.Contains(
            run.AdapterSource,
            "new DiscordModalFieldDefinition(\"memo\", \"メモ\", \"必要な場合だけ入力してください\", EconomyModalFieldStyle.Paragraph, false, 0, 100),");
    }

    [TestMethod]
    public void AModalWhoseFormLacksTheFormAttributeIsRejected()
    {
        GeneratorRun run = Run("""
            public sealed class TransferForm
            {
                public string BankCode { get; set; } = string.Empty;
            }

            public sealed class PanelEndpoints
            {
                [EconomyModal("transfer", typeof(TransferForm))]
                public Task<DiscordEndpointResponse> SubmitAsync(
                    DiscordEndpointContext context,
                    TransferForm form,
                    CancellationToken cancellationToken) =>
                    Task.FromResult(DiscordEndpointResponse.NoContent());
            }
            """);

        Assert.IsTrue(run.HasError("ECONCMD027"));
    }
}
