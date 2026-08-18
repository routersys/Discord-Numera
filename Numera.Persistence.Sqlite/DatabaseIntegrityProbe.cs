using Microsoft.Data.Sqlite;

namespace Numera.Persistence.Sqlite;

public sealed record DatabaseProbeResult(bool IsOk, string Detail)
{
    public static DatabaseProbeResult Ok { get; } = new(true, SqlitePragmaGuard.IntegrityOk);

    public static DatabaseProbeResult Failed(string detail) => new(false, detail);
}

public interface IDatabaseIntegrityProbe
{
    DatabaseProbeResult QuickCheck();

    DatabaseProbeResult ForeignKeyCheck();

    DatabaseProbeResult IntegrityCheck();
}

public sealed class SqliteDatabaseIntegrityProbe : IDatabaseIntegrityProbe
{
    public const string QuickCheckStatement = "PRAGMA quick_check;";
    public const string ForeignKeyCheckStatement = "PRAGMA foreign_key_check;";
    public const string IntegrityCheckStatement = "PRAGMA integrity_check;";

    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteDatabaseIntegrityProbe(SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        this.connectionFactory = connectionFactory;
    }

    public DatabaseProbeResult QuickCheck() => Scalar(QuickCheckStatement);

    public DatabaseProbeResult IntegrityCheck() => Scalar(IntegrityCheckStatement);

    public DatabaseProbeResult ForeignKeyCheck()
    {
        using SqliteConnection connection = connectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = ForeignKeyCheckStatement;

        using SqliteDataReader reader = command.ExecuteReader();
        int violations = 0;

        while (reader.Read())
        {
            violations++;
        }

        return violations == 0
            ? DatabaseProbeResult.Ok
            : DatabaseProbeResult.Failed(violations.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private DatabaseProbeResult Scalar(string statement)
    {
        using SqliteConnection connection = connectionFactory.OpenRuntimeConnection();
        string result = SqliteConnectionFactory.ReadScalarText(connection, statement);

        return string.Equals(result, SqlitePragmaGuard.IntegrityOk, StringComparison.Ordinal)
            ? DatabaseProbeResult.Ok
            : DatabaseProbeResult.Failed(result);
    }
}
