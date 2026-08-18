namespace Numera.Discord.Rendering;

internal static class ViewKeys
{
    public const string AccountRegistered = "view.account.registered";
    public const string AccountStatus = "view.account.status";
    public const string BankAccountOpened = "view.bank.opened";
    public const string BankAccountSubmitted = "view.bank.submitted";
    public const string TransferCompleted = "view.bank.transfer_completed";
    public const string TransferAccepted = "view.bank.transfer_accepted";
    public const string Help = "view.help";
    public const string ManageBankCreated = "view.manage.bank_created";
    public const string ManageCurrencyCreated = "view.manage.currency_created";
    public const string ManageCurrencyIssued = "view.manage.currency_issued";
    public const string ManageCurrencyBurned = "view.manage.currency_burned";
    public const string BankAccountClosing = "view.bank.closing";
    public const string ManagePanel = "view.manage.panel";
    public const string SystemPanel = "view.system.panel";
    public const string SystemCommandsSynced = "view.system.commands_synced";
    public const string AccountLinkIssued = "view.account.link_issued";
    public const string AccountLinkConsumed = "view.account.link_consumed";
    public const string AccountUnlinked = "view.account.unlinked";
    public const string BankList = "view.bank.list";
    public const string BankListEmpty = "view.bank.list_empty";
    public const string BankAccountList = "view.bank.accounts";
    public const string BankAccountListEmpty = "view.bank.accounts_empty";
    public const string Statement = "view.bank.statement";
    public const string StatementEmpty = "view.bank.statement_empty";

    public const string TransferSource = "view.bank.transfer_source";
    public const string TransferSourceEmpty = "view.bank.transfer_source_empty";
    public const string TransferInput = "view.bank.transfer_input";
    public const string TransferModal = "view.bank.transfer_modal";
    public const string TransferConfirm = "view.bank.transfer_confirm";

    public const string TransferSourcePlaceholder = "label.bank.transfer_source";
    public const string TransferInputLabel = "label.bank.transfer_input";
    public const string TransferExecuteLabel = "label.bank.transfer_execute";

    public const string FieldSource = "source";
    public const string FieldBank = "bank";
    public const string FieldBranch = "branch";
    public const string FieldAccount = "account";
    public const string FieldAmount = "amount";
    public const string FieldFee = "fee";
    public const string FieldTotal = "total";

    public const string StatusPrefix = "status.";

    public const string StatusUnknown = "status.unknown";

    public static string StatusOf(string internalToken) => StatusPrefix + internalToken;

    public static string FieldLabel(string viewKey, string field) => viewKey + ".field." + field + ".label";

    public static string FieldValue(string viewKey, string field) => viewKey + ".field." + field + ".value";
}

public static partial class CanonicalTextCatalog
{
    public static IReadOnlyDictionary<string, string> ViewEntries { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ViewKeys.AccountRegistered + ".title"] = "登録が完了しました",
            [ViewKeys.AccountRegistered + ".description"] =
                "ハンドル {publicHandle} で登録しました。表示名は {displayName} です。",
            [ViewKeys.AccountStatus + ".title"] = "アカウント情報",
            [ViewKeys.AccountStatus + ".description"] =
                "ハンドル {publicHandle} / 表示名 {displayName} / 状態 {status}",
            [ViewKeys.BankAccountOpened + ".title"] = "口座を開設しました",
            [ViewKeys.BankAccountOpened + ".description"] =
                "{institutionCode} に口座を開設しました。口座番号の下4桁は {accountNumberSuffix} です。",
            [ViewKeys.BankAccountSubmitted + ".title"] = "口座開設を申し込みました",
            [ViewKeys.BankAccountSubmitted + ".description"] =
                "{institutionCode} へ申込を受け付けました。審査の結果をお待ちください。",
            [ViewKeys.TransferCompleted + ".title"] = "振込が完了しました",
            [ViewKeys.TransferCompleted + ".description"] =
                "{amount} を振り込みました。手数料は {fee} です。利用可能残高は {availableBalance} です。",
            [ViewKeys.TransferAccepted + ".title"] = "振込を受け付けました",
            [ViewKeys.TransferAccepted + ".description"] =
                "{amount} の振込を受け付けました。手数料は {fee} です。完了までしばらくお待ちください。",
            [ViewKeys.Help + ".title"] = "Numera の使い方",
            [ViewKeys.Help + ".description"] =
                "口座を作るには /account register を実行します。"
                + "銀行口座の開設は /bank open、振込は /bank transfer です。"
                + "登録状況は /account status で確認できます。",
            [ViewKeys.ManageBankCreated + ".title"] = "銀行を設立しました",
            [ViewKeys.ManageBankCreated + ".description"] =
                "{institutionCode} を {status} で作成しました。名称は {bankName} です。",
            [ViewKeys.ManageCurrencyCreated + ".title"] = "通貨を発行しました",
            [ViewKeys.ManageCurrencyCreated + ".description"] =
                "通貨 {code} を作成しました。発行済のベースマネーは {baseMoneySupply} です。",
            [ViewKeys.ManageCurrencyIssued + ".title"] = "通貨を追加発行しました",
            [ViewKeys.ManageCurrencyIssued + ".description"] =
                "{amount} を追加発行しました。発行済のベースマネーは {baseMoneySupply} です。",
            [ViewKeys.ManageCurrencyBurned + ".title"] = "通貨を償却しました",
            [ViewKeys.ManageCurrencyBurned + ".description"] =
                "{amount} を償却しました。発行済のベースマネーは {baseMoneySupply} です。",
            [ViewKeys.BankAccountClosing + ".title"] = "解約を申し込みました",
            [ViewKeys.BankAccountClosing + ".description"] = "口座の解約手続を開始しました。",
            [ViewKeys.ManagePanel + ".title"] = "管理メニュー",
            [ViewKeys.ManagePanel + ".description"] = "銀行と通貨の管理は /manage の各サブコマンドから行います。",
            [ViewKeys.SystemPanel + ".title"] = "システムメニュー",
            [ViewKeys.SystemPanel + ".description"] = "Command 同期は /system commands-sync から行います。",
            [ViewKeys.SystemCommandsSynced + ".title"] = "Command を同期しました",
            [ViewKeys.SystemCommandsSynced + ".description"] = "作成 {created} 件 / 更新 {updated} 件 / 削除 {deleted} 件です。",
            [ViewKeys.AccountLinkIssued + ".title"] = "連携コードを発行しました",
            [ViewKeys.AccountLinkIssued + ".description"] = "連携コードは {code} です。10分で失効します。",
            [ViewKeys.AccountLinkConsumed + ".title"] = "連携が完了しました",
            [ViewKeys.AccountLinkConsumed + ".description"] = "ハンドル {publicHandle} のアカウントへ連携しました。",
            [ViewKeys.AccountUnlinked + ".title"] = "連携を解除しました",
            [ViewKeys.AccountUnlinked + ".description"] = "この Discord アカウントの連携を解除しました。",
            [ViewKeys.BankList + ".title"] = "銀行の一覧",
            [ViewKeys.BankList + ".description"] = "{count}件の銀行があります。{items}",
            [ViewKeys.BankListEmpty + ".title"] = "銀行がありません",
            [ViewKeys.BankListEmpty + ".description"] = "この経済圏にはまだ銀行がありません。",
            [ViewKeys.BankAccountList + ".title"] = "口座の一覧",
            [ViewKeys.BankAccountList + ".description"] = "{count}件の口座があります。{items}",
            [ViewKeys.BankAccountListEmpty + ".title"] = "口座がありません",
            [ViewKeys.BankAccountListEmpty + ".description"] = "まだ口座がありません。/bank open で開設できます。",
            [ViewKeys.Statement + ".title"] = "取引明細",
            [ViewKeys.Statement + ".description"] = "{count}件の取引があります。{items}",
            [ViewKeys.StatementEmpty + ".title"] = "取引がありません",
            [ViewKeys.StatementEmpty + ".description"] = "この口座にはまだ取引がありません。",
            [ViewKeys.TransferSource + ".title"] = "送金元口座の選択",
            [ViewKeys.TransferSource + ".description"] = "振込に使う口座を選んでください。",
            [ViewKeys.TransferSourceEmpty + ".title"] = "対象がありません",
            [ViewKeys.TransferSourceEmpty + ".description"] = "現在利用できる対象がありません。",
            [ViewKeys.TransferInput + ".title"] = "振込内容の入力",
            [ViewKeys.TransferInput + ".description"] =
                "送金元は {sourceAccount} です。振込内容を入力してください。",
            [ViewKeys.TransferModal + ".title"] = "振込内容の入力",
            [ViewKeys.TransferModal + ".description"] = "振込先と金額を入力してください。",
            [ViewKeys.TransferConfirm + ".title"] = "振込内容の確認",
            [ViewKeys.TransferConfirm + ".description"] =
                "内容を確認して、振込を実行してください。実行するまで引落は行いません。",
            [ViewKeys.FieldLabel(ViewKeys.TransferConfirm, ViewKeys.FieldSource)] = "送金元",
            [ViewKeys.FieldValue(ViewKeys.TransferConfirm, ViewKeys.FieldSource)] = "{sourceAccount}",
            [ViewKeys.FieldLabel(ViewKeys.TransferConfirm, ViewKeys.FieldBank)] = "振込先銀行",
            [ViewKeys.FieldValue(ViewKeys.TransferConfirm, ViewKeys.FieldBank)] = "{institutionCode}",
            [ViewKeys.FieldLabel(ViewKeys.TransferConfirm, ViewKeys.FieldBranch)] = "振込先支店",
            [ViewKeys.FieldValue(ViewKeys.TransferConfirm, ViewKeys.FieldBranch)] = "{branchCode}",
            [ViewKeys.FieldLabel(ViewKeys.TransferConfirm, ViewKeys.FieldAccount)] = "振込先口座",
            [ViewKeys.FieldValue(ViewKeys.TransferConfirm, ViewKeys.FieldAccount)] = "{accountNumberSuffix}",
            [ViewKeys.FieldLabel(ViewKeys.TransferConfirm, ViewKeys.FieldAmount)] = "振込金額",
            [ViewKeys.FieldValue(ViewKeys.TransferConfirm, ViewKeys.FieldAmount)] = "{amount}",
            [ViewKeys.FieldLabel(ViewKeys.TransferConfirm, ViewKeys.FieldFee)] = "手数料",
            [ViewKeys.FieldValue(ViewKeys.TransferConfirm, ViewKeys.FieldFee)] = "実行時に確定します",
            [ViewKeys.FieldLabel(ViewKeys.TransferConfirm, ViewKeys.FieldTotal)] = "合計引落額",
            [ViewKeys.FieldValue(ViewKeys.TransferConfirm, ViewKeys.FieldTotal)] = "実行時に確定します",
            [ViewKeys.TransferSourcePlaceholder] = "送金元口座を選んでください",
            [ViewKeys.TransferInputLabel] = "振込内容を入力",
            [ViewKeys.TransferExecuteLabel] = "振込を実行",
            [ViewKeys.StatusOf("ACTIVE")] = "利用可能",
            [ViewKeys.StatusOf("OPERATING")] = "利用可能",
            [ViewKeys.StatusOf("PENDING")] = "処理待ち",
            [ViewKeys.StatusOf("QUEUED")] = "処理待ち",
            [ViewKeys.StatusOf("PENDING_ACTIVATION")] = "処理待ち",
            [ViewKeys.StatusOf("RESTRICTED")] = "制限中",
            [ViewKeys.StatusOf("FROZEN")] = "凍結中",
            [ViewKeys.StatusOf("SUSPENDED")] = "凍結中",
            [ViewKeys.StatusOf("DORMANT")] = "休眠中",
            [ViewKeys.StatusOf("REOPENING")] = "再開手続中",
            [ViewKeys.StatusOf("SETTLEMENT_SUSPENDED")] = "決済停止中",
            [ViewKeys.StatusOf("CLOSING")] = "解約手続中",
            [ViewKeys.StatusOf("CLOSED")] = "終了",
            [ViewKeys.StatusOf("CLOSED_USER")] = "終了",
            [ViewKeys.StatusOf("CLOSED_DORMANCY")] = "終了",
            [ViewKeys.StatusOf("CLOSED_RESOLUTION")] = "終了",
            [ViewKeys.StatusOf("UNLINKED")] = "連携解除済み",
            [ViewKeys.StatusOf("SETTLED")] = "完了",
            [ViewKeys.StatusOf("COMPLETED")] = "完了",
            [ViewKeys.StatusOf("FAILED")] = "失敗",
            [ViewKeys.StatusOf("RESOLUTION")] = "破綻処理中",
            [ViewKeys.StatusUnknown] = "状態を表示できません",
        };
}
