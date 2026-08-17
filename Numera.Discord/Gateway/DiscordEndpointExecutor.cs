using Numera.Discord.Abstractions;
using Numera.Discord.Commands;
using Numera.Discord.Rendering;

namespace Numera.Discord.Gateway;

internal static class ExecutorFailure
{
    internal const string ResponseKindNotExecutable =
        "Message, UpdateMessage and NoContent are the only kinds this overload executes.";

    internal const string ModalKindRequired = "A modal execution requires a modal response kind.";

    internal const string AutocompleteKindRequired =
        "An autocomplete execution requires an autocomplete interaction.";
}

internal sealed class DiscordInteractionExchange
{
    internal DiscordInteractionExchange(DiscordInteractionKind kind, IDiscordResponseSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        Kind = kind;
        Sink = sink;
        State = new DiscordResponseStateMachine(kind);
    }

    internal DiscordInteractionKind Kind { get; }

    internal IDiscordResponseSink Sink { get; }

    internal DiscordResponseStateMachine State { get; }
}

internal interface IDiscordEndpointExecutor
{
    Task<ResponsePlanFailure> DeferAsync(
        DiscordInteractionExchange exchange,
        bool ephemeral,
        CancellationToken cancellationToken);

    Task<ResponsePlanFailure> ExecuteAsync(
        DiscordInteractionExchange exchange,
        DiscordEndpointResponse response,
        CancellationToken cancellationToken);

    Task<ResponsePlanFailure> ExecuteModalAsync(
        DiscordInteractionExchange exchange,
        DiscordEndpointResponse response,
        IReadOnlyList<DiscordModalField> fields,
        CancellationToken cancellationToken);

    Task<ResponsePlanFailure> ExecuteAutocompleteAsync(
        DiscordInteractionExchange exchange,
        IReadOnlyList<DiscordAutocompleteOption> options,
        CancellationToken cancellationToken);

    Task<ResponsePlanFailure> ExecuteErrorAsync(
        DiscordInteractionExchange exchange,
        RenderedError error,
        CancellationToken cancellationToken);
}

internal sealed class DiscordEndpointExecutor : IDiscordEndpointExecutor
{
    private readonly IDiscordResponseComposer composer;

    public DiscordEndpointExecutor(IDiscordResponseComposer composer)
    {
        ArgumentNullException.ThrowIfNull(composer);
        this.composer = composer;
    }

    public async Task<ResponsePlanFailure> DeferAsync(
        DiscordInteractionExchange exchange,
        bool ephemeral,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exchange);

        ResponsePlan plan = exchange.State.PlanDeferral();

        if (!plan.IsPermitted)
        {
            return plan.Failure;
        }

        exchange.State.RecordDeferral();
        await exchange.Sink.DeferAsync(ephemeral, cancellationToken).ConfigureAwait(false);

        return ResponsePlanFailure.None;
    }

    public async Task<ResponsePlanFailure> ExecuteAsync(
        DiscordInteractionExchange exchange,
        DiscordEndpointResponse response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(response);

        if (response.Kind is not (DiscordResponseKind.Message
            or DiscordResponseKind.UpdateMessage
            or DiscordResponseKind.NoContent))
        {
            throw new ArgumentException(ExecutorFailure.ResponseKindNotExecutable, nameof(response));
        }

        ResponsePlan plan = exchange.State.PlanResponse(response.Kind);

        if (!plan.IsPermitted)
        {
            return plan.Failure;
        }

        if (plan.Operation == DiscordResponseOperation.Defer)
        {
            exchange.State.RecordDeferral();
            await exchange.Sink.DeferAsync(response.Ephemeral, cancellationToken).ConfigureAwait(false);

            return ResponsePlanFailure.None;
        }

        if (response.Kind == DiscordResponseKind.NoContent)
        {
            exchange.State.RecordResponse();

            return ResponsePlanFailure.None;
        }

        exchange.State.RecordResponse();

        DiscordEmbedPayload embed = composer.Compose(response);
        await DispatchAsync(exchange, plan.Operation, embed, response.Ephemeral, cancellationToken)
            .ConfigureAwait(false);

        return ResponsePlanFailure.None;
    }

    public async Task<ResponsePlanFailure> ExecuteModalAsync(
        DiscordInteractionExchange exchange,
        DiscordEndpointResponse response,
        IReadOnlyList<DiscordModalField> fields,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(fields);

        if (response.Kind != DiscordResponseKind.Modal)
        {
            throw new ArgumentException(ExecutorFailure.ModalKindRequired, nameof(response));
        }

        ResponsePlan plan = exchange.State.PlanResponse(DiscordResponseKind.Modal);

        if (!plan.IsPermitted)
        {
            return plan.Failure;
        }

        string customId = composer.ResolveModalCustomId(response);
        DiscordEmbedPayload embed = composer.Compose(response);

        exchange.State.RecordResponse();

        await exchange.Sink
            .RespondWithModalAsync(new DiscordModalPayload(customId, embed.Title, fields), cancellationToken)
            .ConfigureAwait(false);

        return ResponsePlanFailure.None;
    }

    public async Task<ResponsePlanFailure> ExecuteAutocompleteAsync(
        DiscordInteractionExchange exchange,
        IReadOnlyList<DiscordAutocompleteOption> options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(options);

        if (exchange.Kind != DiscordInteractionKind.Autocomplete)
        {
            throw new ArgumentException(ExecutorFailure.AutocompleteKindRequired, nameof(exchange));
        }

        ResponsePlan plan = exchange.State.PlanResponse(DiscordResponseKind.Autocomplete);

        if (!plan.IsPermitted)
        {
            return plan.Failure;
        }

        AutocompleteDelivery delivery = AutocompleteResultSet.Enforce(options);

        exchange.State.RecordResponse();
        await exchange.Sink.RespondWithAutocompleteAsync(delivery.Options, cancellationToken).ConfigureAwait(false);

        return ResponsePlanFailure.None;
    }

    public async Task<ResponsePlanFailure> ExecuteErrorAsync(
        DiscordInteractionExchange exchange,
        RenderedError error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(error);

        DiscordResponseKind kind = exchange.Kind is DiscordInteractionKind.Button
            or DiscordInteractionKind.SelectMenu
            ? DiscordResponseKind.UpdateMessage
            : DiscordResponseKind.Message;

        ResponsePlan plan = exchange.State.PlanResponse(kind);

        if (!plan.IsPermitted)
        {
            return plan.Failure;
        }

        exchange.State.RecordResponse();

        DiscordEmbedPayload embed = composer.Compose(error);
        await DispatchAsync(exchange, plan.Operation, embed, error.Ephemeral, cancellationToken)
            .ConfigureAwait(false);

        return ResponsePlanFailure.None;
    }

    private static Task DispatchAsync(
        DiscordInteractionExchange exchange,
        DiscordResponseOperation operation,
        DiscordEmbedPayload embed,
        bool ephemeral,
        CancellationToken cancellationToken) => operation switch
        {
            DiscordResponseOperation.Respond => exchange.Sink.RespondAsync(embed, ephemeral, cancellationToken),
            DiscordResponseOperation.UpdateMessage => exchange.Sink.UpdateAsync(embed, cancellationToken),
            _ => exchange.Sink.ModifyOriginalResponseAsync(embed, cancellationToken),
        };
}
