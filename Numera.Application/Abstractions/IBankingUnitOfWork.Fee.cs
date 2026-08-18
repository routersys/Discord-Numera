using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public interface IFeeAdministrationRepository
{
    void AddVersion(FeeScheduleVersionId id, BankId bankId, UtcTimestamp effectiveFrom, long version);

    void UpsertRule(FeeScheduleVersionId versionId, FeeRuleId ruleId, FeeRule rule);

    void Publish(BankId bankId, FeeScheduleVersionId versionId, UtcTimestamp effectiveFrom);

    BankId? FindVersionBank(FeeScheduleVersionId versionId);

    bool IsPublished(FeeScheduleVersionId versionId);

    long NextVersion(BankId bankId);

    int CountRules(FeeScheduleVersionId versionId);
}

public partial interface IBankingUnitOfWork
{
    IFeeAdministrationRepository FeeAdministration { get; }
}
