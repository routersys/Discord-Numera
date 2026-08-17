using System.Reflection;

namespace Numera.Persistence.Sqlite.Migrations;

public static class EmbeddedMigrationCatalog
{
    private const string ResourcePrefix = "Numera.Persistence.Sqlite.Migrations.";
    private const string ResourceSuffix = ".sql";

    public static IReadOnlyList<SqlMigration> Load()
    {
        Assembly assembly = typeof(EmbeddedMigrationCatalog).Assembly;
        List<SqlMigration> migrations = [];

        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw PersistenceFailureException.Create(PersistenceFailureCode.MigrationMissing);
            using StreamReader reader = new(stream);

            migrations.Add(SqlMigration.Create(resourceName[ResourcePrefix.Length..], reader.ReadToEnd()));
        }

        migrations.Sort(static (left, right) => left.Version.CompareTo(right.Version));
        return migrations;
    }
}
