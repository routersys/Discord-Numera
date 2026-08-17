using System.Diagnostics.CodeAnalysis;

namespace Numera.Discord.Rendering;

public interface ITextCatalog
{
    bool TryResolve(string key, out string text);

    string Resolve(string key);

    string Format(string key, IReadOnlyDictionary<string, string> arguments);
}

public sealed class TextCatalog : ITextCatalog
{
    public const char PlaceholderOpen = '{';
    public const char PlaceholderClose = '}';

    private readonly IReadOnlyDictionary<string, string> entries;

    private TextCatalog(IReadOnlyDictionary<string, string> entries) => this.entries = entries;

    public static TextCatalog Create(IReadOnlyDictionary<string, string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Dictionary<string, string> copy = new(entries.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                throw new ArgumentException(TextCatalogFailure.KeyBlank, nameof(entries));
            }

            if (entry.Value is null)
            {
                throw new ArgumentException(TextCatalogFailure.ValueMissing, nameof(entries));
            }

            copy[entry.Key] = entry.Value;
        }

        return new TextCatalog(copy);
    }

    public bool TryResolve(string key, [NotNullWhen(true)] out string text)
    {
        if (entries.TryGetValue(key, out string? value))
        {
            text = value;
            return true;
        }

        text = string.Empty;
        return false;
    }

    public string Resolve(string key) =>
        TryResolve(key, out string text)
            ? text
            : throw new KeyNotFoundException($"{TextCatalogFailure.KeyMissing}: {key}");

    public string Format(string key, IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string template = Resolve(key);
        if (template.IndexOf(PlaceholderOpen) < 0)
        {
            return template;
        }

        System.Text.StringBuilder builder = new(template.Length);

        for (int index = 0; index < template.Length; index++)
        {
            char character = template[index];

            if (character != PlaceholderOpen)
            {
                builder.Append(character);
                continue;
            }

            int close = template.IndexOf(PlaceholderClose, index + 1);
            if (close < 0)
            {
                throw new FormatException($"{TextCatalogFailure.PlaceholderUnclosed}: {key}");
            }

            string placeholder = template[(index + 1)..close];

            if (!arguments.TryGetValue(placeholder, out string? replacement))
            {
                throw new FormatException($"{TextCatalogFailure.PlaceholderUnbound}: {key}/{placeholder}");
            }

            builder.Append(replacement);
            index = close;
        }

        return builder.ToString();
    }
}

public static class TextCatalogFailure
{
    public const string KeyBlank = "Text Catalog の Key を空にできません。";
    public const string ValueMissing = "Text Catalog の値を null にできません。";
    public const string KeyMissing = "Text Catalog へ未登録の Key を参照しました。";
    public const string PlaceholderUnclosed = "Text Catalog の Placeholder が閉じられていません。";
    public const string PlaceholderUnbound = "Text Catalog の Placeholder へ値が与えられていません。";
}

public static class TextCatalogKeys
{
    public const string ErrorValidation = "error.validation";
    public const string ErrorNotFound = "error.not_found";
    public const string ErrorForbidden = "error.forbidden";
    public const string ErrorConflict = "error.conflict";
    public const string ErrorInsufficientFunds = "error.insufficient_funds";
    public const string ErrorBankUnavailable = "error.bank_unavailable";
    public const string ErrorAccountRestricted = "error.account_restricted";
    public const string ErrorOperationExpired = "error.operation_expired";
    public const string ErrorConcurrencyConflict = "error.concurrency_conflict";
    public const string ErrorInfrastructureUnavailable = "error.infrastructure_unavailable";
    public const string ErrorUnexpected = "error.unexpected";
    public const string ErrorTitle = "error.title";
    public const string ErrorFooter = "error.footer";
}
