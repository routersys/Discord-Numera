using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Numera.Discord.Generators;

namespace Numera.Generators.Tests;

internal sealed record GeneratorRun(
    ImmutableArray<Diagnostic> Diagnostics,
    string ManifestSource,
    string AdapterSource,
    ImmutableArray<Diagnostic> CompilationDiagnostics)
{
    internal string[] ErrorIds =>
        [.. Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Id).Order()];

    internal string[] CompilationErrors =>
    [
        .. CompilationDiagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => $"{d.Id} {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")
            .Order(StringComparer.Ordinal),
    ];

    internal bool HasError(string id) => Diagnostics.Any(d => d.Id == id);
}

internal static class GeneratorHarness
{
    private static readonly MetadataReference[] References = BuildReferences();

    internal static GeneratorRun Run(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Numera.Generators.Tests.Sample",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver
            .Create(new EconomyCommandGenerator())
            .RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation updated,
                out ImmutableArray<Diagnostic> diagnostics);

        GeneratorDriverRunResult result = driver.GetRunResult();

        return new GeneratorRun(
            diagnostics,
            Emitted(result, "EconomyCommandManifest.g.cs"),
            Emitted(result, "EconomyGeneratedModules.g.cs"),
            updated.GetDiagnostics());
    }

    private static string Emitted(GeneratorDriverRunResult result, string fileName) =>
        result.GeneratedTrees
            .Where(tree => tree.FilePath.EndsWith(fileName, StringComparison.Ordinal))
            .Select(static tree => tree.GetText().ToString())
            .FirstOrDefault() ?? string.Empty;

    private static MetadataReference[] BuildReferences()
    {
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Threading.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Linq.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "netstandard.dll")),
            MetadataReference.CreateFromFile(typeof(Discord.Abstractions.EconomySlashCommandAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Discord.Gateway.IGeneratedEndpointDispatcher).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::Discord.IUser).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::Discord.Interactions.InteractionService).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::Discord.WebSocket.DiscordSocketClient).Assembly.Location),
        ];
    }
}
