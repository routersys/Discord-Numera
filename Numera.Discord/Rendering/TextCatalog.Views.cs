namespace Numera.Discord.Rendering;

public static class ViewKeys
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
    public const string BankList = "view.bank.list";
    public const string BankListEmpty = "view.bank.list_empty";
    public const string BankAccountList = "view.bank.accounts";
    public const string BankAccountListEmpty = "view.bank.accounts_empty";
    public const string Statement = "view.bank.statement";
    public const string StatementEmpty = "view.bank.statement_empty";

    public const string StatusPrefix = "status.";

    public const string StatusUnknown = "status.unknown";

    public static string StatusOf(string internalToken) => StatusPrefix + internalToken;
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
