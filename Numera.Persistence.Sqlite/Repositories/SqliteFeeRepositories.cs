using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteEconomyCalendarRepository : IEconomyCalendarRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteEconomyCalendarRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public string? FindCanonicalTimezone(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT canonical_timezone FROM guild_economies WHERE economy_scope_id = $scope;
            """);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? reader.GetString(0) : null;
    }

    public BusinessDayClass? FindDayClassOverride(EconomyScopeId economyScopeId, BusinessDate localDate)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT day_class FROM economy_calendar_overrides
            WHERE economy_scope_id = $scope AND local_date = $date;
            """);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));
        command.Parameters.AddWithValue("$date", localDate.ToString());

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? BusinessDayClassCatalog.ParseToken(reader.GetString(0)) : null;
    }

    public void UpsertDayClassOverride(
        EconomyScopeId economyScopeId,
        BusinessDate localDate,
        BusinessDayClass dayClass,
        string? description)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO economy_calendar_overrides(
                economy_scope_id, local_date, day_class, description, version)
            VALUES($scope, $date, $class, $description, 1)
            ON CONFLICT(economy_scope_id, local_date) DO UPDATE
            SET day_class = excluded.day_class,
                description = excluded.description,
                version = economy_calendar_overrides.version + 1;
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));
        command.Parameters.AddWithValue("$date", localDate.ToString());
        command.Parameters.AddWithValue("$class", dayClass.ToToken());
        command.Parameters.AddWithValue("$description", (object?)description ?? DBNull.Value);

        command.ExecuteNonQuery();
    }

    public bool DeleteDayClassOverride(EconomyScopeId economyScopeId, BusinessDate localDate)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            DELETE FROM economy_calendar_overrides
            WHERE economy_scope_id = $scope AND local_date = $date;
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));
        command.Parameters.AddWithValue("$date", localDate.ToString());

        return command.ExecuteNonQuery() == 1;
    }
}

public sealed class SqliteBankPolicyRepository : IBankPolicyRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteBankPolicyRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public TransferLimitSet? FindTransferLimits(BankPolicyVersionId bankPolicyVersionId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT per_transfer_limit_minor, daily_outgoing_limit_minor FROM bank_policy_versions
            WHERE bank_policy_version_id = $id;
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(bankPolicyVersionId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? TransferLimitReader.Read(reader) : null;
    }

    public MoneyMinor? FindMaximumActiveHolds(BankPolicyVersionId bankPolicyVersionId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT maximum_active_holds_minor FROM bank_policy_versions
            WHERE bank_policy_version_id = $id;
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(bankPolicyVersionId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() && !reader.IsDBNull(0) ? MoneyMinor.FromMinor(reader.GetInt64(0)) : null;
    }
}

public sealed class SqliteAccountLimitPreferenceRepository : IAccountLimitPreferenceRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteAccountLimitPreferenceRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public TransferLimitSet? FindTransferLimits(DepositAccountId depositAccountId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT per_transfer_limit_minor, daily_outgoing_limit_minor FROM account_limit_preferences
            WHERE deposit_account_id = $id;
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(depositAccountId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? TransferLimitReader.Read(reader) : null;
    }

    public void Set(DepositAccountId depositAccountId, TransferLimitSet limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO account_limit_preferences(
                deposit_account_id, per_transfer_limit_minor, daily_outgoing_limit_minor, version)
            VALUES($id, $perTransfer, $daily, 1)
            ON CONFLICT(deposit_account_id) DO UPDATE SET
                per_transfer_limit_minor = $perTransfer,
                daily_outgoing_limit_minor = $daily,
                version = account_limit_preferences.version + 1;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(depositAccountId.Value));
        command.Parameters.AddWithValue(
            "$perTransfer", (object?)limits.PerTransfer?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$daily", (object?)limits.DailyOutgoing?.Value ?? DBNull.Value);

        command.ExecuteNonQuery();
    }
}

internal static class TransferLimitReader
{
    internal static TransferLimitSet Read(SqliteDataReader reader) => new(
        reader.IsDBNull(0) ? null : MoneyMinor.FromMinor(reader.GetInt64(0)),
        reader.IsDBNull(1) ? null : MoneyMinor.FromMinor(reader.GetInt64(1)));
}

public sealed class SqliteFeeScheduleRepository : IFeeScheduleRepository
{
    private const string Columns = """
        fee_rule_id, fee_schedule_version_id, fee_type, priority, channel, account_product_id,
        atm_network_id, counterparty_bank_id, amount_min_minor, amount_max_minor, day_class,
        local_start_minute, local_end_minute, fixed_minor, basis_points, minimum_minor,
        maximum_minor, waiver_counter_key, free_occurrences_per_business_month
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteFeeScheduleRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public IReadOnlyList<FeeRule> ListRules(FeeScheduleVersionId feeScheduleVersionId, FeeType feeType)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM fee_rules
            WHERE fee_schedule_version_id = $version AND fee_type = $type
            ORDER BY priority, fee_rule_id;
            """);
        command.Parameters.AddWithValue("$version", SqliteValueMapper.ToBlob(feeScheduleVersionId.Value));
        command.Parameters.AddWithValue("$type", feeType.ToToken());

        List<FeeRule> rules = [];

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rules.Add(Read(reader));
        }

        return rules;
    }

    private static FeeRule Read(SqliteDataReader reader) => FeeRule.Create(
        FeeRuleId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        FeeScheduleVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        FeeCatalog.ParseFeeTypeToken(reader.GetString(2)),
        reader.GetInt32(3),
        FeeCatalog.ParseChannelToken(reader.GetString(4)),
        reader.IsDBNull(5) ? null : AccountProductId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
        reader.IsDBNull(6) ? null : AtmNetworkId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
        reader.IsDBNull(7) ? null : BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
        MoneyMinor.FromMinor(reader.GetInt64(8)),
        reader.IsDBNull(9) ? null : MoneyMinor.FromMinor(reader.GetInt64(9)),
        FeeCatalog.ParseDayClassToken(reader.GetString(10)),
        reader.IsDBNull(11) ? null : reader.GetInt32(11),
        reader.IsDBNull(12) ? null : reader.GetInt32(12),
        MoneyMinor.FromMinor(reader.GetInt64(13)),
        reader.GetInt32(14),
        MoneyMinor.FromMinor(reader.GetInt64(15)),
        reader.IsDBNull(16) ? null : MoneyMinor.FromMinor(reader.GetInt64(16)),
        reader.IsDBNull(17) ? null : reader.GetString(17),
        reader.GetInt32(18));
}

public sealed class SqliteFeeWaiverCounterRepository : IFeeWaiverCounterRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteFeeWaiverCounterRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public long FindUsedCount(DepositAccountId depositAccountId, string waiverCounterKey, int businessMonth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waiverCounterKey);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT used_count FROM fee_waiver_usage_counters
            WHERE deposit_account_id = $account AND waiver_counter_key = $key AND business_month = $month;
            """);
        Bind(command, depositAccountId, waiverCounterKey, businessMonth);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? reader.GetInt64(0) : 0;
    }

    public void Consume(DepositAccountId depositAccountId, string waiverCounterKey, int businessMonth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waiverCounterKey);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO fee_waiver_usage_counters(deposit_account_id, waiver_counter_key, business_month,
                used_count, version)
            VALUES($account, $key, $month, 1, 1)
            ON CONFLICT(deposit_account_id, waiver_counter_key, business_month) DO UPDATE SET
                used_count = used_count + 1,
                version = version + 1;
            """);
        Bind(command, depositAccountId, waiverCounterKey, businessMonth);
        command.ExecuteNonQuery();
    }

    private static void Bind(
        SqliteCommand command,
        DepositAccountId depositAccountId,
        string waiverCounterKey,
        int businessMonth)
    {
        command.Parameters.AddWithValue("$account", SqliteValueMapper.ToBlob(depositAccountId.Value));
        command.Parameters.AddWithValue("$key", waiverCounterKey);
        command.Parameters.AddWithValue("$month", businessMonth);
    }
}

public sealed class SqliteFeeAssessmentRepository : IFeeAssessmentRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteFeeAssessmentRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(FeeAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO fee_assessments(fee_assessment_id, business_operation_id, fee_schedule_version_id,
                fee_rule_id, currency_id, payer_ledger_account_id, recipient_ledger_account_id,
                fee_type, amount_minor, assessed_at, version)
            VALUES($id, $operation, $schedule, $rule, $currency, $payer, $recipient, $type, $amount,
                $assessed, $version);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(assessment.Id.Value));
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(assessment.BusinessOperationId.Value));
        command.Parameters.AddWithValue(
            "$schedule", SqliteValueMapper.ToParameter(assessment.FeeScheduleVersionId?.Value));
        command.Parameters.AddWithValue("$rule", SqliteValueMapper.ToParameter(assessment.FeeRuleId?.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(assessment.CurrencyId.Value));
        command.Parameters.AddWithValue(
            "$payer", SqliteValueMapper.ToBlob(assessment.PayerLedgerAccountId.Value));
        command.Parameters.AddWithValue(
            "$recipient", SqliteValueMapper.ToBlob(assessment.RecipientLedgerAccountId.Value));
        command.Parameters.AddWithValue("$type", assessment.FeeType.ToToken());
        command.Parameters.AddWithValue("$amount", assessment.Amount.Value);
        command.Parameters.AddWithValue("$assessed", assessment.AssessedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", assessment.Version);
        command.ExecuteNonQuery();
    }
}
