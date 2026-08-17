using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Numera.Persistence.Sqlite.Migrations;

public sealed class SqlMigration
{
    public const int VersionDigits = 4;

    private SqlMigration(int version, string name, string script, string checksum)
    {
        Version = version;
        Name = name;
        Script = script;
        Checksum = checksum;
    }

    public int Version { get; }

    public string Name { get; }

    public string Script { get; }

    public string Checksum { get; }

    public static SqlMigration Create(string fileName, string script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (string.IsNullOrWhiteSpace(script))
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.MigrationScriptEmpty);
        }

        (int version, string name) = ParseFileName(fileName);
        return new SqlMigration(version, name, script, ComputeChecksum(script));
    }

    public static string ComputeChecksum(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(script)));
        return Convert.ToHexStringLower(hash);
    }

    private static string Normalize(string script) => script.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static (int Version, string Name) ParseFileName(string fileName)
    {
        ReadOnlySpan<char> stem = fileName.AsSpan();
        int extensionIndex = stem.LastIndexOf('.');
        if (extensionIndex >= 0)
        {
            stem = stem[..extensionIndex];
        }

        int separatorIndex = stem.IndexOf('_');
        if (separatorIndex != VersionDigits || stem.Length <= separatorIndex + 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.MigrationResourceNameInvalid);
        }

        ReadOnlySpan<char> versionText = stem[..separatorIndex];
        foreach (char character in versionText)
        {
            if (character is < '0' or > '9')
            {
                throw PersistenceFailureException.Create(PersistenceFailureCode.MigrationResourceNameInvalid);
            }
        }

        int version = int.Parse(versionText, NumberStyles.None, CultureInfo.InvariantCulture);
        if (version < 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.MigrationResourceNameInvalid);
        }

        return (version, stem[(separatorIndex + 1)..].ToString());
    }
}
