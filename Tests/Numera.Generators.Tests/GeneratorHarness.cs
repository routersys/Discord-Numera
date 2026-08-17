using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Numera.Discord.Generators;

namespace Numera.Generators.Tests;

internal sealed record GeneratorRun(ImmutableArray<Diagnostic> Diagnostics, string ManifestSource)
{
    internal string[] ErrorIds =>
        [.. Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Id).Order()];

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
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        GeneratorDriverRunResult result = driver.GetRunResult();

        string manifest = result.GeneratedTrees
            .Where(static tree => tree.FilePath.EndsWith("EconomyCommandManifest.g.cs", StringComparison.Ordinal))
            .Select(static tree => tree.GetText().ToString())
            .FirstOrDefault() ?? string.Empty;

        return new GeneratorRun(diagnostics, manifest);
    }

    private static MetadataReference[] BuildReferences()
    {
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Threading.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Collections.dll")),
            MetadataReference.CreateFromFile(typeof(Discord.Abstractions.EconomySlashCommandAttribute).Assembly.Location),
        ];
    }
}
