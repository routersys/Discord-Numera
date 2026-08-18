using System.Globalization;
using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteCustomerIdentityReadRepository : ICustomerIdentityReadRepository
{
    private readonly SqliteConnection connection;

    internal SqliteCustomerIdentityReadRepository(SqliteConnection connection) => this.connection = connection;

    public CustomerAccountStatusView? FindByDiscordUser(DiscordUserId discordUserId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.customer_account_id, c.public_handle, c.display_name, c.status, c.created_at
            FROM discord_identity_links AS l
            INNER JOIN customer_accounts AS c ON c.customer_account_id = l.customer_account_id
            WHERE l.discord_user_id = $discordUserId
              AND l.status = 'ACTIVE'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$discordUserId",
            discordUserId.Value.ToString(CultureInfo.InvariantCulture));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new CustomerAccountStatusView(
            CustomerAccountId.FromValue(EntityIdValue.FromBytes((byte[])reader[0])),
            reader.GetString(1),
            reader.GetString(2),
            CustomerAccountStatusCatalog.ParseToken(reader.GetString(3)),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(4)));
    }
}
