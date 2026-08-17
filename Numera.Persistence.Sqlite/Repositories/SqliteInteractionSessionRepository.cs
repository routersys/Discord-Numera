using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteInteractionSessionRepository : IInteractionSessionRepository
{
    private const string Columns = """
        interaction_session_id, discord_user_id, guild_id, economy_scope_id, flow_type, state,
        token_hash, payload_json, state_version, status, created_at, expires_at, completed_at
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteInteractionSessionRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(InteractionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO interaction_sessions({Columns})
            VALUES($id, $user, $guild, $scope, $flow, $state, $hash, $payload, $version, $status,
                $created, $expires, $completed);
            """);
        Bind(command, session);
        command.ExecuteNonQuery();
    }

    public void Update(InteractionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE interaction_sessions
            SET state = $state, payload_json = $payload, state_version = $version, status = $status,
                completed_at = $completed
            WHERE interaction_session_id = $id;
            """);
        Bind(command, session);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public InteractionSession? FindByTokenHash(byte[] tokenHash)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM interaction_sessions WHERE token_hash = $hash;
            """);
        command.Parameters.AddWithValue("$hash", tokenHash);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public IReadOnlyList<InteractionSession> ListActiveByUser(string discordUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discordUserId);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM interaction_sessions
            WHERE discord_user_id = $user AND status = 'ACTIVE'
            ORDER BY created_at ASC, interaction_session_id ASC;
            """);
        command.Parameters.AddWithValue("$user", discordUserId);

        List<InteractionSession> sessions = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            sessions.Add(Read(reader));
        }

        return sessions;
    }

    public IReadOnlyList<InteractionSession> ListExpired(UtcTimestamp now, int batchSize)
    {
        if (batchSize <= 0)
        {
            return [];
        }

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM interaction_sessions
            WHERE status = 'ACTIVE' AND expires_at <= $now
            ORDER BY expires_at ASC, interaction_session_id ASC
            LIMIT $limit;
            """);
        command.Parameters.AddWithValue("$now", now.UnixMilliseconds);
        command.Parameters.AddWithValue("$limit", batchSize);

        List<InteractionSession> sessions = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            sessions.Add(Read(reader));
        }

        return sessions;
    }

    public int PurgeTerminal(UtcTimestamp completedBefore, int batchSize)
    {
        if (batchSize <= 0)
        {
            return 0;
        }

        using SqliteCommand command = unitOfWork.CreateCommand("""
            DELETE FROM interaction_sessions
            WHERE interaction_session_id IN (
                SELECT interaction_session_id FROM interaction_sessions
                WHERE status <> 'ACTIVE' AND completed_at IS NOT NULL AND completed_at < $before
                ORDER BY completed_at ASC
                LIMIT $limit
            );
            """);
        command.Parameters.AddWithValue("$before", completedBefore.UnixMilliseconds);
        command.Parameters.AddWithValue("$limit", batchSize);

        return command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, InteractionSession session)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(session.Id.Value));
        command.Parameters.AddWithValue("$user", session.DiscordUserId);
        command.Parameters.AddWithValue("$guild", session.GuildId);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(session.EconomyScopeId.Value));
        command.Parameters.AddWithValue("$flow", session.FlowType);
        command.Parameters.AddWithValue("$state", session.State);
        command.Parameters.AddWithValue("$hash", session.TokenHashCopy());
        command.Parameters.AddWithValue("$payload", session.PayloadJson);
        command.Parameters.AddWithValue("$version", session.StateVersion);
        command.Parameters.AddWithValue("$status", session.Status.ToToken());
        command.Parameters.AddWithValue("$created", session.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$expires", session.ExpiresAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$completed", SqliteValueMapper.ToParameter(session.CompletedAt));
    }

    private static InteractionSession Read(SqliteDataReader reader) =>
        InteractionSession.Rehydrate(
            InteractionSessionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
            reader.GetString(1),
            reader.GetString(2),
            EconomyScopeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<byte[]>(6),
            reader.GetString(7),
            reader.GetInt64(8),
            InteractionSessionStatusCatalog.ParseToken(reader.GetString(9)),
            SqliteValueMapper.ReadTimestamp(reader, 10),
            SqliteValueMapper.ReadTimestamp(reader, 11),
            SqliteValueMapper.ReadNullableTimestamp(reader, 12));
}
