using System.Reflection;

namespace Numera.Architecture.Tests;

internal static class ProjectLayout
{
    internal static Assembly Domain => typeof(Numera.Domain.Common.EntityIdValue).Assembly;

    internal static Assembly Application => typeof(Numera.Application.Common.Result).Assembly;

    internal static Assembly Persistence => typeof(Numera.Persistence.Sqlite.SqliteConnectionFactory).Assembly;

    internal static Assembly Discord => typeof(Numera.Discord.Rendering.TextCatalog).Assembly;

    internal static Assembly[] Assemblies => [Domain, Application, Persistence, Discord];

    internal static string RepositoryRoot { get; } = Locate();

    internal static IEnumerable<string> SourceFiles(string projectDirectory) =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, projectDirectory), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));

    private static string Locate()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Numera.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Numera.slnx が見つかりません。");
    }
}
