using Microsoft.CodeAnalysis;

namespace Numera.Discord.Generators;

internal static class CommandDiagnostics
{
    private const string Category = "Numera.Discord.Commands";

    internal static readonly DiagnosticDescriptor DuplicateCommandName = Create(
        "ECONCMD001",
        "Application Command が重複しています",
        "種別 {0}、Scope {1} の Command 名 '{2}' が重複しています。Discord へ同一 Key の Command を二重登録できません。");

    internal static readonly DiagnosticDescriptor CommandNameFormatInvalid = Create(
        "ECONCMD002",
        "Command 名が Discord の命名規則に違反しています",
        "Command 名 '{0}' は ASCII 小文字と数字とハイフンとアンダースコアだけで構成し、先頭と末尾をハイフンにできません。");

    internal static readonly DiagnosticDescriptor CommandNameLengthInvalid = Create(
        "ECONCMD003",
        "Command 名の長さが範囲外です",
        "Command 名 '{0}' の長さは 1 文字以上 32 文字以下でなければなりません。");

    internal static readonly DiagnosticDescriptor DescriptionLengthInvalid = Create(
        "ECONCMD004",
        "Description の長さが範囲外です",
        "'{0}' の Description は 1 文字以上 100 文字以下でなければなりません。");

    internal static readonly DiagnosticDescriptor OptionCountExceeded = Create(
        "ECONCMD005",
        "Slash Option が上限を超えています",
        "Command '{0}' の Option は 25 個以下でなければなりませんが {1} 個あります。");

    internal static readonly DiagnosticDescriptor DuplicateOptionName = Create(
        "ECONCMD006",
        "Slash Option 名が重複しています",
        "Command '{0}' の Option 名 '{1}' が重複しています。");

    internal static readonly DiagnosticDescriptor RequiredOptionAfterOptional = Create(
        "ECONCMD007",
        "必須 Option が任意 Option より後ろにあります",
        "Command '{0}' の Option '{1}' は必須ですが、任意 Option より後ろに定義されています。Discord は必須 Option を先に並べることを要求します。");

    internal static readonly DiagnosticDescriptor ChoiceCountExceeded = Create(
        "ECONCMD008",
        "Choice が上限を超えています",
        "Option '{0}' の Choice は 25 個以下でなければなりませんが {1} 個あります。");

    internal static readonly DiagnosticDescriptor ChoiceAndAutocompleteTogether = Create(
        "ECONCMD009",
        "Choice と Autocomplete を同時に指定しています",
        "Option '{0}' へ Choice と Autocomplete を同時に指定できません。");

    internal static readonly DiagnosticDescriptor AutocompleteProviderMissing = Create(
        "ECONCMD010",
        "Autocomplete Provider が存在しません",
        "Option '{0}' が参照する Autocomplete Provider Key '{1}' に対応する Provider が定義されていません。");

    internal static readonly DiagnosticDescriptor DuplicateComponentAction = Create(
        "ECONCMD011",
        "Component Action が重複しています",
        "Component Action '{0}' が重複しています。");

    internal static readonly DiagnosticDescriptor DuplicateModalAction = Create(
        "ECONCMD012",
        "Modal Action が重複しています",
        "Modal Action '{0}' が重複しています。");

    internal static readonly DiagnosticDescriptor DuplicateAutocompleteProviderKey = Create(
        "ECONCMD013",
        "Autocomplete Provider Key が重複しています",
        "Autocomplete Provider Key '{0}' が重複しています。");

    internal static readonly DiagnosticDescriptor EndpointReturnTypeInvalid = Create(
        "ECONCMD014",
        "Endpoint の戻り値型が規定と一致しません",
        "Endpoint '{0}' の戻り値型は '{1}' でなければなりません。");

    internal static readonly DiagnosticDescriptor CancellationTokenParameterInvalid = Create(
        "ECONCMD015",
        "CancellationToken 引数が規定位置にありません",
        "Endpoint '{0}' は最後の引数として CancellationToken を受け取らなければなりません。");

    internal static readonly DiagnosticDescriptor ContextCommandDescriptionForbidden = Create(
        "ECONCMD016",
        "Context Command へ Description を指定しています",
        "User Command と Message Command へ Description を指定できません。Endpoint '{0}' から Description を削除してください。");

    internal static readonly DiagnosticDescriptor EmojiInPublicText = Create(
        "ECONCMD017",
        "公開文言へ Emoji が含まれています",
        "'{0}' の公開文言へ Emoji を含めることはできません。");

    internal static readonly DiagnosticDescriptor GroupDepthExceeded = Create(
        "ECONCMD018",
        "Slash Command の階層が深すぎます",
        "Command '{0}' の階層は Group 2 段までです。Discord は 3 段以上の入れ子を受理しません。");

    internal static readonly DiagnosticDescriptor ScopeCommandCountExceeded = Create(
        "ECONCMD019",
        "Scope あたりの Command 数が上限を超えています",
        "種別 {0} の Command は {1} 個以下でなければなりませんが {2} 個あります。");

    internal static readonly DiagnosticDescriptor CustomIdTooLong = Create(
        "ECONCMD020",
        "Custom ID が 100 文字を超える可能性があります",
        "'{0}' の Custom ID は 100 文字以下でなければなりません。");

    internal static readonly DiagnosticDescriptor ComponentKindMissing = Create(
        "ECONCMD021",
        "Component の種別が確定していません",
        "Component Endpoint '{0}' は Button または Select のどちらかを明示しなければなりません。");

    internal static readonly DiagnosticDescriptor ModalFieldCustomIdDuplicated = Create(
        "ECONCMD022",
        "Modal Field の Custom ID が重複しています",
        "Modal Form '{0}' の Field Custom ID '{1}' が重複しています。");

    internal static readonly DiagnosticDescriptor ModalTextLengthInvalid = Create(
        "ECONCMD023",
        "Modal の表示文言が Discord の長さ制限に違反しています",
        "'{0}' の {1} は {2} 文字以下でなければなりません。");

    private static DiagnosticDescriptor Create(string id, string title, string messageFormat) =>
        new(id, title, messageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
