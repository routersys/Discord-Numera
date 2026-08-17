using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Numera.Analyzers;

namespace Numera.Generators.Tests;

internal static class AnalyzerHarness
{
    private static readonly MetadataReference[] References = BuildReferences();

    internal static string[] Run(string source, params DiagnosticAnalyzer[] analyzers)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Numera.Analyzers.Tests.Sample",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<Diagnostic> diagnostics = compilation
            .WithAnalyzers([.. analyzers])
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();

        return [.. diagnostics.Select(static diagnostic => diagnostic.Id).Order()];
    }

    private static MetadataReference[] BuildReferences()
    {
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Threading.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Threading.Tasks.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Text.RegularExpressions.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Collections.dll")),
        ];
    }
}

[TestClass]
public sealed class RegexUsageAnalyzerTests
{
    private static string[] Run(string source) => AnalyzerHarness.Run(source, new RegexUsageAnalyzer());

    [TestMethod]
    public void CanonicalGeneratedRegexProducesNoDiagnostic() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), Run("""
            using System.Text.RegularExpressions;

            internal static partial class CanonicalRegexes
            {
                [GeneratedRegex(@"\A[A-Z][A-Z0-9_]{0,31}\z", RegexOptions.CultureInvariant, 100)]
                internal static partial Regex ProtectionClassCode();
            }
            """));

    [TestMethod]
    public void RuntimeRegexConstructionIsRejected() =>
        CollectionAssert.Contains(Run("""
            using System.Text.RegularExpressions;

            internal static class Probe
            {
                internal static Regex Build() => new Regex("abc");
            }
            """), "ECONREG001");

    [TestMethod]
    public void StaticRegexApiWithPatternIsRejected() =>
        CollectionAssert.Contains(Run("""
            using System.Text.RegularExpressions;

            internal static class Probe
            {
                internal static bool Check(string input) => Regex.IsMatch(input, "abc");
            }
            """), "ECONREG001");

    [TestMethod]
    public void MissingCultureInvariantIsRejected() =>
        CollectionAssert.Contains(Run("""
            using System.Text.RegularExpressions;

            internal static partial class CanonicalRegexes
            {
                [GeneratedRegex(@"\Aabc\z", RegexOptions.None, 100)]
                internal static partial Regex Probe();
            }
            """), "ECONREG002");

    [TestMethod]
    public void MissingTimeoutIsRejected() =>
        CollectionAssert.Contains(Run("""
            using System.Text.RegularExpressions;

            internal static partial class CanonicalRegexes
            {
                [GeneratedRegex(@"\Aabc\z", RegexOptions.CultureInvariant)]
                internal static partial Regex Probe();
            }
            """), "ECONREG003");

    [TestMethod]
    public void WrongTimeoutIsRejected() =>
        CollectionAssert.Contains(Run("""
            using System.Text.RegularExpressions;

            internal static partial class CanonicalRegexes
            {
                [GeneratedRegex(@"\Aabc\z", RegexOptions.CultureInvariant, 250)]
                internal static partial Regex Probe();
            }
            """), "ECONREG003");

    [TestMethod]
    public void CompiledOptionIsRejected() =>
        CollectionAssert.Contains(Run("""
            using System.Text.RegularExpressions;

            internal static partial class CanonicalRegexes
            {
                [GeneratedRegex(@"\Aabc\z", RegexOptions.CultureInvariant | RegexOptions.Compiled, 100)]
                internal static partial Regex Probe();
            }
            """), "ECONREG004");

    [TestMethod]
    public void NonBacktrackingOptionIsRejected() =>
        CollectionAssert.Contains(Run("""
            using System.Text.RegularExpressions;

            internal static partial class CanonicalRegexes
            {
                [GeneratedRegex(@"\Aabc\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 100)]
                internal static partial Regex Probe();
            }
            """), "ECONREG004");
}

[TestClass]
public sealed class ForbiddenApiAnalyzerTests
{
    private static string[] Run(string source) => AnalyzerHarness.Run(source, new ForbiddenApiAnalyzer());

    [TestMethod]
    public void AwaitedCallProducesNoDiagnostic() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), Run("""
            using System.Threading.Tasks;

            internal static class Probe
            {
                internal static async Task<int> ReadAsync() => await Task.FromResult(1);
            }
            """));

    [TestMethod]
    public void TaskResultIsRejected() =>
        CollectionAssert.Contains(Run("""
            using System.Threading.Tasks;

            internal static class Probe
            {
                internal static int Read() => Task.FromResult(1).Result;
            }
            """), "ECONAPI001");

    [TestMethod]
    public void TaskWaitIsRejected() =>
        CollectionAssert.Contains(Run("""
            using System.Threading.Tasks;

            internal static class Probe
            {
                internal static void Read() => Task.CompletedTask.Wait();
            }
            """), "ECONAPI001");

    [TestMethod]
    public void GetAwaiterGetResultIsRejected() =>
        CollectionAssert.Contains(Run("""
            using System.Threading.Tasks;

            internal static class Probe
            {
                internal static int Read() => Task.FromResult(1).GetAwaiter().GetResult();
            }
            """), "ECONAPI001");

    [TestMethod]
    public void AmbientClockAccessIsRejected()
    {
        CollectionAssert.Contains(Run("""
            using System;

            internal static class Probe
            {
                internal static DateTime Read() => DateTime.UtcNow;
            }
            """), "ECONAPI002");

        CollectionAssert.Contains(Run("""
            using System;

            internal static class Probe
            {
                internal static DateTimeOffset Read() => DateTimeOffset.Now;
            }
            """), "ECONAPI002");
    }

    [TestMethod]
    public void CalendarHelpersAreNotFlagged() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), Run("""
            using System;

            internal static class Probe
            {
                internal static int Read() => DateTime.DaysInMonth(2024, 2);
            }
            """));
}
