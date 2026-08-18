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

    public const string BankCard = "view.bank.card";

    public const string PaymentsBeneficiaries = "view.bank.payments_beneficiaries";
    public const string PaymentsBeneficiariesEmpty = "view.bank.payments_beneficiaries_empty";
    public const string PaymentsScheduled = "view.bank.payments_scheduled";
    public const string PaymentsScheduledEmpty = "view.bank.payments_scheduled_empty";
    public const string PaymentsMandates = "view.bank.payments_mandates";
    public const string PaymentsMandatesEmpty = "view.bank.payments_mandates_empty";

    public const string ManageBankDraft = "view.manage.bank_draft";
    public const string ManageBankUpdated = "view.manage.bank_updated";
    public const string ManageBankRetired = "view.manage.bank_retired";
    public const string ManageFxMarketCreated = "view.manage.fx_market_created";
    public const string ManageFxMarketState = "view.manage.fx_market_state";
    public const string ManageFxMarketPolicyPublished = "view.manage.fx_market_policy_published";

    public const string FxMarket = "view.fx.market";
    public const string FxRate = "view.fx.rate";
    public const string FxBoard = "view.fx.board";
    public const string FxBoardEmpty = "view.fx.board_empty";
    public const string FxChart = "view.fx.chart";
    public const string FxChartEmpty = "view.fx.chart_empty";
    public const string FxOrders = "view.fx.orders";
    public const string FxOrdersEmpty = "view.fx.orders_empty";
    public const string FxHistory = "view.fx.history";
    public const string FxHistoryEmpty = "view.fx.history_empty";
    public const string FxOrderCancelled = "view.fx.order_cancelled";

    public const string ShopStores = "view.shop.stores";
    public const string ShopStoresEmpty = "view.shop.stores_empty";
    public const string ShopProducts = "view.shop.products";
    public const string ShopProductsEmpty = "view.shop.products_empty";
    public const string ShopOrders = "view.shop.orders";
    public const string ShopOrdersEmpty = "view.shop.orders_empty";
    public const string BankAtm = "view.bank.atm";

    public const string FxSidePrefix = "fx_side.";

    public static string FxSideOf(string internalToken) => FxSidePrefix + internalToken;

    public const string ScheduledPaymentKindPrefix = "scheduled_payment_kind.";

    public static string ScheduledPaymentKindOf(string internalToken) =>
        ScheduledPaymentKindPrefix + internalToken;

    public const string CardFormPrefix = "card_form.";

    public const string CardCapabilityAbsent = "card_capability.absent";

    public static string CardFormOf(string internalToken) => CardFormPrefix + internalToken;

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
            [ViewKeys.StatusOf("LOCKED")] = "利用停止中",
            [ViewKeys.StatusOf("REPLACED")] = "再発行済み",
            [ViewKeys.StatusOf("EXPIRED")] = "期限切れ",
            [ViewKeys.CardFormOf("CASH_ONLY")] = "キャッシュカード",
            [ViewKeys.CardFormOf("DEBIT_ONLY")] = "デビットカード",
            [ViewKeys.CardFormOf("INTEGRATED_CASH_DEBIT")] = "一体型カード",
            [ViewKeys.CardCapabilityAbsent] = "なし",
            [ViewKeys.BankCard + ".title"] = "銀行カード",
            [ViewKeys.BankCard + ".description"] =
                "{institutionCode} の{form}です。カード状態は{status}、キャッシュカード機能は{cashCard}、デビット機能は{debitCard}、識別番号は {displayIdentifier} です。",
            [ViewKeys.StatusOf("HIDDEN")] = "非表示",
            [ViewKeys.StatusOf("INVALID")] = "無効",
            [ViewKeys.StatusOf("PAUSED")] = "一時停止中",
            [ViewKeys.StatusOf("CANCELLED")] = "取消済み",
            [ViewKeys.StatusOf("EXECUTING")] = "実行中",
            [ViewKeys.StatusOf("SUCCEEDED")] = "完了",
            [ViewKeys.StatusOf("FAILED_FUNDS")] = "残高不足で失敗",
            [ViewKeys.StatusOf("FAILED_RESTRICTED")] = "制限により失敗",
            [ViewKeys.StatusOf("FAILED_DESTINATION")] = "送金先の事由で失敗",
            [ViewKeys.StatusOf("FAILED_MANDATE")] = "承認の事由で失敗",
            [ViewKeys.StatusOf("FAILED_ACCOUNT")] = "口座の事由で失敗",
            [ViewKeys.ScheduledPaymentKindOf("ONCE")] = "一回",
            [ViewKeys.ScheduledPaymentKindOf("WEEKLY")] = "毎週",
            [ViewKeys.ScheduledPaymentKindOf("MONTHLY")] = "毎月",
            [ViewKeys.PaymentsBeneficiaries + ".title"] = "登録した振込先",
            [ViewKeys.PaymentsBeneficiaries + ".description"] = "{count}件の振込先があります。{items}",
            [ViewKeys.PaymentsBeneficiariesEmpty + ".title"] = "振込先がありません",
            [ViewKeys.PaymentsBeneficiariesEmpty + ".description"] = "まだ振込先を登録していません。",
            [ViewKeys.PaymentsScheduled + ".title"] = "予約振込",
            [ViewKeys.PaymentsScheduled + ".description"] = "{count}件の予約振込があります。{items}",
            [ViewKeys.PaymentsScheduledEmpty + ".title"] = "予約振込がありません",
            [ViewKeys.PaymentsScheduledEmpty + ".description"] = "まだ予約振込を登録していません。",
            [ViewKeys.PaymentsMandates + ".title"] = "口座振替",
            [ViewKeys.PaymentsMandates + ".description"] = "{count}件の口座振替があります。{items}",
            [ViewKeys.PaymentsMandatesEmpty + ".title"] = "口座振替がありません",
            [ViewKeys.PaymentsMandatesEmpty + ".description"] = "まだ口座振替を承認していません。",
            [ViewKeys.ManageBankDraft + ".title"] = "銀行設立を開始しました",
            [ViewKeys.ManageBankDraft + ".description"] =
                "金融機関コード {institutionCode} で設立を開始しました。手順は {steps} です。",
            [ViewKeys.ManageBankUpdated + ".title"] = "銀行の方針を更新しました",
            [ViewKeys.ManageBankUpdated + ".description"] =
                "{institutionCode} {name} の方針を更新しました。状態は{status}です。",
            [ViewKeys.ManageFxMarketCreated + ".title"] = "為替市場を設置しました",
            [ViewKeys.ManageFxMarketCreated + ".description"] =
                "市場 {market} を設置しました。状態は{status}です。価格桁数 {priceScale}、呼値 {tickSize}、売買単位 {lotSize} です。",
            [ViewKeys.ManageFxMarketState + ".title"] = "為替市場の状態を変更しました",
            [ViewKeys.ManageFxMarketState + ".description"] =
                "市場 {market} の状態は{status}です。価格桁数 {priceScale}、呼値 {tickSize}、売買単位 {lotSize} です。",
            [ViewKeys.ManageFxMarketPolicyPublished + ".title"] = "為替市場の方針を公開しました",
            [ViewKeys.ManageFxMarketPolicyPublished + ".description"] =
                "メイカー {makerFeeBps}bps、テイカー {takerFeeBps}bps、許容スリッページ {maximumSlippageBps}bps です。版数は {version} です。",
            [ViewKeys.ManageBankRetired + ".title"] = "銀行の廃止手続を開始しました",
            [ViewKeys.ManageBankRetired + ".description"] =
                "{institutionCode} の廃止手続を開始しました。",
            [ViewKeys.StatusOf("PARTIALLY_FILLED")] = "一部約定",
            [ViewKeys.StatusOf("FILLED")] = "約定済み",
            [ViewKeys.StatusOf("OPEN")] = "板に出ている",
            [ViewKeys.StatusOf("REJECTED")] = "拒否",
            [ViewKeys.StatusOf("PENDING_APPROVAL")] = "承認待ち",
            [ViewKeys.StatusOf("RETIRED")] = "廃止済み",
            [ViewKeys.StatusOf("DRAFT")] = "下書き",
            [ViewKeys.StatusOf("PUBLISHED")] = "公開中",
            [ViewKeys.StatusOf("CLEARING")] = "清算中",
            [ViewKeys.StatusOf("INTERNAL_FINAL")] = "行内完了",
            [ViewKeys.FxSideOf("BUY_BASE")] = "買い",
            [ViewKeys.FxSideOf("SELL_BASE")] = "売り",
            [ViewKeys.ShopStores + ".title"] = "加盟店一覧",
            [ViewKeys.ShopStores + ".description"] = "{count}件の加盟店があります。{items}{cursor}",
            [ViewKeys.ShopStoresEmpty + ".title"] = "加盟店がありません",
            [ViewKeys.ShopStoresEmpty + ".description"] = "このサーバーで利用できる加盟店はまだありません。",
            [ViewKeys.ShopProducts + ".title"] = "商品一覧",
            [ViewKeys.ShopProducts + ".description"] = "{count}件の商品があります。{items}{cursor}",
            [ViewKeys.ShopProductsEmpty + ".title"] = "商品がありません",
            [ViewKeys.ShopProductsEmpty + ".description"] = "この加盟店には販売中の商品がありません。",
            [ViewKeys.ShopOrders + ".title"] = "注文履歴",
            [ViewKeys.ShopOrders + ".description"] = "{count}件の注文があります。{items}{cursor}",
            [ViewKeys.ShopOrdersEmpty + ".title"] = "注文がありません",
            [ViewKeys.ShopOrdersEmpty + ".description"] = "まだ注文はありません。",
            [ViewKeys.BankAtm + ".title"] = "ATM",
            [ViewKeys.BankAtm + ".description"] =
                "{terminal} は {status} です。取扱通貨は {currencies} 種類です。",
            [ViewKeys.FxMarket + ".title"] = "為替市場",
            [ViewKeys.FxMarket + ".description"] =
                "最良買気配は {bestBid}、最良売気配は {bestAsk} です。板の版数は {orderBookVersion} です。",
            [ViewKeys.FxRate + ".title"] = "為替レート",
            [ViewKeys.FxRate + ".description"] =
                "直近約定は {lastTrade}、買気配 {bestBid} 売気配 {bestAsk} スプレッド {spread} です。24時間の高値は {high}、安値は {low}、出来高は {volume} です。",
            [ViewKeys.FxBoard + ".title"] = "板情報",
            [ViewKeys.FxBoard + ".description"] = "買い板{bids}売り板{asks}",
            [ViewKeys.FxBoardEmpty + ".title"] = "板がありません",
            [ViewKeys.FxBoardEmpty + ".description"] = "この市場にはまだ注文がありません。",
            [ViewKeys.FxChart + ".title"] = "為替チャート",
            [ViewKeys.FxChart + ".description"] = "{interval}秒足で{count}本の足があります。",
            [ViewKeys.FxChartEmpty + ".title"] = "チャートがありません",
            [ViewKeys.FxChartEmpty + ".description"] = "この市場にはまだ約定がありません。",
            [ViewKeys.FxOrders + ".title"] = "為替注文の一覧",
            [ViewKeys.FxOrders + ".description"] = "{count}件の注文があります。{items}",
            [ViewKeys.FxOrdersEmpty + ".title"] = "注文がありません",
            [ViewKeys.FxOrdersEmpty + ".description"] = "まだ為替注文を出していません。",
            [ViewKeys.FxHistory + ".title"] = "約定履歴",
            [ViewKeys.FxHistory + ".description"] = "{count}件の約定があります。",
            [ViewKeys.FxHistoryEmpty + ".title"] = "約定がありません",
            [ViewKeys.FxHistoryEmpty + ".description"] = "この市場にはまだ約定がありません。",
            [ViewKeys.FxOrderCancelled + ".title"] = "注文を取り消しました",
            [ViewKeys.FxOrderCancelled + ".description"] = "注文の状態は{status}です。",
            [ViewKeys.StatusUnknown] = "状態を表示できません",
        };
}
