using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Numera.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RegexUsageAnalyzer : DiagnosticAnalyzer
{
    private const string RegexTypeName = "System.Text.RegularExpressions.Regex";
    private const string GeneratedRegexAttributeName = "System.Text.RegularExpressions.GeneratedRegexAttribute";
    private const int RequiredMatchTimeoutMilliseconds = 100;
    private const int CompiledFlag = 8;
    private const int CultureInvariantFlag = 512;
    private const int NonBacktrackingFlag = 1024;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        NumeraDiagnostics.RuntimeRegexApi,
        NumeraDiagnostics.CultureInvariantMissing,
        NumeraDiagnostics.MatchTimeoutInvalid,
        NumeraDiagnostics.ForbiddenRegexOption,
        NumeraDiagnostics.RuntimePatternComposition,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterSymbolAction(AnalyzeMethodSymbol, SymbolKind.Method);
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        IObjectCreationOperation operation = (IObjectCreationOperation)context.Operation;

        if (operation.Type?.ToDisplayString() == RegexTypeName)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NumeraDiagnostics.RuntimeRegexApi, operation.Syntax.GetLocation(), "new Regex"));
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        IInvocationOperation operation = (IInvocationOperation)context.Operation;
        IMethodSymbol method = operation.TargetMethod;

        if (!method.IsStatic || method.ContainingType?.ToDisplayString() != RegexTypeName)
        {
            return;
        }

        bool takesPattern = method.Parameters.Any(static parameter =>
            parameter.Name == "pattern" && parameter.Type.SpecialType == SpecialType.System_String);

        if (takesPattern)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NumeraDiagnostics.RuntimeRegexApi,
                operation.Syntax.GetLocation(),
                $"Regex.{method.Name}"));
        }
    }

    private static void AnalyzeMethodSymbol(SymbolAnalysisContext context)
    {
        IMethodSymbol method = (IMethodSymbol)context.Symbol;

        AttributeData? attribute = method.GetAttributes().FirstOrDefault(
            static data => data.AttributeClass?.ToDisplayString() == GeneratedRegexAttributeName);

        if (attribute is null)
        {
            return;
        }

        Location location = attribute.ApplicationSyntaxReference is { } reference
            ? Location.Create(reference.SyntaxTree, reference.Span)
            : method.Locations.FirstOrDefault() ?? Location.None;

        ImmutableArray<TypedConstant> arguments = attribute.ConstructorArguments;

        if (arguments.Length == 0 || arguments[0].Value is not string)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NumeraDiagnostics.RuntimePatternComposition, location, method.Name));
            return;
        }

        int options = arguments.Length > 1 && arguments[1].Value is int optionValue ? optionValue : 0;

        if ((options & CultureInvariantFlag) == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NumeraDiagnostics.CultureInvariantMissing, location, method.Name));
        }

        if ((options & CompiledFlag) != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NumeraDiagnostics.ForbiddenRegexOption, location, method.Name, "Compiled"));
        }

        if ((options & NonBacktrackingFlag) != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NumeraDiagnostics.ForbiddenRegexOption, location, method.Name, "NonBacktracking"));
        }

        bool timeoutValid = arguments.Length > 2
            && arguments[2].Value is int timeout
            && timeout == RequiredMatchTimeoutMilliseconds;

        if (!timeoutValid)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NumeraDiagnostics.MatchTimeoutInvalid, location, method.Name));
        }
    }
}
