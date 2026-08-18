using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum PrudentialPolicyVersionStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3,
}

public static class PrudentialPolicyVersionStatusCatalog
{
    private static readonly StateTransitionTable<PrudentialPolicyVersionStatus> Transitions =
        StateTransitionTable<PrudentialPolicyVersionStatus>
            .Create(InvariantViolationCode.PrudentialPolicyVersionTransitionInvalid)
            .AllowCreation(PrudentialPolicyVersionStatus.Draft)
            .Allow(
                PrudentialPolicyVersionStatus.Draft,
                PrudentialPolicyVersionStatus.Published,
                PrudentialPolicyVersionStatus.Retired)
            .Allow(PrudentialPolicyVersionStatus.Published, PrudentialPolicyVersionStatus.Retired)
            .Build();

    public static void EnsureCreatable(PrudentialPolicyVersionStatus status) =>
        Transitions.EnsureCreatable(status);

    public static void EnsureTransition(PrudentialPolicyVersionStatus from, PrudentialPolicyVersionStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static bool IsAllowed(PrudentialPolicyVersionStatus from, PrudentialPolicyVersionStatus to) =>
        Transitions.IsAllowed(from, to);

    public static string ToToken(this PrudentialPolicyVersionStatus status) => status switch
    {
        PrudentialPolicyVersionStatus.Draft => "DRAFT",
        PrudentialPolicyVersionStatus.Published => "PUBLISHED",
        PrudentialPolicyVersionStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out PrudentialPolicyVersionStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = PrudentialPolicyVersionStatus.Draft;
                return true;
            case "PUBLISHED":
                status = PrudentialPolicyVersionStatus.Published;
                return true;
            case "RETIRED":
                status = PrudentialPolicyVersionStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static PrudentialPolicyVersionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out PrudentialPolicyVersionStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.PrudentialPolicyVersionStatusUnknown);
}
