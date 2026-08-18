using System.Globalization;
using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Common;
using Numera.Domain.Identity;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteAccountLinkGrantRepository : IAccountLinkGrantRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteAccountLinkGrantRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(AccountLinkGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO account_link_grants(
                account_link_grant_id, customer_account_id, code_digest, status,
                issued_at, expires_at, consumed_at, consumed_by_discord_user_id, version)
            VALUES($id, $customer, $digest, $status, $issued, $expires, NULL, NULL, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(grant.Id.Value));
        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(grant.CustomerAccountId.Value));
        command.Parameters.AddWithValue("$digest", grant.CodeDigest.ToArray());
        command.Parameters.AddWithValue("$status", grant.Status.ToToken());
        command.Parameters.AddWithValue("$issued", grant.IssuedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$expires", grant.ExpiresAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", grant.Version);

        command.ExecuteNonQuery();
    }

    public void Update(AccountLinkGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE account_link_grants
            SET status = $status,
                consumed_at = $consumedAt,
                consumed_by_discord_user_id = $consumedBy,
                version = $version
            WHERE account_link_grant_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$status", grant.Status.ToToken());
        command.Parameters.AddWithValue(
            "$consumedAt", (object?)grant.ConsumedAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$consumedBy",
            grant.ConsumedBy is { } user
                ? user.Value.ToString(CultureInfo.InvariantCulture)
                : DBNull.Value);
        command.Parameters.AddWithValue("$version", grant.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(grant.Id.Value));
        command.Parameters.AddWithValue("$expected", grant.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public AccountLinkGrant? FindByDigest(ReadOnlyMemory<byte> codeDigest)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT account_link_grant_id, customer_account_id, code_digest, status,
                   issued_at, expires_at, consumed_at, consumed_by_discord_user_id, version
            FROM account_link_grants
            WHERE code_digest = $digest;
            """);

        command.Parameters.AddWithValue("$digest", codeDigest.ToArray());

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return AccountLinkGrant.Rehydrate(
            AccountLinkGrantId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            CustomerAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            reader.GetFieldValue<byte[]>(2),
            AccountLinkGrantCatalog.ParseToken(reader.GetString(3)),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(4)),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(5)),
            reader.IsDBNull(6) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(6)),
            reader.IsDBNull(7)
                ? null
                : DiscordUserId.FromUInt64(ulong.Parse(reader.GetString(7), CultureInfo.InvariantCulture)),
            reader.GetInt64(8));
    }

    public IReadOnlyList<DiscordIdentityLink> ListActiveLinks(CustomerAccountId customerAccountId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT discord_identity_link_id, customer_account_id, discord_user_id, is_primary,
                   status, linked_at, unlinked_at, last_authenticated_at, version
            FROM discord_identity_links
            WHERE customer_account_id = $customer AND status = 'ACTIVE'
            ORDER BY is_primary DESC, linked_at ASC;
            """);

        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));

        List<DiscordIdentityLink> links = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            links.Add(Read(reader));
        }

        return links;
    }

    public DiscordIdentityLink? FindLink(DiscordIdentityLinkId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT discord_identity_link_id, customer_account_id, discord_user_id, is_primary,
                   status, linked_at, unlinked_at, last_authenticated_at, version
            FROM discord_identity_links
            WHERE discord_identity_link_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? Read(reader) : null;
    }

    private static DiscordIdentityLink Read(SqliteDataReader reader) =>
        DiscordIdentityLink.Rehydrate(
            DiscordIdentityLinkId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            CustomerAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            DiscordUserId.FromUInt64(ulong.Parse(reader.GetString(2), CultureInfo.InvariantCulture)),
            reader.GetInt64(3) != 0,
            DiscordIdentityLinkStatusCatalog.ParseToken(reader.GetString(4)),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(5)),
            reader.IsDBNull(6) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(6)),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(7)),
            reader.GetInt64(8));
}
