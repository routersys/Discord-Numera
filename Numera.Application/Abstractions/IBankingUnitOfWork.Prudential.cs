using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public interface IPrudentialPolicyRepository
{
    void AddDraft(PrudentialPolicyVersion policy, UtcTimestamp createdAt);

    void ReplaceDraft(PrudentialPolicyVersion policy, long expectedVersion);

    void Publish(PrudentialPolicyVersionId id, UtcTimestamp publishedAt);

    void Retire(PrudentialPolicyVersionId id, UtcTimestamp retiredAt);

    PrudentialPolicyVersion? Find(PrudentialPolicyVersionId id);

    string? FindStatus(PrudentialPolicyVersionId id);

    PrudentialPolicyVersion? FindPublished(EconomyScopeId economyScopeId);

    long NextVersion(EconomyScopeId economyScopeId);
}

public partial interface IBankingUnitOfWork
{
    IPrudentialPolicyRepository PrudentialPolicies { get; }
}
