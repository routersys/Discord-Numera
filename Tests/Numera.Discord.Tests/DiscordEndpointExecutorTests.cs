using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Commands;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;

namespace Numera.Discord.Tests;

internal sealed class RecordingResponseSink : IDiscordResponseSink
{
    internal List<string> Calls { get; } = [];

    internal List<DiscordEmbedPayload> Embeds { get; } = [];

    internal List<DiscordModalPayload> Modals { get; } = [];

    internal List<IReadOnlyList<DiscordAutocompleteOption>> AutocompleteResults { get; } = [];

    internal int Deferrals { get; private set; }

    internal int LastDeferralEphemeral { get; private set; } = -1;

    public Task DeferAsync(bool ephemeral, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(DeferAsync));
        Deferrals++;
        LastDeferralEphemeral = ephemeral ? 1 : 0;
        return Task.CompletedTask;
    }

    public Task RespondAsync(DiscordEmbedPayload embed, bool ephemeral, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(RespondAsync));
        Embeds.Add(embed);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(DiscordEmbedPayload embed, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(UpdateAsync));
        Embeds.Add(embed);
        return Task.CompletedTask;
    }

    public Task ModifyOriginalResponseAsync(DiscordEmbedPayload embed, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(ModifyOriginalResponseAsync));
        Embeds.Add(embed);
        return Task.CompletedTask;
    }

    public Task RespondWithModalAsync(DiscordModalPayload modal, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(RespondWithModalAsync));
        Modals.Add(modal);
        return Task.CompletedTask;
    }

    public Task RespondWithAutocompleteAsync(
        IReadOnlyList<DiscordAutocompleteOption> options,
        CancellationToken cancellationToken)
    {
        Calls.Add(nameof(RespondWithAutocompleteAsync));
        AutocompleteResults.Add(options);
        return Task.CompletedTask;
    }
}

[TestClass]
public sealed class DiscordEndpointExecutorTests
{
    private const string ViewKey = "bank.transfer.accepted";

    private static readonly Dictionary<string, string> ViewData = new(StringComparer.Ordinal)
    {
        ["operationPublicId"] = "OP-1",
        ["customId"] = "bank:v1:modal:transfer:token",
    };

    private static TextCatalog Catalog() => TextCatalog.Create(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ViewKey + ".title"] = "Transfer accepted",
        [ViewKey + ".description"] = "The transfer is being processed.",
        [TextCatalogKeys.OperationFooter] = "Operation: {operationPublicId}",
        [TextCatalogKeys.ErrorNotFoundTitle] = "Not found",
        [TextCatalogKeys.ErrorNotFoundDescription] = "Check the target.",
        [TextCatalogKeys.ErrorFooter] = "Operation: {operationPublicId}",
    });

    private static (DiscordEndpointExecutor Executor, DiscordInteractionExchange Exchange, RecordingResponseSink Sink)
        Create(DiscordInteractionKind kind)
    {
        RecordingResponseSink sink = new();
        DiscordEndpointExecutor executor = new(new CatalogResponseComposer(Catalog()));

        return (executor, new DiscordInteractionExchange(kind, sink), sink);
    }

    private static DiscordEndpointResponse Message() => DiscordEndpointResponse.Message(ViewKey, ViewData);

    private static DiscordEndpointResponse Update() => DiscordEndpointResponse.UpdateMessage(ViewKey, ViewData);

    [TestMethod]
    public async Task SlashMessageUsesRespond()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.SlashCommand);

        ResponsePlanFailure failure = await executor.ExecuteAsync(exchange, Message(), CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.None, failure);
        CollectionAssert.AreEqual(new[] { "RespondAsync" }, sink.Calls);
        Assert.AreEqual("Transfer accepted", sink.Embeds[0].Title);
        Assert.AreEqual("Operation: OP-1", sink.Embeds[0].Footer);
    }

    [TestMethod]
    public async Task ComponentUpdateUsesUpdate()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.Button);

        ResponsePlanFailure failure = await executor.ExecuteAsync(exchange, Update(), CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.None, failure);
        CollectionAssert.AreEqual(new[] { "UpdateAsync" }, sink.Calls);
    }

    [TestMethod]
    public async Task DeferredCommandFinishesThroughModifyOriginalResponse()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.SlashCommand);

        await executor.DeferAsync(exchange, ephemeral: true, CancellationToken.None);
        ResponsePlanFailure failure = await executor.ExecuteAsync(exchange, Message(), CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.None, failure);
        CollectionAssert.AreEqual(new[] { "DeferAsync", "ModifyOriginalResponseAsync" }, sink.Calls);
    }

    [TestMethod]
    public async Task DeferredComponentPanelUpdateGoesThroughModifyOriginalResponse()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.Button);

        await executor.DeferAsync(exchange, ephemeral: true, CancellationToken.None);
        await executor.ExecuteAsync(exchange, Update(), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "DeferAsync", "ModifyOriginalResponseAsync" }, sink.Calls);
    }

    [TestMethod]
    public async Task SecondResponseIsRejectedWithoutTouchingDiscord()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.SlashCommand);

        await executor.ExecuteAsync(exchange, Message(), CancellationToken.None);
        ResponsePlanFailure second = await executor.ExecuteAsync(exchange, Message(), CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.AlreadyResponded, second);
        Assert.HasCount(1, sink.Calls);
    }

    [TestMethod]
    public async Task RepeatedDeferralIsRejected()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.SlashCommand);

        await executor.DeferAsync(exchange, ephemeral: true, CancellationToken.None);
        ResponsePlanFailure second = await executor.DeferAsync(exchange, ephemeral: true, CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.DeferralAlreadyPerformed, second);
        Assert.AreEqual(1, sink.Deferrals);
    }

    [TestMethod]
    public async Task DeferralAfterResponseIsRejected()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.SlashCommand);

        await executor.ExecuteAsync(exchange, Message(), CancellationToken.None);
        ResponsePlanFailure failure = await executor.DeferAsync(exchange, ephemeral: true, CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.AlreadyResponded, failure);
        Assert.AreEqual(0, sink.Deferrals);
    }

    [TestMethod]
    public async Task ModalAfterDeferralIsRejected()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.Button);

        await executor.DeferAsync(exchange, ephemeral: true, CancellationToken.None);

        ResponsePlanFailure failure = await executor.ExecuteModalAsync(
            exchange,
            DiscordEndpointResponse.Modal(ViewKey, ViewData),
            [],
            CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.ModalAfterDeferral, failure);
        Assert.IsEmpty(sink.Modals);
    }

    [TestMethod]
    public async Task ModalCarriesTheCustomIdFromViewData()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.SlashCommand);

        DiscordModalField field = new("amount", "Amount", null, null, 1, 20, Required: true);

        ResponsePlanFailure failure = await executor.ExecuteModalAsync(
            exchange,
            DiscordEndpointResponse.Modal(ViewKey, ViewData),
            [field],
            CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.None, failure);
        Assert.AreEqual("bank:v1:modal:transfer:token", sink.Modals[0].CustomId);
        Assert.AreEqual("Transfer accepted", sink.Modals[0].Title);
        Assert.HasCount(1, sink.Modals[0].Fields);
    }

    [TestMethod]
    public async Task ModalWithoutCustomIdIsAProgrammerError()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, _) =
            Create(DiscordInteractionKind.SlashCommand);

        Dictionary<string, string> withoutCustomId = new(StringComparer.Ordinal)
        {
            ["operationPublicId"] = "OP-1",
        };

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => executor.ExecuteModalAsync(
            exchange,
            DiscordEndpointResponse.Modal(ViewKey, withoutCustomId),
            [],
            CancellationToken.None));
    }

    [TestMethod]
    public async Task ModalFromMessageCommandIsRejectedByTheStateMachine()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.MessageCommand);

        ResponsePlanFailure failure = await executor.ExecuteModalAsync(
            exchange,
            DiscordEndpointResponse.Modal(ViewKey, ViewData),
            [],
            CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.ResponseKindNotPermitted, failure);
        Assert.IsEmpty(sink.Calls);
    }

    [TestMethod]
    public async Task AutocompleteIsNeverDeferred()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.Autocomplete);

        ResponsePlanFailure failure = await executor.DeferAsync(exchange, ephemeral: true, CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.DeferralNotPermitted, failure);
        Assert.IsEmpty(sink.Calls);
    }

    [TestMethod]
    public async Task AutocompleteResultsAreCappedAtTwentyFive()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.Autocomplete);

        List<DiscordAutocompleteOption> options = [];
        for (int index = 0; index < 40; index++)
        {
            options.Add(DiscordAutocompleteOption.Create($"name-{index}", $"value-{index}"));
        }

        ResponsePlanFailure failure =
            await executor.ExecuteAutocompleteAsync(exchange, options, CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.None, failure);
        Assert.HasCount(AutocompleteResultSet.MaximumResults, sink.AutocompleteResults[0]);
    }

    [TestMethod]
    public async Task AutocompleteFromASlashInteractionIsAProgrammerError()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, _) =
            Create(DiscordInteractionKind.SlashCommand);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            executor.ExecuteAutocompleteAsync(exchange, [], CancellationToken.None));
    }

    [TestMethod]
    public async Task NoContentFromASlashInteractionIsRejected()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.SlashCommand);

        ResponsePlanFailure failure = await executor.ExecuteAsync(
            exchange,
            DiscordEndpointResponse.NoContent(),
            CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.NoContentNotPermitted, failure);
        Assert.IsEmpty(sink.Calls);
    }

    [TestMethod]
    public async Task NoContentFromAButtonAcknowledgesWithDefer()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.Button);

        ResponsePlanFailure failure = await executor.ExecuteAsync(
            exchange,
            DiscordEndpointResponse.NoContent(),
            CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.None, failure);
        CollectionAssert.AreEqual(new[] { "DeferAsync" }, sink.Calls);
    }

    [TestMethod]
    public async Task NoContentAfterDeferralSendsNothing()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.Button);

        await executor.DeferAsync(exchange, ephemeral: true, CancellationToken.None);
        ResponsePlanFailure failure = await executor.ExecuteAsync(
            exchange,
            DiscordEndpointResponse.NoContent(),
            CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.None, failure);
        CollectionAssert.AreEqual(new[] { "DeferAsync" }, sink.Calls);
    }

    [TestMethod]
    public async Task ModalAndAutocompleteKindsAreRejectedByTheMessageOverload()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, _) =
            Create(DiscordInteractionKind.SlashCommand);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => executor.ExecuteAsync(
            exchange,
            DiscordEndpointResponse.Modal(ViewKey, ViewData),
            CancellationToken.None));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => executor.ExecuteAsync(
            exchange,
            DiscordEndpointResponse.Autocomplete(ViewKey, ViewData),
            CancellationToken.None));
    }

    [TestMethod]
    public async Task ErrorUsesRespondForCommandsAndUpdateForComponents()
    {
        RenderedError error = new ErrorRenderer(Catalog()).Render(
            ApplicationError.Create(ErrorCategory.NotFound, ErrorCodeFormat.Compose(ErrorCategory.NotFound, 1)),
            "OP-1");

        (DiscordEndpointExecutor commandExecutor, DiscordInteractionExchange command, RecordingResponseSink commandSink) =
            Create(DiscordInteractionKind.SlashCommand);
        await commandExecutor.ExecuteErrorAsync(command, error, CancellationToken.None);

        (DiscordEndpointExecutor buttonExecutor, DiscordInteractionExchange button, RecordingResponseSink buttonSink) =
            Create(DiscordInteractionKind.Button);
        await buttonExecutor.ExecuteErrorAsync(button, error, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "RespondAsync" }, commandSink.Calls);
        CollectionAssert.AreEqual(new[] { "UpdateAsync" }, buttonSink.Calls);
        Assert.AreEqual("Not found", commandSink.Embeds[0].Title);
    }

    [TestMethod]
    public async Task ErrorAfterAResponseNeverProducesASecondResponse()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.SlashCommand);

        RenderedError error = new ErrorRenderer(Catalog()).Render(
            ApplicationError.Create(ErrorCategory.NotFound, ErrorCodeFormat.Compose(ErrorCategory.NotFound, 1)),
            "OP-1");

        await executor.ExecuteAsync(exchange, Message(), CancellationToken.None);
        ResponsePlanFailure failure = await executor.ExecuteErrorAsync(exchange, error, CancellationToken.None);

        Assert.AreEqual(ResponsePlanFailure.AlreadyResponded, failure);
        Assert.HasCount(1, sink.Calls);
    }

    [TestMethod]
    public async Task EphemeralIntentReachesTheDeferral()
    {
        (DiscordEndpointExecutor executor, DiscordInteractionExchange exchange, RecordingResponseSink sink) =
            Create(DiscordInteractionKind.SlashCommand);

        await executor.DeferAsync(exchange, ephemeral: true, CancellationToken.None);

        Assert.AreEqual(1, sink.LastDeferralEphemeral);
    }
}
