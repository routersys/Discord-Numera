namespace Numera.Architecture.Tests;

internal sealed record FeatureClosureColumn(string Key, string[] References);

internal sealed record FeatureClosureDeclaration(string FeatureId, FeatureClosureColumn[] Columns);

internal sealed record CanonicalUseCase(int Row, string[] Routes, string[] UseCaseNames);

internal static class ClosureCatalog
{
    internal static readonly string[] CanonicalColumnOrder =
    [
        "invariant",
        "state-transition",
        "linearization",
        "canonical-data-contract",
        "command-permission",
        "public-application-api",
        "expected-outcome-test",
        "resource-budget",
    ];

    private static readonly Dictionary<string, string[]> Identifiers = LoadIdentifiers();

    internal static string[] Invariants => Identifiers["invariants"];

    internal static string[] StateTransitions => Identifiers["state-transitions"];

    internal static string[] Linearizations => Identifiers["linearizations"];

    internal static string[] ExpectedOutcomes => Identifiers["expected-outcomes"];

    internal static string[] ResourceBudgets => Identifiers["resource-budgets"];

    internal static string[] ApiMembers => Identifiers["api-members"];

    internal static string[] ApiInterfaces { get; } =
    [
        .. Identifiers["api-members"]
            .Select(static member => member[..member.IndexOf('.', StringComparison.Ordinal)])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    internal static string[] RequiredTables { get; } = ReadLines("RequiredTables.txt");

    internal static string[] CommandRoutes { get; } = ReadLines("CommandRoutes.txt");

    internal static CanonicalUseCase[] UseCases { get; } = LoadUseCases();

    internal static FeatureClosureDeclaration[] Features { get; } = LoadFeatures();

    internal static string[] MembersOf(string interfaceName) =>
    [
        .. Identifiers["api-members"]
            .Where(member => member.StartsWith(interfaceName + ".", StringComparison.Ordinal))
            .Select(static member => member[(member.IndexOf('.', StringComparison.Ordinal) + 1)..]),
    ];

    internal static string[] ExpandInvariantReference(string reference)
    {
        int range = reference.IndexOf("..", StringComparison.Ordinal);

        if (range < 0)
        {
            return [reference];
        }

        string head = reference[..range];
        string tail = reference[(range + 2)..];
        int separator = head.LastIndexOf('-');

        if (separator < 0 ||
            !int.TryParse(head[(separator + 1)..], out int first) ||
            !int.TryParse(tail, out int last) ||
            last < first)
        {
            return [reference];
        }

        string prefix = head[..(separator + 1)];
        int width = tail.Length;

        return [.. Enumerable.Range(first, last - first + 1).Select(value => prefix + value.ToString().PadLeft(width, '0'))];
    }

    internal static string DataFile(string fileName) =>
        Path.Combine(
            ProjectLayout.RepositoryRoot,
            "Tests",
            "Numera.Architecture.Tests",
            "Conformance",
            fileName);

    private static string[] ReadLines(string fileName) =>
    [
        .. File.ReadAllLines(DataFile(fileName))
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0),
    ];

    private static string[] SplitList(string value) =>
    [
        .. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    ];

    private static Dictionary<string, string[]> LoadIdentifiers()
    {
        Dictionary<string, List<string>> sections = new(StringComparer.Ordinal);
        List<string>? current = null;

        foreach (string line in ReadLines("CanonicalIdentifiers.txt"))
        {
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = [];
                sections[line[1..^1]] = current;
                continue;
            }

            current?.Add(line);
        }

        return sections.ToDictionary(
            static section => section.Key,
            static section => section.Value.ToArray(),
            StringComparer.Ordinal);
    }

    private static CanonicalUseCase[] LoadUseCases()
    {
        List<CanonicalUseCase> entries = [];
        int row = 0;

        foreach (string line in ReadLines("UseCases.txt"))
        {
            row++;
            int separator = line.IndexOf('|');

            if (separator < 0)
            {
                continue;
            }

            entries.Add(new CanonicalUseCase(
                row,
                SplitList(line[..separator]),
                SplitList(line[(separator + 1)..])));
        }

        return [.. entries];
    }

    private static FeatureClosureDeclaration[] LoadFeatures()
    {
        List<FeatureClosureDeclaration> features = [];
        string? featureId = null;
        List<FeatureClosureColumn> columns = [];

        void Flush()
        {
            if (featureId is not null)
            {
                features.Add(new FeatureClosureDeclaration(featureId, [.. columns]));
            }
        }

        foreach (string line in ReadLines("FeatureClosure.txt"))
        {
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                featureId = line[1..^1];
                columns = [];
                continue;
            }

            int separator = line.IndexOf('=');

            if (separator < 0)
            {
                continue;
            }

            columns.Add(new FeatureClosureColumn(
                line[..separator].Trim(),
                SplitList(line[(separator + 1)..])));
        }

        Flush();

        return [.. features];
    }
}
