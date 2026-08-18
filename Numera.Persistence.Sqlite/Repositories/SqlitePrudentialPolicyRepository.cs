using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqlitePrudentialPolicyRepository : IPrudentialPolicyRepository
{
    private const string Columns = """
        prudential_policy_version_id, economy_scope_id, minimum_cet1_bps, lending_cet1_bps,
        minimum_leverage_bps, configured_warning_leverage_bps, minimum_liquidity_bps,
        minimum_initial_bank_capital_minor, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqlitePrudentialPolicyRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void AddDraft(PrudentialPolicyVersion policy, UtcTimestamp createdAt)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO prudential_policy_versions({Columns}, status, created_at)
            VALUES($id, $scope, $cet1, $lending, $leverage, $warning, $liquidity, $capital, $version,
                   'DRAFT', $createdAt);
            """);

        Bind(command, policy);
        command.Parameters.AddWithValue("$createdAt", createdAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public void ReplaceDraft(PrudentialPolicyVersion policy, long expectedVersion)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE prudential_policy_versions
            SET minimum_cet1_bps = $cet1,
                lending_cet1_bps = $lending,
                minimum_leverage_bps = $leverage,
                configured_warning_leverage_bps = $warning,
                minimum_liquidity_bps = $liquidity,
                minimum_initial_bank_capital_minor = $capital
            WHERE prudential_policy_version_id = $id AND status = 'DRAFT' AND version = $expected;
            """);

        Bind(command, policy);
        command.Parameters.AddWithValue("$expected", expectedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public void Publish(PrudentialPolicyVersionId id, UtcTimestamp publishedAt)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE prudential_policy_versions
            SET status = 'PUBLISHED', published_at = $publishedAt
            WHERE prudential_policy_version_id = $id AND status = 'DRAFT';
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));
        command.Parameters.AddWithValue("$publishedAt", publishedAt.UnixMilliseconds);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public void Retire(PrudentialPolicyVersionId id, UtcTimestamp retiredAt)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE prudential_policy_versions
            SET status = 'RETIRED', retired_at = $retiredAt
            WHERE prudential_policy_version_id = $id AND status = 'PUBLISHED';
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));
        command.Parameters.AddWithValue("$retiredAt", retiredAt.UnixMilliseconds);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public PrudentialPolicyVersion? Find(PrudentialPolicyVersionId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM prudential_policy_versions WHERE prudential_policy_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public string? FindStatus(PrudentialPolicyVersionId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT status FROM prudential_policy_versions WHERE prudential_policy_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        return command.ExecuteScalar() as string;
    }

    public PrudentialPolicyVersion? FindPublished(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM prudential_policy_versions
            WHERE economy_scope_id = $scope AND status = 'PUBLISHED';
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public long NextVersion(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COALESCE(MAX(version), 0) + 1 FROM prudential_policy_versions WHERE economy_scope_id = $scope;
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        return command.ExecuteScalar() is long next ? next : 1L;
    }

    private static void Bind(SqliteCommand command, PrudentialPolicyVersion policy)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(policy.Id.Value));
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(policy.EconomyScopeId.Value));
        command.Parameters.AddWithValue("$cet1", policy.MinimumCet1BasisPoints);
        command.Parameters.AddWithValue("$lending", policy.LendingCet1BasisPoints);
        command.Parameters.AddWithValue("$leverage", policy.MinimumLeverageBasisPoints);
        command.Parameters.AddWithValue("$warning", policy.ConfiguredWarningLeverageBasisPoints);
        command.Parameters.AddWithValue("$liquidity", policy.MinimumLiquidityBasisPoints);
        command.Parameters.AddWithValue("$capital", policy.MinimumInitialBankCapital.Value);
        command.Parameters.AddWithValue("$version", policy.Version);
    }

    private static PrudentialPolicyVersion Read(SqliteDataReader reader) =>
        PrudentialPolicyVersion.Create(
            PrudentialPolicyVersionId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            EconomyScopeId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            MoneyMinor.FromMinor(reader.GetInt64(7)),
            reader.GetInt64(8));
}
