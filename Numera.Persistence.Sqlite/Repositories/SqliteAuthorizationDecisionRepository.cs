using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteAuthorizationDecisionRepository : IAuthorizationDecisionRepository
{
    private const string Columns =
        "authorization_decision_id, target_type, target_id, scope_guild_id, authority_kind, " +
        "actor_discord_user_id, actor_customer_account_id, decision_kind, reason_code, occurred_at";

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteAuthorizationDecisionRepository(SqliteUnitOfWork unitOfWork) =>
        this.unitOfWork = unitOfWork;

    public void Add(AuthorizationDecisionRecord decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO authorization_decisions({Columns}, supersedes_decision_id)
            VALUES($id, $targetType, $targetId, $scope, $authority, $actor, $customer, $kind, $reason,
                $occurred, NULL);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(decision.Id.Value));
        command.Parameters.AddWithValue("$targetType", decision.TargetType);
        command.Parameters.AddWithValue("$targetId", SqliteValueMapper.ToBlob(decision.TargetId));
        command.Parameters.AddWithValue("$scope", (object?)decision.ScopeGuildId ?? DBNull.Value);
        command.Parameters.AddWithValue("$authority", decision.AuthorityKind);
        command.Parameters.AddWithValue("$actor", decision.ActorDiscordUserId);
        command.Parameters.AddWithValue(
            "$customer",
            decision.ActorCustomerAccountId is { } customer
                ? SqliteValueMapper.ToBlob(customer.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$kind", decision.DecisionKind);
        command.Parameters.AddWithValue("$reason", (object?)decision.ReasonCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$occurred", decision.OccurredAt.UnixMilliseconds);

        command.ExecuteNonQuery();
    }

    public IReadOnlyList<AuthorizationDecisionRecord> ListEffective(
        string targetType,
        EntityIdValue targetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM authorization_decisions AS d
            WHERE d.target_type = $targetType AND d.target_id = $targetId
              AND NOT EXISTS(
                SELECT 1 FROM authorization_decisions AS later
                WHERE later.target_type = d.target_type AND later.target_id = d.target_id
                  AND later.authority_kind = d.authority_kind
                  AND COALESCE(later.scope_guild_id, '') = COALESCE(d.scope_guild_id, '')
                  AND (later.occurred_at > d.occurred_at
                    OR (later.occurred_at = d.occurred_at
                      AND later.authorization_decision_id > d.authorization_decision_id)))
            ORDER BY d.authority_kind ASC, d.scope_guild_id ASC;
            """);

        command.Parameters.AddWithValue("$targetType", targetType);
        command.Parameters.AddWithValue("$targetId", SqliteValueMapper.ToBlob(targetId));

        List<AuthorizationDecisionRecord> decisions = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            decisions.Add(new AuthorizationDecisionRecord(
                AuthorizationDecisionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                reader.GetString(1),
                SqliteValueMapper.ReadEntityId(reader, 2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6)
                    ? null
                    : CustomerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(9))));
        }

        return decisions;
    }
}
