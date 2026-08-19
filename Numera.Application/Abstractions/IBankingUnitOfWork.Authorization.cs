using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public sealed record AuthorizationDecisionRecord(
    AuthorizationDecisionId Id,
    string TargetType,
    EntityIdValue TargetId,
    string? ScopeGuildId,
    string AuthorityKind,
    string ActorDiscordUserId,
    CustomerAccountId? ActorCustomerAccountId,
    string DecisionKind,
    string? ReasonCode,
    UtcTimestamp OccurredAt);

public interface IAuthorizationDecisionRepository
{
    void Add(AuthorizationDecisionRecord decision);

    IReadOnlyList<AuthorizationDecisionRecord> ListEffective(string targetType, EntityIdValue targetId);
}

public partial interface IBankingUnitOfWork
{
    IAuthorizationDecisionRepository AuthorizationDecisions { get; }
}
