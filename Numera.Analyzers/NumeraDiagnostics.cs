using Microsoft.CodeAnalysis;

namespace Numera.Analyzers;

internal static class NumeraDiagnostics
{
    private const string RegexCategory = "Numera.Regex";
    private const string ApiCategory = "Numera.Api";

    internal static readonly DiagnosticDescriptor RuntimeRegexApi = Create(
        "ECONREG001",
        RegexCategory,
        "Runtime で Pattern を解析する Regex API を使用しています",
        "'{0}' は Runtime で Pattern を解析します。GeneratedRegexAttribute による Compile-time 生成へ置き換えてください。");

    internal static readonly DiagnosticDescriptor CultureInvariantMissing = Create(
        "ECONREG002",
        RegexCategory,
        "GeneratedRegex へ CultureInvariant が指定されていません",
        "GeneratedRegex '{0}' へ RegexOptions.CultureInvariant を必ず指定してください。");

    internal static readonly DiagnosticDescriptor MatchTimeoutInvalid = Create(
        "ECONREG003",
        RegexCategory,
        "GeneratedRegex の Timeout が規定値ではありません",
        "GeneratedRegex '{0}' の matchTimeoutMilliseconds を 100 へ固定してください。無期限を許可しません。");

    internal static readonly DiagnosticDescriptor ForbiddenRegexOption = Create(
        "ECONREG004",
        RegexCategory,
        "GeneratedRegex へ禁止された RegexOptions を指定しています",
        "GeneratedRegex '{0}' へ RegexOptions.{1} を指定できません。");

    internal static readonly DiagnosticDescriptor RuntimePatternComposition = Create(
        "ECONREG005",
        RegexCategory,
        "Regex Pattern を Runtime 値から構築しています",
        "GeneratedRegex '{0}' の Pattern は Compile-time 定数でなければなりません。");

    internal static readonly DiagnosticDescriptor BlockingAsyncCall = Create(
        "ECONAPI001",
        ApiCategory,
        "非同期処理を同期的に待機しています",
        "'{0}' は Deadlock と Thread 枯渇を招きます。await を使用してください。");

    internal static readonly DiagnosticDescriptor AmbientClockAccess = Create(
        "ECONAPI002",
        ApiCategory,
        "環境時刻へ直接アクセスしています",
        "'{0}' を直接呼び出さず、IClock を経由して時刻を取得してください。");

    private static DiagnosticDescriptor Create(string id, string category, string title, string messageFormat) =>
        new(id, title, messageFormat, category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
