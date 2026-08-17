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
    public const string ErrorValidationTitle = "error.validation.title";
    public const string ErrorValidationDescription = "error.validation.description";
    public const string ErrorNotFoundTitle = "error.not_found.title";
    public const string ErrorNotFoundDescription = "error.not_found.description";
    public const string ErrorForbiddenTitle = "error.forbidden.title";
    public const string ErrorForbiddenDescription = "error.forbidden.description";
    public const string ErrorConflictTitle = "error.conflict.title";
    public const string ErrorConflictDescription = "error.conflict.description";
    public const string ErrorInsufficientFundsTitle = "error.insufficient_funds.title";
    public const string ErrorInsufficientFundsDescription = "error.insufficient_funds.description";
    public const string ErrorBankUnavailableTitle = "error.bank_unavailable.title";
    public const string ErrorBankUnavailableDescription = "error.bank_unavailable.description";
    public const string ErrorAccountRestrictedTitle = "error.account_restricted.title";
    public const string ErrorAccountRestrictedDescription = "error.account_restricted.description";
    public const string ErrorOperationExpiredTitle = "error.operation_expired.title";
    public const string ErrorOperationExpiredDescription = "error.operation_expired.description";
    public const string ErrorConcurrencyConflictTitle = "error.concurrency_conflict.title";
    public const string ErrorConcurrencyConflictDescription = "error.concurrency_conflict.description";
    public const string ErrorInfrastructureUnavailableTitle = "error.infrastructure_unavailable.title";
    public const string ErrorInfrastructureUnavailableDescription = "error.infrastructure_unavailable.description";
    public const string ErrorUnexpectedTitle = "error.unexpected.title";
    public const string ErrorUnexpectedDescription = "error.unexpected.description";
    public const string ErrorFooter = "error.footer";
    public const string ErrorFooterWithCode = "error.footer_with_code";
    public const string PresenceActivity = "presence.activity";
}

public static class CanonicalTextCatalog
{
    public static IReadOnlyDictionary<string, string> Entries { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TextCatalogKeys.ErrorValidationTitle] = "入力内容を確認してください",
            [TextCatalogKeys.ErrorValidationDescription] = "入力内容に問題があります。表示された項目を修正してください。",
            [TextCatalogKeys.ErrorNotFoundTitle] = "対象が見つかりません",
            [TextCatalogKeys.ErrorNotFoundDescription] = "指定した情報を確認し、もう一度操作してください。",
            [TextCatalogKeys.ErrorForbiddenTitle] = "この操作は実行できません",
            [TextCatalogKeys.ErrorForbiddenDescription] = "この操作を行う権限がありません。",
            [TextCatalogKeys.ErrorConflictTitle] = "現在の状態では実行できません",
            [TextCatalogKeys.ErrorConflictDescription] = "情報が更新されています。最新の状態を確認してからやり直してください。",
            [TextCatalogKeys.ErrorInsufficientFundsTitle] = "利用可能残高が不足しています",
            [TextCatalogKeys.ErrorInsufficientFundsDescription] = "振込金額と手数料の合計に対して利用可能残高が不足しています。",
            [TextCatalogKeys.ErrorBankUnavailableTitle] = "現在この銀行を利用できません",
            [TextCatalogKeys.ErrorBankUnavailableDescription] = "銀行または決済機能が利用できない状態です。時間をおいて再度確認してください。",
            [TextCatalogKeys.ErrorAccountRestrictedTitle] = "この口座では操作できません",
            [TextCatalogKeys.ErrorAccountRestrictedDescription] = "口座の状態により、この操作は現在利用できません。",
            [TextCatalogKeys.ErrorOperationExpiredTitle] = "操作の有効期限が切れました",
            [TextCatalogKeys.ErrorOperationExpiredDescription] = "この操作は続行できません。最初からやり直してください。",
            [TextCatalogKeys.ErrorConcurrencyConflictTitle] = "情報が更新されました",
            [TextCatalogKeys.ErrorConcurrencyConflictDescription] = "他の処理によって状態が変更されました。最新の状態を確認してからやり直してください。",
            [TextCatalogKeys.ErrorInfrastructureUnavailableTitle] = "現在処理を完了できません",
            [TextCatalogKeys.ErrorInfrastructureUnavailableDescription] = "一時的な問題が発生しています。時間をおいて再度実行してください。",
            [TextCatalogKeys.ErrorUnexpectedTitle] = "処理中にエラーが発生しました",
            [TextCatalogKeys.ErrorUnexpectedDescription] =
                "処理を完了できませんでした。時間をおいて再度実行してください。改善しない場合は操作IDを管理者へ伝えてください。",
            [TextCatalogKeys.ErrorFooter] = "操作ID: {operationPublicId}",
            [TextCatalogKeys.ErrorFooterWithCode] = "操作ID: {operationPublicId} / エラーコード: {errorCode}",
            [TextCatalogKeys.PresenceActivity] = "銀行システム",
        };

    public static TextCatalog Create() => TextCatalog.Create(Entries);
}
