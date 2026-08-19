using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Numera.Persistence.Sqlite;

public sealed record SystemOwnerSyncOutcome(int Added, int Removed, int Unchanged);

public sealed record EconomyBootstrapOutcome(
    bool IsSuccess,
    string Detail,
    string EconomyScopeId,
    string IssuanceAccountingBookId)
{
    public static EconomyBootstrapOutcome Failed(string detail) =>
        new(false, detail, string.Empty, string.Empty);
}

public interface IDatabaseBootstrapService
{
    SystemOwnerSyncOutcome SynchronizeSystemOwners(
        IReadOnlyList<string> discordUserIds,
        long nowMilliseconds);

    EconomyBootstrapOutcome InitializeEconomy(
        string guildId,
        string canonicalTimezone,
        long nowMilliseconds);

    string? FindEconomyScope(string guildId);
}

public sealed class SqliteDatabaseBootstrapService : IDatabaseBootstrapService
{
    public const string CentralBankPartyName = "中央銀行";
    public const string CentralBankBookKind = "CENTRAL_BANK";

    public const string EconomyAlreadyExists = "ECONOMY_ALREADY_EXISTS";
    public const string GuildIdInvalid = "GUILD_ID_INVALID";
    public const string TimezoneInvalid = "TIMEZONE_INVALID";

    private readonly SqliteConnectionFactory connectionFactory;
    private readonly Func<byte[]> idFactory;

    public SqliteDatabaseBootstrapService(
        SqliteConnectionFactory connectionFactory,
        Func<byte[]> idFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(idFactory);

        this.connectionFactory = connectionFactory;
        this.idFactory = idFactory;
    }

    public SystemOwnerSyncOutcome SynchronizeSystemOwners(
        IReadOnlyList<string> discordUserIds,
        long nowMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(discordUserIds);

        HashSet<string> desired = new(StringComparer.Ordinal);

        foreach (string candidate in discordUserIds)
        {
            if (IsIdentifier(candidate))
            {
                desired.Add(candidate);
            }
        }

        using SqliteConnection connection = connectionFactory.OpenRuntimeConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        HashSet<string> present = new(StringComparer.Ordinal);

        using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT discord_user_id FROM system_owner_identities;";

            using SqliteDataReader reader = read.ExecuteReader();

            while (reader.Read())
            {
                present.Add(reader.GetString(0));
            }
        }

        int added = 0;

        foreach (string owner in desired)
        {
            if (present.Contains(owner))
            {
                continue;
            }

            Execute(
                connection,
                transaction,
                "INSERT INTO system_owner_identities(discord_user_id, created_at) VALUES($id, $now);",
                command =>
                {
                    command.Parameters.AddWithValue("$id", owner);
                    command.Parameters.AddWithValue("$now", nowMilliseconds);
                });

            added++;
        }

        int removed = 0;

        foreach (string owner in present)
        {
            if (desired.Contains(owner))
            {
                continue;
            }

            Execute(
                connection,
                transaction,
                "DELETE FROM system_owner_identities WHERE discord_user_id = $id;",
                command => command.Parameters.AddWithValue("$id", owner));

            removed++;
        }

        transaction.Commit();

        return new SystemOwnerSyncOutcome(added, removed, desired.Count - added);
    }

    public string? FindEconomyScope(string guildId)
    {
        ArgumentNullException.ThrowIfNull(guildId);

        using SqliteConnection connection = connectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT hex(economy_scope_id) FROM guild_economies WHERE guild_id = $guild;";
        command.Parameters.AddWithValue("$guild", guildId);

        return command.ExecuteScalar() as string;
    }

    public EconomyBootstrapOutcome InitializeEconomy(
        string guildId,
        string canonicalTimezone,
        long nowMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(guildId);
        ArgumentNullException.ThrowIfNull(canonicalTimezone);

        if (!IsIdentifier(guildId))
        {
            return EconomyBootstrapOutcome.Failed(GuildIdInvalid);
        }

        if (canonicalTimezone.Length is 0 or > 64)
        {
            return EconomyBootstrapOutcome.Failed(TimezoneInvalid);
        }

        if (FindEconomyScope(guildId) is not null)
        {
            return EconomyBootstrapOutcome.Failed(EconomyAlreadyExists);
        }

        byte[] scopeId = idFactory();
        byte[] partyId = idFactory();
        byte[] bookId = idFactory();
        byte[] periodId = idFactory();

        int year = DateTimeOffset.FromUnixTimeMilliseconds(nowMilliseconds).UtcDateTime.Year;
        string periodKey = year.ToString(CultureInfo.InvariantCulture);

        using SqliteConnection connection = connectionFactory.OpenRuntimeConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        Execute(
            connection,
            transaction,
            """
            INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
            VALUES($id, $guild, $timezone, 'ACTIVE', 1);
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", scopeId);
                command.Parameters.AddWithValue("$guild", guildId);
                command.Parameters.AddWithValue("$timezone", canonicalTimezone);
            });

        Execute(
            connection,
            transaction,
            """
            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES($id, 'SYSTEM', $name, 'ACTIVE', $now, 1);
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", partyId);
                command.Parameters.AddWithValue("$name", CentralBankPartyName);
                command.Parameters.AddWithValue("$now", nowMilliseconds);
            });

        Execute(
            connection,
            transaction,
            """
            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES($id, $owner, 'CENTRAL_BANK', 'OPEN', $now, 1);
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", bookId);
                command.Parameters.AddWithValue("$owner", partyId);
                command.Parameters.AddWithValue("$now", nowMilliseconds);
            });

        Execute(
            connection,
            transaction,
            """
            INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                starts_on, ends_on, status, closed_at, version)
            VALUES($id, $book, $key, $starts, $ends, 'OPEN', NULL, 1);
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", periodId);
                command.Parameters.AddWithValue("$book", bookId);
                command.Parameters.AddWithValue("$key", periodKey);
                command.Parameters.AddWithValue("$starts", periodKey + "-01-01");
                command.Parameters.AddWithValue("$ends", periodKey + "-12-31");
            });

        transaction.Commit();

        return new EconomyBootstrapOutcome(
            true, string.Empty, Convert.ToHexString(scopeId), Convert.ToHexString(bookId));
    }

    internal static bool IsIdentifier(string candidate)
    {
        if (candidate.Length is 0 or > 20)
        {
            return false;
        }

        foreach (char character in candidate)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        Action<SqliteCommand> bind)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        bind(command);
        command.ExecuteNonQuery();
    }
}
