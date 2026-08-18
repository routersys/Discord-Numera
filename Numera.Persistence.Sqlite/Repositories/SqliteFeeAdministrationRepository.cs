using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteFeeAdministrationRepository : IFeeAdministrationRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteFeeAdministrationRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void AddVersion(FeeScheduleVersionId id, BankId bankId, UtcTimestamp effectiveFrom, long version)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO fee_schedule_versions(fee_schedule_version_id, bank_id, effective_from, version)
            VALUES($id, $bank, $from, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$from", effectiveFrom.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", version);

        command.ExecuteNonQuery();
    }

    public void UpsertRule(FeeScheduleVersionId versionId, FeeRuleId ruleId, FeeRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO fee_rules(
                fee_rule_id, fee_schedule_version_id, fee_type, priority, channel,
                amount_min_minor, day_class, fixed_minor, basis_points, minimum_minor,
                maximum_minor, free_occurrences_per_business_month)
            VALUES($id, $version, $type, $priority, 'ANY', 0, 'ANY', $fixed, $bps, $minimum,
                   $maximum, $free);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(ruleId.Value));
        command.Parameters.AddWithValue("$version", SqliteValueMapper.ToBlob(versionId.Value));
        command.Parameters.AddWithValue("$type", rule.Type.ToToken());
        command.Parameters.AddWithValue("$priority", rule.Priority);
        command.Parameters.AddWithValue("$fixed", rule.FixedAmount.Value);
        command.Parameters.AddWithValue("$bps", rule.BasisPoints);
        command.Parameters.AddWithValue("$minimum", rule.MinimumAmount.Value);
        command.Parameters.AddWithValue("$maximum", (object?)rule.MaximumAmount?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("$free", rule.FreeOccurrencesPerBusinessMonth);

        command.ExecuteNonQuery();
    }

    public void Publish(BankId bankId, FeeScheduleVersionId versionId, UtcTimestamp effectiveFrom)
    {
        using SqliteCommand close = unitOfWork.CreateCommand("""
            UPDATE fee_schedule_versions
            SET effective_to = $from
            WHERE bank_id = $bank AND effective_to IS NULL AND fee_schedule_version_id <> $id
              AND effective_from < $from;
            """);

        close.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        close.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(versionId.Value));
        close.Parameters.AddWithValue("$from", effectiveFrom.UnixMilliseconds);
        close.ExecuteNonQuery();

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE banks SET current_fee_schedule_version_id = $id, version = version + 1
            WHERE bank_id = $bank;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(versionId.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public BankId? FindVersionBank(FeeScheduleVersionId versionId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT bank_id FROM fee_schedule_versions WHERE fee_schedule_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(versionId.Value));

        return command.ExecuteScalar() is byte[] bytes
            ? BankId.FromValue(EntityIdValue.FromBytes(bytes))
            : null;
    }

    public bool IsPublished(FeeScheduleVersionId versionId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COUNT(*) FROM banks WHERE current_fee_schedule_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(versionId.Value));

        return command.ExecuteScalar() is long count && count > 0;
    }

    public long NextVersion(BankId bankId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COALESCE(MAX(version), 0) + 1 FROM fee_schedule_versions WHERE bank_id = $bank;
            """);

        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));

        return command.ExecuteScalar() is long next ? next : 1L;
    }

    public int CountRules(FeeScheduleVersionId versionId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COUNT(*) FROM fee_rules WHERE fee_schedule_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(versionId.Value));

        return command.ExecuteScalar() is long count ? (int)count : 0;
    }
}
