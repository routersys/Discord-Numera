using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Numera.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ForbiddenApiAnalyzer : DiagnosticAnalyzer
{
    private const string TaskTypeName = "System.Threading.Tasks.Task";
    private const string GenericTaskTypeName = "System.Threading.Tasks.Task<TResult>";
    private const string ValueTaskTypeName = "System.Threading.Tasks.ValueTask";
    private const string DateTimeTypeName = "System.DateTime";
    private const string DateTimeOffsetTypeName = "System.DateTimeOffset";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        NumeraDiagnostics.BlockingAsyncCall,
        NumeraDiagnostics.AmbientClockAccess,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        IPropertyReferenceOperation operation = (IPropertyReferenceOperation)context.Operation;
        IPropertySymbol property = operation.Property;
        string? containingType = property.ContainingType?.ConstructedFrom.ToDisplayString();

        if (property.Name == "Result" && IsTaskLike(containingType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NumeraDiagnostics.BlockingAsyncCall, operation.Syntax.GetLocation(), "Task.Result"));
            return;
        }

        bool ambientClock = property.IsStatic
            && property.Name is "Now" or "UtcNow"
            && containingType is DateTimeTypeName or DateTimeOffsetTypeName;

        if (ambientClock)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NumeraDiagnostics.AmbientClockAccess,
                operation.Syntax.GetLocation(),
                $"{property.ContainingType!.Name}.{property.Name}"));
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        IInvocationOperation operation = (IInvocationOperation)context.Operation;
        IMethodSymbol method = operation.TargetMethod;
        string? containingType = method.ContainingType?.ConstructedFrom.ToDisplayString();

        if (method.Name == "Wait" && IsTaskLike(containingType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NumeraDiagnostics.BlockingAsyncCall, operation.Syntax.GetLocation(), "Task.Wait"));
            return;
        }

        if (method.Name == "GetResult" && containingType is not null && containingType.Contains("Awaiter"))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NumeraDiagnostics.BlockingAsyncCall,
                operation.Syntax.GetLocation(),
                "GetAwaiter().GetResult()"));
        }
    }

    private static bool IsTaskLike(string? typeName) =>
        typeName is TaskTypeName or GenericTaskTypeName or ValueTaskTypeName;
}
