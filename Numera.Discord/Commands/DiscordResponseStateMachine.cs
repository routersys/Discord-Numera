using Numera.Discord.Abstractions;

namespace Numera.Discord.Commands;

public enum DiscordInteractionKind
{
    SlashCommand = 1,
    UserCommand = 2,
    MessageCommand = 3,
    Button = 4,
    SelectMenu = 5,
    ModalSubmit = 6,
    Autocomplete = 7,
}

public enum DiscordResponseOperation
{
    Respond = 1,
    UpdateMessage = 2,
    RespondWithModal = 3,
    RespondWithAutocomplete = 4,
    Defer = 5,
    ModifyOriginalResponse = 6,
}

public enum ResponsePlanFailure
{
    None = 0,
    AlreadyResponded = 1,
    DeferralNotPermitted = 2,
    ResponseKindNotPermitted = 3,
    NoContentNotPermitted = 4,
    ModalAfterDeferral = 5,
    AutocompleteAfterDeferral = 6,
    DeferralAlreadyPerformed = 7,
}

public readonly struct ResponsePlan
{
    private ResponsePlan(DiscordResponseOperation operation, ResponsePlanFailure failure)
    {
        Operation = operation;
        Failure = failure;
    }

    public DiscordResponseOperation Operation { get; }

    public ResponsePlanFailure Failure { get; }

    public bool IsPermitted => Failure == ResponsePlanFailure.None;

    internal static ResponsePlan Allow(DiscordResponseOperation operation) =>
        new(operation, ResponsePlanFailure.None);

    internal static ResponsePlan Reject(ResponsePlanFailure failure) => new(default, failure);
}

public sealed class DiscordResponseStateMachine
{
    private readonly DiscordInteractionKind kind;

    public DiscordResponseStateMachine(DiscordInteractionKind kind) => this.kind = kind;

    public bool HasDeferred { get; private set; }

    public bool HasResponded { get; private set; }

    public static bool SupportsDeferral(DiscordInteractionKind kind) =>
        kind is DiscordInteractionKind.SlashCommand
            or DiscordInteractionKind.UserCommand
            or DiscordInteractionKind.MessageCommand
            or DiscordInteractionKind.Button
            or DiscordInteractionKind.SelectMenu
            or DiscordInteractionKind.ModalSubmit;

    public ResponsePlan PlanDeferral()
    {
        if (HasResponded)
        {
            return ResponsePlan.Reject(ResponsePlanFailure.AlreadyResponded);
        }

        if (HasDeferred)
        {
            return ResponsePlan.Reject(ResponsePlanFailure.DeferralAlreadyPerformed);
        }

        return SupportsDeferral(kind)
            ? ResponsePlan.Allow(DiscordResponseOperation.Defer)
            : ResponsePlan.Reject(ResponsePlanFailure.DeferralNotPermitted);
    }

    public ResponsePlan PlanResponse(DiscordResponseKind responseKind)
    {
        if (HasResponded)
        {
            return ResponsePlan.Reject(ResponsePlanFailure.AlreadyResponded);
        }

        return HasDeferred ? PlanAfterDeferral(responseKind) : PlanInitial(responseKind);
    }

    public void RecordDeferral() => HasDeferred = true;

    public void RecordResponse() => HasResponded = true;

    private ResponsePlan PlanInitial(DiscordResponseKind responseKind) => responseKind switch
    {
        DiscordResponseKind.Message => IsCommandOrModalSubmit(kind)
            ? ResponsePlan.Allow(DiscordResponseOperation.Respond)
            : ResponsePlan.Reject(ResponsePlanFailure.ResponseKindNotPermitted),

        DiscordResponseKind.UpdateMessage => IsComponent(kind)
            ? ResponsePlan.Allow(DiscordResponseOperation.UpdateMessage)
            : ResponsePlan.Reject(ResponsePlanFailure.ResponseKindNotPermitted),

        DiscordResponseKind.Modal => kind is DiscordInteractionKind.SlashCommand
            or DiscordInteractionKind.UserCommand
            or DiscordInteractionKind.Button
            or DiscordInteractionKind.SelectMenu
            ? ResponsePlan.Allow(DiscordResponseOperation.RespondWithModal)
            : ResponsePlan.Reject(ResponsePlanFailure.ResponseKindNotPermitted),

        DiscordResponseKind.Autocomplete => kind == DiscordInteractionKind.Autocomplete
            ? ResponsePlan.Allow(DiscordResponseOperation.RespondWithAutocomplete)
            : ResponsePlan.Reject(ResponsePlanFailure.ResponseKindNotPermitted),

        DiscordResponseKind.NoContent => IsComponent(kind)
            ? ResponsePlan.Allow(DiscordResponseOperation.Defer)
            : ResponsePlan.Reject(ResponsePlanFailure.NoContentNotPermitted),

        _ => ResponsePlan.Reject(ResponsePlanFailure.ResponseKindNotPermitted),
    };

    private ResponsePlan PlanAfterDeferral(DiscordResponseKind responseKind) => responseKind switch
    {
        DiscordResponseKind.Message or DiscordResponseKind.UpdateMessage =>
            ResponsePlan.Allow(DiscordResponseOperation.ModifyOriginalResponse),

        DiscordResponseKind.Modal => ResponsePlan.Reject(ResponsePlanFailure.ModalAfterDeferral),

        DiscordResponseKind.Autocomplete => ResponsePlan.Reject(ResponsePlanFailure.AutocompleteAfterDeferral),

        DiscordResponseKind.NoContent => IsComponent(kind)
            ? ResponsePlan.Allow(DiscordResponseOperation.ModifyOriginalResponse)
            : ResponsePlan.Reject(ResponsePlanFailure.NoContentNotPermitted),

        _ => ResponsePlan.Reject(ResponsePlanFailure.ResponseKindNotPermitted),
    };

    private static bool IsCommandOrModalSubmit(DiscordInteractionKind kind) =>
        kind is DiscordInteractionKind.SlashCommand
            or DiscordInteractionKind.UserCommand
            or DiscordInteractionKind.MessageCommand
            or DiscordInteractionKind.ModalSubmit;

    private static bool IsComponent(DiscordInteractionKind kind) =>
        kind is DiscordInteractionKind.Button or DiscordInteractionKind.SelectMenu;
}
