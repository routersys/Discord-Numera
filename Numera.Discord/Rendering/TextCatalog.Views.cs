using Numera.Discord.Sessions;

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
    public const string ManageBankCreateModal = "view.manage.bank_create.modal";
    public const string ManageBankCreateReview = "view.manage.bank_create.review";
    public const string ManageBankCreateInputLabel = "view.manage.bank_create.input_label";
    public const string ManageBankCreateCommitLabel = "view.manage.bank_create.commit_label";
    public const string ManageBankCapitalPrompt = "view.manage.bank_capital.prompt";
    public const string ManageBankCapitalModal = "view.manage.bank_capital.modal";
    public const string ManageBankCapitalReview = "view.manage.bank_capital.review";
    public const string ManageBankCapitalContributed = "view.manage.bank_capital.contributed";
    public const string ManageBankActivated = "view.manage.bank_activated";
    public const string ManageBankCapitalInputLabel = "view.manage.bank_capital.input_label";
    public const string ManageBankCapitalCommitLabel = "view.manage.bank_capital.commit_label";
    public const string ManageBankActivateLabel = "view.manage.bank_capital.activate_label";
    public const string ManageBankCapitalIssuerLabel = "view.manage.bank_capital.issuer_label";
    public const string ManageBankCapitalShortfall = "view.manage.bank_capital.shortfall";
    public const string BankDetail = "view.bank.detail";
    public const string BankDetailPlaceholder = "label.bank.detail_select";
    public const string BankLoanModal = "view.bank.loan_modal";
    public const string BankLoanReview = "view.bank.loan_review";
    public const string BankLoanOriginated = "view.bank.loan_originated";
    public const string BankLoanInputLabel = "label.bank.loan_input";
    public const string BankLoanCommitLabel = "label.bank.loan_commit";
    public const string FieldLoanPrincipal = "loan_principal";
    public const string FieldLoanProduct = "loan_product";
    public const string FieldCapitalAmount = "capital_amount";
    public const string FieldCapitalSource = "capital_source";
    public const string FieldCapitalMinimum = "capital_minimum";
    public const string FieldInstitution = "institution";
    public const string FieldBankName = "bank_name";
    public const string FieldProduct = "product";
    public const string FieldOpeningPolicy = "opening_policy";

    public const string ManagePanelPlaceholder = "view.manage.panel.placeholder";
    public const string ManagePanelCategory = "view.manage.panel.category";
    public const string ManagePanelActionPlaceholder = "view.manage.panel.action.placeholder";
    public const string ManagePanelRoute = "view.manage.panel.route";
    public const string ManagePanelPending = "view.manage.panel.pending";
    public const string ManagePanelEditor = "view.manage.panel.editor";
    public const string ManagePanelEditLabel = "view.manage.panel.edit_label";
    public const string ManagePanelReview = "view.manage.panel.review";
    public const string ManagePanelCommitLabel = "view.manage.panel.commit_label";
    public const string ManagePanelApplied = "view.manage.panel.applied";
    public const string PanelFieldCurrent = "view.manage.panel.field.current";
    public const string PanelFieldAfter = "view.manage.panel.field.after";
    public const string PanelValueCurrent = "view.manage.panel.value.current";
    public const string PanelValueAfter = "view.manage.panel.value.after";
    public const string PanelCurrentUnavailable = "view.manage.panel.current.unavailable";
    public const string PanelCurrentDefaultSuffix = "view.manage.panel.current.default";
    public const string PanelFundExists = "view.manage.panel.fund.exists";
    public const string PanelGrantActive = "view.manage.panel.grant.active";
    public const string PanelGrantAbsent = "view.manage.panel.grant.absent";

    public static string PanelEditorModal(string action) =>
        "view.manage.panel.modal." + action;

    public static string PanelCategoryLabel(string category) =>
        "view.manage.panel.category." + category;

    public static string PanelActionLabel(string category, string action) =>
        "view.manage.panel.category." + category + "." + action;

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
    public const string ManageCurrencyEditor = "view.manage.currency_editor";
    public const string ManageCurrencyRetireReview = "view.manage.currency_retire_review";
    public const string ManageBankAsset = "view.manage.bank_asset";
    public const string SystemReconcile = "view.system.reconcile";

    public const string FxMarket = "view.fx.market";
    public const string FxRate = "view.fx.rate";
    public const string FxBoard = "view.fx.board";
    public const string FxBoardEmpty = "view.fx.board_empty";
    public const string FxChart = "view.fx.chart";
    public const string FxChartEmpty = "view.fx.chart_empty";

    public const string FxChartStart = "view.fx.chart.start";

    public const string FxChartEnd = "view.fx.chart.end";

    public const string FxChartHigh = "view.fx.chart.high";

    public const string FxChartLow = "view.fx.chart.low";

    public const string FxChartChange = "view.fx.chart.change";

    public const string FxChartVolume = "view.fx.chart.volume";

    public const string FxChartPeriodPlaceholder = "view.fx.chart.period_placeholder";

    public const string FxChartPeriodHour = "view.fx.chart.period_hour";

    public const string FxChartPeriodDay = "view.fx.chart.period_day";

    public const string FxChartPeriodWeek = "view.fx.chart.period_week";

    public const string FxChartPeriodMonth = "view.fx.chart.period_month";

    public const string FxChartToLine = "view.fx.chart.to_line";

    public const string FxChartToCandle = "view.fx.chart.to_candle";

    public const string FxChartToLight = "view.fx.chart.to_light";

    public const string FxChartToDark = "view.fx.chart.to_dark";
    public const string FxOrders = "view.fx.orders";
    public const string FxOrdersEmpty = "view.fx.orders_empty";
    public const string FxHistory = "view.fx.history";
    public const string FxHistoryEmpty = "view.fx.history_empty";
    public const string FxOrderCancelled = "view.fx.order_cancelled";

    public const string FxOrderPlaced = "view.fx.order_placed";
    public const string FxOrderUnfilled = "view.fx.order_unfilled";

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
                "{institutionCode} を {status} で作成しました。名称は {bankName} です。"
                + "営業開始には {minimum} 以上の払込資本が要ります。",
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
            [ViewKeys.ManageBankCreateModal + ".title"] = "銀行の基本情報",
            [ViewKeys.ManageBankCreateModal + ".description"] = "銀行名と本店と預金商品を入力します。",
            [ViewKeys.ManageBankCreateInputLabel] = "基本情報を入力",
            [ViewKeys.ManageBankCreateCommitLabel] = "この内容で設立",
            [ViewKeys.ManageBankCreateReview + ".title"] = "設立内容の確認",
            [ViewKeys.ManageBankCreateReview + ".description"] =
                "内容を確認して設立を確定してください。口座開設方針は設立後に /manage bank-edit で変更できます。",
            [ViewKeys.FieldLabel(ViewKeys.ManageBankCreateReview, ViewKeys.FieldInstitution)] = "金融機関コード",
            [ViewKeys.FieldValue(ViewKeys.ManageBankCreateReview, ViewKeys.FieldInstitution)] = "{institutionCode}",
            [ViewKeys.FieldLabel(ViewKeys.ManageBankCreateReview, ViewKeys.FieldBankName)] = "銀行名",
            [ViewKeys.FieldValue(ViewKeys.ManageBankCreateReview, ViewKeys.FieldBankName)] = "{bankName}",
            [ViewKeys.FieldLabel(ViewKeys.ManageBankCreateReview, ViewKeys.FieldBranch)] = "本店",
            [ViewKeys.FieldValue(ViewKeys.ManageBankCreateReview, ViewKeys.FieldBranch)] = "{branchCode} {branchName}",
            [ViewKeys.FieldLabel(ViewKeys.ManageBankCreateReview, ViewKeys.FieldProduct)] = "預金商品",
            [ViewKeys.FieldValue(ViewKeys.ManageBankCreateReview, ViewKeys.FieldProduct)] = "{productCode} {productName}",
            [ViewKeys.FieldLabel(ViewKeys.ManageBankCreateReview, ViewKeys.FieldOpeningPolicy)] = "口座開設",
            [ViewKeys.FieldValue(ViewKeys.ManageBankCreateReview, ViewKeys.FieldOpeningPolicy)] = "受付する",
            [ViewKeys.ManageBankCapitalPrompt + ".title"] = "資本の払込",
            [ViewKeys.ManageBankCapitalPrompt + ".description"] =
                "{institutionCode} は {status} です。営業開始には {minimum} 以上の払込資本が要ります。"
                + "現在の払込資本は {paidIn} です。",
            [ViewKeys.ManageBankCapitalModal + ".title"] = "資本払込の入力",
            [ViewKeys.ManageBankCapitalModal + ".description"] =
                "払込額を最小単位で入力します。払込元を空欄にすると中央銀行が払い込みます。",
            [ViewKeys.ManageBankCapitalInputLabel] = "払込内容を入力",
            [ViewKeys.ManageBankCapitalCommitLabel] = "この内容で払い込む",
            [ViewKeys.ManageBankActivateLabel] = "営業を開始する",
            [ViewKeys.ManageBankCapitalIssuerLabel] = "中央銀行",
            [ViewKeys.BankDetail + ".title"] = "{bankName}",
            [ViewKeys.BankDetail + ".description"] =
                "{institutionCode} は {status} です。預金商品は {products} です。"
                + "融資商品は {loanProducts} です。",
            [ViewKeys.BankDetailPlaceholder] = "銀行を選択",
            [ViewKeys.BankLoanModal + ".title"] = "融資の申込",
            [ViewKeys.BankLoanModal + ".description"] = "借入額と商品コードを入力します。",
            [ViewKeys.BankLoanInputLabel] = "融資を申し込む",
            [ViewKeys.BankLoanCommitLabel] = "この内容で申し込む",
            [ViewKeys.BankLoanReview + ".title"] = "融資内容の確認",
            [ViewKeys.BankLoanReview + ".description"] =
                "内容を確認して申込を確定してください。実行されると借入額が口座へ入金されます。",
            [ViewKeys.FieldLabel(ViewKeys.BankLoanReview, ViewKeys.FieldInstitution)] = "金融機関コード",
            [ViewKeys.FieldValue(ViewKeys.BankLoanReview, ViewKeys.FieldInstitution)] = "{institutionCode}",
            [ViewKeys.FieldLabel(ViewKeys.BankLoanReview, ViewKeys.FieldLoanPrincipal)] = "借入額",
            [ViewKeys.FieldValue(ViewKeys.BankLoanReview, ViewKeys.FieldLoanPrincipal)] = "{principal}",
            [ViewKeys.FieldLabel(ViewKeys.BankLoanReview, ViewKeys.FieldLoanProduct)] = "商品コード",
            [ViewKeys.FieldValue(ViewKeys.BankLoanReview, ViewKeys.FieldLoanProduct)] = "{productCode}",
            [ViewKeys.BankLoanOriginated + ".title"] = "融資を実行しました",
            [ViewKeys.BankLoanOriginated + ".description"] =
                "{institutionCode} から {principal} を借り入れました。契約は {status} です。",
            [ViewKeys.ManageBankCapitalReview + ".title"] = "払込内容の確認",
            [ViewKeys.ManageBankCapitalReview + ".description"] =
                "内容を確認して払込を確定してください。払込は取り消せません。",
            [ViewKeys.FieldLabel(ViewKeys.ManageBankCapitalReview, ViewKeys.FieldInstitution)] = "金融機関コード",
            [ViewKeys.FieldValue(ViewKeys.ManageBankCapitalReview, ViewKeys.FieldInstitution)] =
                "{institutionCode}",
            [ViewKeys.FieldLabel(ViewKeys.ManageBankCapitalReview, ViewKeys.FieldCapitalAmount)] = "払込額",
            [ViewKeys.FieldValue(ViewKeys.ManageBankCapitalReview, ViewKeys.FieldCapitalAmount)] = "{amount}",
            [ViewKeys.FieldLabel(ViewKeys.ManageBankCapitalReview, ViewKeys.FieldCapitalSource)] = "払込元",
            [ViewKeys.FieldValue(ViewKeys.ManageBankCapitalReview, ViewKeys.FieldCapitalSource)] = "{source}",
            [ViewKeys.ManageBankCapitalShortfall + ".title"] = "払込資本が不足しています",
            [ViewKeys.ManageBankCapitalShortfall + ".description"] =
                "{institutionCode} へ {amount} を払い込みました。払込資本は {paidIn} で最低額は {minimum} です。"
                + "不足分を払い込むと営業を開始できます。",
            [ViewKeys.ManageBankCapitalContributed + ".title"] = "資本を払い込みました",
            [ViewKeys.ManageBankCapitalContributed + ".description"] =
                "{institutionCode} へ {amount} を払い込みました。払込資本は {paidIn} で最低額は {minimum} です。",
            [ViewKeys.ManageBankActivated + ".title"] = "銀行の営業を開始しました",
            [ViewKeys.ManageBankActivated + ".description"] =
                "{institutionCode} を {status} にしました。名称は {bankName} です。",
            [ViewKeys.ManagePanelPlaceholder] = "管理項目を選択",
            [ViewKeys.ManagePanelCategory + ".title"] = "{category}",
            [ViewKeys.ManagePanelCategory + ".description"] = "操作を選択してください。",
            [ViewKeys.ManagePanelActionPlaceholder] = "操作を選択",
            [ViewKeys.ManagePanelRoute + ".title"] = "{action}",
            [ViewKeys.ManagePanelRoute + ".description"] = "{category} の {action} は {route} から実行します。",
            [ViewKeys.ManagePanelPending + ".title"] = "{action}",
            [ViewKeys.ManagePanelPending + ".description"] = "{category} の {action} は編集画面を実装していません。",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.EconomyCalendar)] = "経済・営業日",
            [ViewKeys.PanelActionLabel("economy-calendar", "calendar-set")] = "営業日区分を設定",
            [ViewKeys.PanelActionLabel("economy-calendar", "calendar-clear")] = "営業日区分の上書きを解除",
            [ViewKeys.ManagePanelEditor + ".title"] = "{action}",
            [ViewKeys.ManagePanelEditor + ".description"] = "{category} の {action} を編集します。入力してください。",
            [ViewKeys.ManagePanelEditLabel] = "入力する",
            [ViewKeys.ManagePanelReview + ".title"] = "{action}",
            [ViewKeys.ManagePanelReview + ".description"] = "{category} の {action} を確定します。現在値と変更後を確認してください。",
            [ViewKeys.ManagePanelCommitLabel] = "確定する",
            [ViewKeys.ManagePanelApplied + ".title"] = "{action}",
            [ViewKeys.ManagePanelApplied + ".description"] = "{category} の {action} を {after} で適用しました。",
            [ViewKeys.PanelFieldCurrent] = "現在値",
            [ViewKeys.PanelFieldAfter] = "変更後",
            [ViewKeys.PanelValueCurrent] = "{current}",
            [ViewKeys.PanelValueAfter] = "{after}",
            [ViewKeys.PanelCurrentUnavailable] = "なし",
            [ViewKeys.PanelCurrentDefaultSuffix] = "（既定）",
            [ViewKeys.PanelFundExists] = "作成済み",
            [ViewKeys.PanelGrantActive] = "権限あり",
            [ViewKeys.PanelGrantAbsent] = "権限なし",
            [ViewKeys.PanelActionLabel("deposit-insurance", "insurance-fund")] = "保険基金の作成",
            [ViewKeys.PanelActionLabel("prudential-resolution", "resolution-case")] = "破綻処理の手続",
            [ViewKeys.PanelEditorModal("insurance-fund")] = "保険基金の作成",
            [ViewKeys.PanelEditorModal("resolution-case")] = "破綻処理の手続",
            [ViewKeys.PanelEditorModal("calendar-set")] = "営業日区分を設定",
            [ViewKeys.PanelEditorModal("calendar-clear")] = "営業日区分の上書きを解除",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.CurrencyIssuance)] = "通貨・発行",
            [ViewKeys.PanelActionLabel("currency-issuance", "currency-create")] = "通貨の作成",
            [ViewKeys.PanelActionLabel("currency-issuance", "currency-issue")] = "追加発行",
            [ViewKeys.PanelActionLabel("currency-issuance", "currency-burn")] = "償却",
            [ViewKeys.PanelActionLabel("currency-issuance", "currency-edit")] = "通貨情報の変更",
            [ViewKeys.PanelActionLabel("currency-issuance", "currency-retire")] = "通貨の廃止",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.CurrencyTrust)] = "通貨信頼性",
            [ViewKeys.PanelActionLabel("currency-trust", "trust-policy")] = "信頼性基準の公開",
            [ViewKeys.PanelEditorModal("trust-policy")] = "信頼性基準の公開",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.BankBranch)] = "銀行・支店",
            [ViewKeys.PanelActionLabel("bank-branch", "bank-create")] = "銀行の設立",
            [ViewKeys.PanelActionLabel("bank-branch", "bank-edit")] = "銀行方針の更新",
            [ViewKeys.PanelActionLabel("bank-branch", "bank-retire")] = "銀行の廃止",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.BankOperator)] = "銀行運営権限",
            [ViewKeys.PanelActionLabel("bank-operator", "operator-grant")] = "運営者権限の付与と取消",
            [ViewKeys.PanelEditorModal("operator-grant")] = "運営者権限の付与と取消",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.DepositProduct)] = "預金商品・口座開設",
            [ViewKeys.PanelActionLabel("deposit-product", "account-review")] = "口座開設の審査",
            [ViewKeys.PanelEditorModal("account-review")] = "口座開設の審査",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.FeeLimitDormancy)] = "手数料・取引上限・休眠",
            [ViewKeys.PanelActionLabel("fee-limit-dormancy", "fee-schedule")] = "手数料規則の公開",
            [ViewKeys.PanelEditorModal("fee-schedule")] = "手数料規則の公開",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.CardDesign)] = "カード・銀行デザイン",
            [ViewKeys.PanelActionLabel("card-design", "bank-design")] = "銀行の配色",
            [ViewKeys.PanelEditorModal("bank-design")] = "銀行の配色",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.AtmCash)] = "ATM・現金・設置メッセージ",
            [ViewKeys.PanelActionLabel("atm-cash", "atm-network")] = "ATM網の作成と状態",
            [ViewKeys.PanelActionLabel("atm-cash", "atm-terminal")] = "ATM端末の設置",
            [ViewKeys.PanelActionLabel("atm-cash", "atm-service")] = "端末の通貨サービス",
            [ViewKeys.PanelActionLabel("atm-cash", "atm-cassette")] = "現金カセットの構成",
            [ViewKeys.PanelActionLabel("atm-cash", "cash-denomination")] = "金種の追加",
            [ViewKeys.PanelActionLabel("atm-cash", "cash-conversion")] = "現金と準備預金の交換",
            [ViewKeys.PanelEditorModal("atm-network")] = "ATM網の作成と状態",
            [ViewKeys.PanelEditorModal("atm-terminal")] = "ATM端末の設置",
            [ViewKeys.PanelEditorModal("atm-service")] = "端末の通貨サービス",
            [ViewKeys.PanelEditorModal("atm-cassette")] = "現金カセットの構成",
            [ViewKeys.PanelEditorModal("cash-denomination")] = "金種の追加",
            [ViewKeys.PanelEditorModal("cash-conversion")] = "現金と準備預金の交換",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.MerchantCommerce)] = "加盟店・商品・定期決済",
            [ViewKeys.PanelActionLabel("merchant-commerce", "merchant-product")] = "商品の登録",
            [ViewKeys.PanelActionLabel("merchant-commerce", "merchant-price")] = "商品価格の公開",
            [ViewKeys.PanelActionLabel("merchant-commerce", "merchant-stock")] = "在庫と商品状態",
            [ViewKeys.PanelEditorModal("merchant-product")] = "商品の登録",
            [ViewKeys.PanelEditorModal("merchant-price")] = "商品価格の公開",
            [ViewKeys.PanelEditorModal("merchant-stock")] = "在庫と商品状態",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.PaymentNetwork)] = "決済参加・Payment Network",
            [ViewKeys.PanelActionLabel("payment-network", "network-policy")] = "決済網方針の公開",
            [ViewKeys.PanelActionLabel("payment-network", "network-state")] = "決済網の停止と再開",
            [ViewKeys.PanelEditorModal("network-policy")] = "決済網方針の公開",
            [ViewKeys.PanelEditorModal("network-state")] = "決済網の停止と再開",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.FxMarket)] = "為替市場",
            [ViewKeys.PanelActionLabel("fx-market", "fx-market")] = "為替市場の管理",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.CentralBank)] = "中央銀行・為替介入",
            [ViewKeys.PanelActionLabel("central-bank", "reserve-position")] = "準備資産の管理",
            [ViewKeys.PanelActionLabel("central-bank", "intervention")] = "為替介入権限の発行",
            [ViewKeys.PanelEditorModal("intervention")] = "為替介入権限の発行",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.DepositInsurance)] = "預金保険",
            [ViewKeys.PanelActionLabel("deposit-insurance", "insurance-scheme")] = "保護区分の公開",
            [ViewKeys.PanelActionLabel("deposit-insurance", "insurance-state")] = "保護区分の状態変更",
            [ViewKeys.PanelEditorModal("insurance-scheme")] = "保護区分の公開",
            [ViewKeys.PanelEditorModal("insurance-state")] = "保護区分の状態変更",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.PrudentialResolution)] = "健全性・Resolution",
            [ViewKeys.PanelActionLabel("prudential-resolution", "prudential-policy")] = "健全性基準の公開",
            [ViewKeys.PanelEditorModal("prudential-policy")] = "健全性基準の公開",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.Presentation)] = "表示設定",
            [ViewKeys.PanelActionLabel("presentation", "presentation-profile")] = "表示配色の公開",
            [ViewKeys.PanelEditorModal("presentation-profile")] = "表示配色の公開",
            [ViewKeys.PanelCategoryLabel(ManagementPanelCatalog.Audit)] = "監査",
            [ViewKeys.PanelActionLabel("audit", "reconcile")] = "整合性検査",
            [ViewKeys.ManagePanel + ".title"] = "管理メニュー",
            [ViewKeys.ManagePanel + ".description"] = "管理する項目を選択してください。",
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
            [ViewKeys.StatusOf("BUSINESS_DAY")] = "営業日",
            [ViewKeys.StatusOf("NON_BUSINESS_DAY")] = "非営業日",
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
            [ViewKeys.ManageCurrencyEditor + ".title"] = "通貨の編集を開始しました",
            [ViewKeys.ManageCurrencyEditor + ".description"] =
                "{code} の編集を開始しました。手順は {steps} です。確定前に現在値と変更後を確認します。",
            [ViewKeys.ManageCurrencyRetireReview + ".title"] = "通貨の廃止手続を開始しました",
            [ViewKeys.ManageCurrencyRetireReview + ".description"] =
                "{code} の廃止手続を開始しました。手順は {steps} です。公開済みの通貨は削除せず廃止状態へ移します。",
            [ViewKeys.ManageBankAsset + ".title"] = "銀行画像の登録を開始しました",
            [ViewKeys.ManageBankAsset + ".description"] =
                "{institutionCode} の{kind}を登録します。手順は {steps} です。",
            [ViewKeys.SystemReconcile + ".title"] = "整合性検査を開始しました",
            [ViewKeys.SystemReconcile + ".description"] =
                "検査範囲は {scope} です。手順は {steps} です。",
            [ViewKeys.StatusOf("PUBLIC_LOGO")] = "公開ロゴ",
            [ViewKeys.StatusOf("PUBLIC_BANNER")] = "公開バナー",
            [ViewKeys.StatusOf("ATM_BANNER")] = "ATMバナー",
            [ViewKeys.StatusOf("CARD_BACKGROUND")] = "カード背景",
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
            [ViewKeys.FxChart + ".description"] = "{pair}の{period}です。{count}本の足があり変化率は{change}です。",
            [ViewKeys.FxChartStart] = "始値",
            [ViewKeys.FxChartEnd] = "終値",
            [ViewKeys.FxChartHigh] = "高値",
            [ViewKeys.FxChartLow] = "安値",
            [ViewKeys.FxChartChange] = "変化率",
            [ViewKeys.FxChartVolume] = "出来高",
            [ViewKeys.FxChartPeriodPlaceholder] = "期間を選びます",
            [ViewKeys.FxChartPeriodHour] = "1時間",
            [ViewKeys.FxChartPeriodDay] = "24時間",
            [ViewKeys.FxChartPeriodWeek] = "7日",
            [ViewKeys.FxChartPeriodMonth] = "30日",
            [ViewKeys.FxChartToLine] = "折れ線",
            [ViewKeys.FxChartToCandle] = "ローソク足",
            [ViewKeys.FxChartToLight] = "ライト",
            [ViewKeys.FxChartToDark] = "ダーク",
            [ViewKeys.FxChartEmpty + ".title"] = "チャートがありません",
            [ViewKeys.FxChartEmpty + ".description"] = "{pair}の{period}には完了した足がありません。期間を変えてください。",
            [ViewKeys.FxOrders + ".title"] = "為替注文の一覧",
            [ViewKeys.FxOrders + ".description"] = "{count}件の注文があります。{items}",
            [ViewKeys.FxOrdersEmpty + ".title"] = "注文がありません",
            [ViewKeys.FxOrdersEmpty + ".description"] = "まだ為替注文を出していません。",
            [ViewKeys.FxHistory + ".title"] = "約定履歴",
            [ViewKeys.FxHistory + ".description"] = "{count}件の約定があります。",
            [ViewKeys.FxHistoryEmpty + ".title"] = "約定がありません",
            [ViewKeys.FxHistoryEmpty + ".description"] = "この市場にはまだ約定がありません。",
            [ViewKeys.FxOrderPlaced + ".title"] = "注文を受け付けました",
            [ViewKeys.FxOrderPlaced + ".description"] =
                "状態は{status}で、約定数量は{filled}、残数量は{remaining}です。",
            [ViewKeys.FxOrderUnfilled + ".title"] = "注文は約定しませんでした",
            [ViewKeys.FxOrderUnfilled + ".description"] =
                "状態は{status}で、約定数量は0です。"
                + "対当する他の参加者の注文が板に無いか、最良気配が自分の注文のため交差できません。"
                + "自分の注文とは約定しない規則です。板は /fx board、自分の注文は /fx orders で確認できます。",
            [ViewKeys.FxOrderCancelled + ".title"] = "注文を取り消しました",
            [ViewKeys.FxOrderCancelled + ".description"] = "注文の状態は{status}です。",
            [ViewKeys.StatusUnknown] = "状態を表示できません",
        };
}
