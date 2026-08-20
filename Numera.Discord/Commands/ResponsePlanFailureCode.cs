namespace Numera.Discord.Commands;

internal static class ResponsePlanFailureCode
{
    internal const string Prefix = "RESPONSE-PLAN-";

    internal static string Of(ResponsePlanFailure failure) => failure switch
    {
        ResponsePlanFailure.None => Prefix + "NONE",
        ResponsePlanFailure.AlreadyResponded => Prefix + "ALREADY-RESPONDED",
        ResponsePlanFailure.DeferralNotPermitted => Prefix + "DEFERRAL-NOT-PERMITTED",
        ResponsePlanFailure.ResponseKindNotPermitted => Prefix + "RESPONSE-KIND-NOT-PERMITTED",
        ResponsePlanFailure.NoContentNotPermitted => Prefix + "NO-CONTENT-NOT-PERMITTED",
        ResponsePlanFailure.ModalAfterDeferral => Prefix + "MODAL-AFTER-DEFERRAL",
        ResponsePlanFailure.AutocompleteAfterDeferral => Prefix + "AUTOCOMPLETE-AFTER-DEFERRAL",
        ResponsePlanFailure.DeferralAlreadyPerformed => Prefix + "DEFERRAL-ALREADY-PERFORMED",
        _ => Prefix + "UNKNOWN",
    };
}
