using System.Globalization;
using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteBankOperatorGrantRepository : IBankOperatorGrantRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteBankOperatorGrantRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(BankOperatorGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO bank_operator_grants(
                bank_operator_grant_id, bank_id, discord_user_id, status,
                granted_by_discord_user_id, granted_at, revoked_at, version)
            VALUES($id, $bank, $user, $status, $grantedBy, $grantedAt, NULL, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(grant.Id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(grant.BankId.Value));
        command.Parameters.AddWithValue("$user", Text(grant.DiscordUserId));
        command.Parameters.AddWithValue("$status", grant.Status.ToToken());
        command.Parameters.AddWithValue("$grantedBy", Text(grant.GrantedBy));
        command.Parameters.AddWithValue("$grantedAt", grant.GrantedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", grant.Version);

        command.ExecuteNonQuery();
    }

    public void Update(BankOperatorGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE bank_operator_grants
            SET status = $status,
                revoked_at = $revokedAt,
                version = $version
            WHERE bank_operator_grant_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$status", grant.Status.ToToken());
        command.Parameters.AddWithValue(
            "$revokedAt", (object?)grant.RevokedAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", grant.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(grant.Id.Value));
        command.Parameters.AddWithValue("$expected", grant.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public BankOperatorGrant? FindActive(BankId bankId, DiscordUserId discordUserId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT bank_operator_grant_id, bank_id, discord_user_id, status,
                   granted_by_discord_user_id, granted_at, revoked_at, version
            FROM bank_operator_grants
            WHERE bank_id = $bank AND discord_user_id = $user AND status = 'ACTIVE';
            """);

        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$user", Text(discordUserId));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? Read(reader) : null;
    }

    public BankOperatorGrant? Find(BankOperatorGrantId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT bank_operator_grant_id, bank_id, discord_user_id, status,
                   granted_by_discord_user_id, granted_at, revoked_at, version
            FROM bank_operator_grants
            WHERE bank_operator_grant_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? Read(reader) : null;
    }

    private static string Text(DiscordUserId discordUserId) =>
        discordUserId.Value.ToString(CultureInfo.InvariantCulture);

    private static BankOperatorGrant Read(SqliteDataReader reader) =>
        BankOperatorGrant.Rehydrate(
            BankOperatorGrantId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            BankId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            DiscordUserId.FromUInt64(ulong.Parse(reader.GetString(2), CultureInfo.InvariantCulture)),
            BankOperatorGrantCatalog.ParseToken(reader.GetString(3)),
            DiscordUserId.FromUInt64(ulong.Parse(reader.GetString(4), CultureInfo.InvariantCulture)),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(5)),
            reader.IsDBNull(6) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(6)),
            reader.GetInt64(7));
}
