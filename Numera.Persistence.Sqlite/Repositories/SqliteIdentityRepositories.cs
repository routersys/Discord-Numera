using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Accounting;
using Numera.Domain.Common;
using Numera.Domain.Identity;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal static class SqliteValueMapper
{
    internal static byte[] ToBlob(EntityIdValue value) => value.ToByteArray();

    internal static EntityIdValue ReadEntityId(SqliteDataReader reader, int ordinal) =>
        EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(ordinal));

    internal static UtcTimestamp ReadTimestamp(SqliteDataReader reader, int ordinal) =>
        UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(ordinal));

    internal static UtcTimestamp? ReadNullableTimestamp(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(ordinal));

    internal static object ToParameter(UtcTimestamp? value) =>
        value is { } timestamp ? timestamp.UnixMilliseconds : DBNull.Value;

    internal static object ToParameter(EntityIdValue? value) =>
        value is { } id ? ToBlob(id) : (object)DBNull.Value;
}

public sealed class SqlitePartyRepository : IPartyRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqlitePartyRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(Party party)
    {
        ArgumentNullException.ThrowIfNull(party);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES($party_id, $party_type, $display_name, $status, $created_at, $version);
            """);
        command.Parameters.AddWithValue("$party_id", SqliteValueMapper.ToBlob(party.Id.Value));
        command.Parameters.AddWithValue("$party_type", party.Type.ToToken());
        command.Parameters.AddWithValue("$display_name", party.DisplayName.Value);
        command.Parameters.AddWithValue("$status", party.Status.ToToken());
        command.Parameters.AddWithValue("$created_at", party.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", party.Version);
        command.ExecuteNonQuery();
    }

    public Party? Find(PartyId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT party_type, display_name, status, created_at, version
            FROM parties WHERE party_id = $party_id;
            """);
        command.Parameters.AddWithValue("$party_id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return Party.Rehydrate(
            id,
            PartyCatalog.ParseTypeToken(reader.GetString(0)),
            DisplayName.Parse(reader.GetString(1)),
            PartyCatalog.ParseStatusToken(reader.GetString(2)),
            SqliteValueMapper.ReadTimestamp(reader, 3),
            reader.GetInt64(4));
    }
}

public sealed class SqliteCustomerAccountRepository : ICustomerAccountRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteCustomerAccountRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(CustomerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO customer_accounts(customer_account_id, party_id, public_handle, display_name,
                status, created_at, last_authenticated_at, version)
            VALUES($customer_account_id, $party_id, $public_handle, $display_name, $status,
                $created_at, $last_authenticated_at, $version);
            """);
        Bind(command, account);
        command.ExecuteNonQuery();
    }

    public void Update(CustomerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE customer_accounts
            SET display_name = $display_name,
                status = $status,
                last_authenticated_at = $last_authenticated_at,
                version = $version
            WHERE customer_account_id = $customer_account_id AND version = $expected_version;
            """);
        Bind(command, account);
        command.Parameters.AddWithValue("$expected_version", account.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public CustomerAccount? Find(CustomerAccountId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT party_id, public_handle, display_name, status, created_at, last_authenticated_at, version
            FROM customer_accounts WHERE customer_account_id = $customer_account_id;
            """);
        command.Parameters.AddWithValue("$customer_account_id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return CustomerAccount.Rehydrate(
            id,
            PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
            PublicHandle.Parse(reader.GetString(1)),
            DisplayName.Parse(reader.GetString(2)),
            CustomerAccountStatusCatalog.ParseToken(reader.GetString(3)),
            SqliteValueMapper.ReadTimestamp(reader, 4),
            SqliteValueMapper.ReadTimestamp(reader, 5),
            reader.GetInt64(6));
    }

    public bool HandleExists(PublicHandle handle)
    {
        using SqliteCommand command = unitOfWork.CreateCommand(
            "SELECT COUNT(*) FROM customer_accounts WHERE public_handle = $public_handle;");
        command.Parameters.AddWithValue("$public_handle", handle.Value);

        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static void Bind(SqliteCommand command, CustomerAccount account)
    {
        command.Parameters.AddWithValue("$customer_account_id", SqliteValueMapper.ToBlob(account.Id.Value));
        command.Parameters.AddWithValue("$party_id", SqliteValueMapper.ToBlob(account.PartyId.Value));
        command.Parameters.AddWithValue("$public_handle", account.PublicHandle.Value);
        command.Parameters.AddWithValue("$display_name", account.DisplayName.Value);
        command.Parameters.AddWithValue("$status", account.Status.ToToken());
        command.Parameters.AddWithValue("$created_at", account.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$last_authenticated_at", account.LastAuthenticatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", account.Version);
    }
}

public sealed class SqliteDiscordIdentityLinkRepository : IDiscordIdentityLinkRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteDiscordIdentityLinkRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(DiscordIdentityLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO discord_identity_links(discord_identity_link_id, customer_account_id, discord_user_id,
                is_primary, status, linked_at, unlinked_at, last_authenticated_at, version)
            VALUES($discord_identity_link_id, $customer_account_id, $discord_user_id, $is_primary, $status,
                $linked_at, $unlinked_at, $last_authenticated_at, $version);
            """);
        Bind(command, link);
        command.ExecuteNonQuery();
    }

    public void Update(DiscordIdentityLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE discord_identity_links
            SET is_primary = $is_primary,
                status = $status,
                unlinked_at = $unlinked_at,
                last_authenticated_at = $last_authenticated_at,
                version = $version
            WHERE discord_identity_link_id = $discord_identity_link_id AND version = $expected_version;
            """);
        Bind(command, link);
        command.Parameters.AddWithValue("$expected_version", link.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public DiscordIdentityLink? FindActive(DiscordUserId discordUserId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT discord_identity_link_id, customer_account_id, is_primary, status, linked_at,
                unlinked_at, last_authenticated_at, version
            FROM discord_identity_links
            WHERE discord_user_id = $discord_user_id AND status = 'ACTIVE';
            """);
        command.Parameters.AddWithValue("$discord_user_id", discordUserId.ToString());

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return DiscordIdentityLink.Rehydrate(
            DiscordIdentityLinkId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
            CustomerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
            discordUserId,
            reader.GetInt64(2) == 1,
            DiscordIdentityLinkStatusCatalog.ParseToken(reader.GetString(3)),
            SqliteValueMapper.ReadTimestamp(reader, 4),
            SqliteValueMapper.ReadNullableTimestamp(reader, 5),
            SqliteValueMapper.ReadTimestamp(reader, 6),
            reader.GetInt64(7));
    }

    private static void Bind(SqliteCommand command, DiscordIdentityLink link)
    {
        command.Parameters.AddWithValue("$discord_identity_link_id", SqliteValueMapper.ToBlob(link.Id.Value));
        command.Parameters.AddWithValue("$customer_account_id", SqliteValueMapper.ToBlob(link.CustomerAccountId.Value));
        command.Parameters.AddWithValue("$discord_user_id", link.DiscordUserId.ToString());
        command.Parameters.AddWithValue("$is_primary", link.IsPrimary ? 1 : 0);
        command.Parameters.AddWithValue("$status", link.Status.ToToken());
        command.Parameters.AddWithValue("$linked_at", link.LinkedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$unlinked_at", SqliteValueMapper.ToParameter(link.UnlinkedAt));
        command.Parameters.AddWithValue("$last_authenticated_at", link.LastAuthenticatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", link.Version);
    }
}

public sealed class SqliteBusinessOperationRepository : IBusinessOperationRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteBusinessOperationRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(BusinessOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO business_operations(business_operation_id, operation_type, economy_scope_id,
                actor_party_id, correlation_id, idempotency_scope, idempotency_key, status, created_at,
                committed_at, version)
            VALUES($business_operation_id, $operation_type, $economy_scope_id, $actor_party_id, $correlation_id,
                $idempotency_scope, $idempotency_key, $status, $created_at, $committed_at, $version);
            """);
        Bind(command, operation);
        command.ExecuteNonQuery();
    }

    public void Update(BusinessOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE business_operations
            SET status = $status, committed_at = $committed_at, version = $version
            WHERE business_operation_id = $business_operation_id AND version = $expected_version;
            """);
        Bind(command, operation);
        command.Parameters.AddWithValue("$expected_version", operation.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public BusinessOperation? Find(IdempotencyKey idempotencyKey)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT business_operation_id, operation_type, economy_scope_id, actor_party_id, correlation_id,
                status, created_at, committed_at, version
            FROM business_operations
            WHERE idempotency_scope = $idempotency_scope AND idempotency_key = $idempotency_key;
            """);
        command.Parameters.AddWithValue("$idempotency_scope", idempotencyKey.Scope);
        command.Parameters.AddWithValue("$idempotency_key", idempotencyKey.Key);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return BusinessOperation.Rehydrate(
            BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
            reader.GetString(1),
            EconomyScopeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
            reader.IsDBNull(3) ? null : PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
            SqliteValueMapper.ReadEntityId(reader, 4),
            idempotencyKey,
            BusinessOperationStatusCatalog.ParseToken(reader.GetString(5)),
            SqliteValueMapper.ReadTimestamp(reader, 6),
            SqliteValueMapper.ReadNullableTimestamp(reader, 7),
            reader.GetInt64(8));
    }

    private static void Bind(SqliteCommand command, BusinessOperation operation)
    {
        command.Parameters.AddWithValue("$business_operation_id", SqliteValueMapper.ToBlob(operation.Id.Value));
        command.Parameters.AddWithValue("$operation_type", operation.OperationType);
        command.Parameters.AddWithValue("$economy_scope_id", SqliteValueMapper.ToBlob(operation.EconomyScopeId.Value));
        command.Parameters.AddWithValue("$actor_party_id", SqliteValueMapper.ToParameter(operation.ActorPartyId?.Value));
        command.Parameters.AddWithValue("$correlation_id", SqliteValueMapper.ToBlob(operation.CorrelationId));
        command.Parameters.AddWithValue("$idempotency_scope", operation.IdempotencyKey.Scope);
        command.Parameters.AddWithValue("$idempotency_key", operation.IdempotencyKey.Key);
        command.Parameters.AddWithValue("$status", operation.Status.ToToken());
        command.Parameters.AddWithValue("$created_at", operation.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$committed_at", SqliteValueMapper.ToParameter(operation.CommittedAt));
        command.Parameters.AddWithValue("$version", operation.Version);
    }
}

public sealed class SqliteOutboxRepository : IOutboxRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteOutboxRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(OutboxEvent outboxEvent)
    {
        ArgumentNullException.ThrowIfNull(outboxEvent);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO outbox_events(outbox_event_id, business_operation_id, event_type, payload_json, status,
                claim_token, claimed_at, claim_expires_at, next_attempt_at, created_at, published_at,
                attempt_count, last_error_code, version)
            VALUES($outbox_event_id, $business_operation_id, $event_type, $payload_json, $status, $claim_token,
                $claimed_at, $claim_expires_at, $next_attempt_at, $created_at, $published_at, $attempt_count,
                $last_error_code, $version);
            """);
        command.Parameters.AddWithValue("$outbox_event_id", SqliteValueMapper.ToBlob(outboxEvent.Id.Value));
        command.Parameters.AddWithValue(
            "$business_operation_id", SqliteValueMapper.ToParameter(outboxEvent.BusinessOperationId?.Value));
        command.Parameters.AddWithValue("$event_type", outboxEvent.EventType);
        command.Parameters.AddWithValue("$payload_json", outboxEvent.PayloadJson);
        command.Parameters.AddWithValue("$status", outboxEvent.Status.ToToken());
        command.Parameters.AddWithValue("$claim_token", SqliteValueMapper.ToParameter(outboxEvent.ClaimToken));
        command.Parameters.AddWithValue("$claimed_at", SqliteValueMapper.ToParameter(outboxEvent.ClaimedAt));
        command.Parameters.AddWithValue("$claim_expires_at", SqliteValueMapper.ToParameter(outboxEvent.ClaimExpiresAt));
        command.Parameters.AddWithValue("$next_attempt_at", SqliteValueMapper.ToParameter(outboxEvent.NextAttemptAt));
        command.Parameters.AddWithValue("$created_at", outboxEvent.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$published_at", SqliteValueMapper.ToParameter(outboxEvent.PublishedAt));
        command.Parameters.AddWithValue("$attempt_count", outboxEvent.AttemptCount);
        command.Parameters.AddWithValue(
            "$last_error_code", outboxEvent.LastErrorCode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$version", outboxEvent.Version);
        command.ExecuteNonQuery();
    }
}
