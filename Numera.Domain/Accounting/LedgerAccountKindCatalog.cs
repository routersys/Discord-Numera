namespace Numera.Domain.Accounting;

public static class LedgerAccountKindCatalog
{
    public static string ToToken(this LedgerAccountKind kind) => kind switch
    {
        LedgerAccountKind.CashAsset => "CASH_ASSET",
        LedgerAccountKind.CentralBankReserveAsset => "CENTRAL_BANK_RESERVE_ASSET",
        LedgerAccountKind.SettlementAgentBalanceAsset => "SETTLEMENT_AGENT_BALANCE_ASSET",
        LedgerAccountKind.ClearingReceivable => "CLEARING_RECEIVABLE",
        LedgerAccountKind.FxClearingReceivable => "FX_CLEARING_RECEIVABLE",
        LedgerAccountKind.AtmNetworkReceivable => "ATM_NETWORK_RECEIVABLE",
        LedgerAccountKind.FxCashDeliveryReceivable => "FX_CASH_DELIVERY_RECEIVABLE",
        LedgerAccountKind.InterbankLoanReceivable => "INTERBANK_LOAN_RECEIVABLE",
        LedgerAccountKind.CustomerLoanPrincipal => "CUSTOMER_LOAN_PRINCIPAL",
        LedgerAccountKind.AccruedInterestReceivable => "ACCRUED_INTEREST_RECEIVABLE",
        LedgerAccountKind.SecurityAsset => "SECURITY_ASSET",
        LedgerAccountKind.FeeReceivable => "FEE_RECEIVABLE",
        LedgerAccountKind.SuspenseAsset => "SUSPENSE_ASSET",
        LedgerAccountKind.CreditLossAllowance => "CREDIT_LOSS_ALLOWANCE",
        LedgerAccountKind.DemandDepositControl => "DEMAND_DEPOSIT_CONTROL",
        LedgerAccountKind.TimeDepositControl => "TIME_DEPOSIT_CONTROL",
        LedgerAccountKind.ClearingPayable => "CLEARING_PAYABLE",
        LedgerAccountKind.FxClearingPayable => "FX_CLEARING_PAYABLE",
        LedgerAccountKind.AtmNetworkPayable => "ATM_NETWORK_PAYABLE",
        LedgerAccountKind.AtmCashDeliveryPayable => "ATM_CASH_DELIVERY_PAYABLE",
        LedgerAccountKind.PlacementFeePayable => "PLACEMENT_FEE_PAYABLE",
        LedgerAccountKind.SettlementPayable => "SETTLEMENT_PAYABLE",
        LedgerAccountKind.IncomingSettlementSuspense => "INCOMING_SETTLEMENT_SUSPENSE",
        LedgerAccountKind.InterbankBorrowing => "INTERBANK_BORROWING",
        LedgerAccountKind.ClientBankSettlementDeposit => "CLIENT_BANK_SETTLEMENT_DEPOSIT",
        LedgerAccountKind.CentralBankSettlementLiability => "CENTRAL_BANK_SETTLEMENT_LIABILITY",
        LedgerAccountKind.CentralBankIntradayBorrowing => "CENTRAL_BANK_INTRADAY_BORROWING",
        LedgerAccountKind.CentralBankOvernightBorrowing => "CENTRAL_BANK_OVERNIGHT_BORROWING",
        LedgerAccountKind.CashOutstandingLiability => "CASH_OUTSTANDING_LIABILITY",
        LedgerAccountKind.BaseMoneyIssuanceLiability => "BASE_MONEY_ISSUANCE_LIABILITY",
        LedgerAccountKind.AccruedInterestPayable => "ACCRUED_INTEREST_PAYABLE",
        LedgerAccountKind.FeeTaxPayable => "FEE_TAX_PAYABLE",
        LedgerAccountKind.SuspenseLiability => "SUSPENSE_LIABILITY",
        LedgerAccountKind.PaidInCapital => "PAID_IN_CAPITAL",
        LedgerAccountKind.CapitalSurplus => "CAPITAL_SURPLUS",
        LedgerAccountKind.RetainedEarnings => "RETAINED_EARNINGS",
        LedgerAccountKind.LoanInterestRevenue => "LOAN_INTEREST_REVENUE",
        LedgerAccountKind.SecurityInterestRevenue => "SECURITY_INTEREST_REVENUE",
        LedgerAccountKind.InterbankInterestRevenue => "INTERBANK_INTEREST_REVENUE",
        LedgerAccountKind.FeeRevenue => "FEE_REVENUE",
        LedgerAccountKind.OtherRevenue => "OTHER_REVENUE",
        LedgerAccountKind.DepositInterestExpense => "DEPOSIT_INTEREST_EXPENSE",
        LedgerAccountKind.InterbankInterestExpense => "INTERBANK_INTEREST_EXPENSE",
        LedgerAccountKind.CentralBankInterestExpense => "CENTRAL_BANK_INTEREST_EXPENSE",
        LedgerAccountKind.ClearingFeeExpense => "CLEARING_FEE_EXPENSE",
        LedgerAccountKind.CreditLossExpense => "CREDIT_LOSS_EXPENSE",
        LedgerAccountKind.OperatingExpense => "OPERATING_EXPENSE",
        LedgerAccountKind.ResolutionLossExpense => "RESOLUTION_LOSS_EXPENSE",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.LedgerAccountKindUnknown),
    };

    public static AccountingType ToAccountingType(this LedgerAccountKind kind) => kind switch
    {
        LedgerAccountKind.CashAsset => AccountingType.Asset,
        LedgerAccountKind.CentralBankReserveAsset => AccountingType.Asset,
        LedgerAccountKind.SettlementAgentBalanceAsset => AccountingType.Asset,
        LedgerAccountKind.ClearingReceivable => AccountingType.Asset,
        LedgerAccountKind.FxClearingReceivable => AccountingType.Asset,
        LedgerAccountKind.AtmNetworkReceivable => AccountingType.Asset,
        LedgerAccountKind.FxCashDeliveryReceivable => AccountingType.Asset,
        LedgerAccountKind.InterbankLoanReceivable => AccountingType.Asset,
        LedgerAccountKind.CustomerLoanPrincipal => AccountingType.Asset,
        LedgerAccountKind.AccruedInterestReceivable => AccountingType.Asset,
        LedgerAccountKind.SecurityAsset => AccountingType.Asset,
        LedgerAccountKind.FeeReceivable => AccountingType.Asset,
        LedgerAccountKind.SuspenseAsset => AccountingType.Asset,
        LedgerAccountKind.CreditLossAllowance => AccountingType.ContraAsset,
        LedgerAccountKind.DemandDepositControl => AccountingType.Liability,
        LedgerAccountKind.TimeDepositControl => AccountingType.Liability,
        LedgerAccountKind.ClearingPayable => AccountingType.Liability,
        LedgerAccountKind.FxClearingPayable => AccountingType.Liability,
        LedgerAccountKind.AtmNetworkPayable => AccountingType.Liability,
        LedgerAccountKind.AtmCashDeliveryPayable => AccountingType.Liability,
        LedgerAccountKind.PlacementFeePayable => AccountingType.Liability,
        LedgerAccountKind.SettlementPayable => AccountingType.Liability,
        LedgerAccountKind.IncomingSettlementSuspense => AccountingType.Liability,
        LedgerAccountKind.InterbankBorrowing => AccountingType.Liability,
        LedgerAccountKind.ClientBankSettlementDeposit => AccountingType.Liability,
        LedgerAccountKind.CentralBankSettlementLiability => AccountingType.Liability,
        LedgerAccountKind.CentralBankIntradayBorrowing => AccountingType.Liability,
        LedgerAccountKind.CentralBankOvernightBorrowing => AccountingType.Liability,
        LedgerAccountKind.CashOutstandingLiability => AccountingType.Liability,
        LedgerAccountKind.BaseMoneyIssuanceLiability => AccountingType.Liability,
        LedgerAccountKind.AccruedInterestPayable => AccountingType.Liability,
        LedgerAccountKind.FeeTaxPayable => AccountingType.Liability,
        LedgerAccountKind.SuspenseLiability => AccountingType.Liability,
        LedgerAccountKind.PaidInCapital => AccountingType.Equity,
        LedgerAccountKind.CapitalSurplus => AccountingType.Equity,
        LedgerAccountKind.RetainedEarnings => AccountingType.Equity,
        LedgerAccountKind.LoanInterestRevenue => AccountingType.Revenue,
        LedgerAccountKind.SecurityInterestRevenue => AccountingType.Revenue,
        LedgerAccountKind.InterbankInterestRevenue => AccountingType.Revenue,
        LedgerAccountKind.FeeRevenue => AccountingType.Revenue,
        LedgerAccountKind.OtherRevenue => AccountingType.Revenue,
        LedgerAccountKind.DepositInterestExpense => AccountingType.Expense,
        LedgerAccountKind.InterbankInterestExpense => AccountingType.Expense,
        LedgerAccountKind.CentralBankInterestExpense => AccountingType.Expense,
        LedgerAccountKind.ClearingFeeExpense => AccountingType.Expense,
        LedgerAccountKind.CreditLossExpense => AccountingType.Expense,
        LedgerAccountKind.OperatingExpense => AccountingType.Expense,
        LedgerAccountKind.ResolutionLossExpense => AccountingType.Expense,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.LedgerAccountKindUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out LedgerAccountKind kind)
    {
        switch (token)
        {
            case "CASH_ASSET":
                kind = LedgerAccountKind.CashAsset;
                return true;
            case "CENTRAL_BANK_RESERVE_ASSET":
                kind = LedgerAccountKind.CentralBankReserveAsset;
                return true;
            case "SETTLEMENT_AGENT_BALANCE_ASSET":
                kind = LedgerAccountKind.SettlementAgentBalanceAsset;
                return true;
            case "CLEARING_RECEIVABLE":
                kind = LedgerAccountKind.ClearingReceivable;
                return true;
            case "FX_CLEARING_RECEIVABLE":
                kind = LedgerAccountKind.FxClearingReceivable;
                return true;
            case "ATM_NETWORK_RECEIVABLE":
                kind = LedgerAccountKind.AtmNetworkReceivable;
                return true;
            case "FX_CASH_DELIVERY_RECEIVABLE":
                kind = LedgerAccountKind.FxCashDeliveryReceivable;
                return true;
            case "INTERBANK_LOAN_RECEIVABLE":
                kind = LedgerAccountKind.InterbankLoanReceivable;
                return true;
            case "CUSTOMER_LOAN_PRINCIPAL":
                kind = LedgerAccountKind.CustomerLoanPrincipal;
                return true;
            case "ACCRUED_INTEREST_RECEIVABLE":
                kind = LedgerAccountKind.AccruedInterestReceivable;
                return true;
            case "SECURITY_ASSET":
                kind = LedgerAccountKind.SecurityAsset;
                return true;
            case "FEE_RECEIVABLE":
                kind = LedgerAccountKind.FeeReceivable;
                return true;
            case "SUSPENSE_ASSET":
                kind = LedgerAccountKind.SuspenseAsset;
                return true;
            case "CREDIT_LOSS_ALLOWANCE":
                kind = LedgerAccountKind.CreditLossAllowance;
                return true;
            case "DEMAND_DEPOSIT_CONTROL":
                kind = LedgerAccountKind.DemandDepositControl;
                return true;
            case "TIME_DEPOSIT_CONTROL":
                kind = LedgerAccountKind.TimeDepositControl;
                return true;
            case "CLEARING_PAYABLE":
                kind = LedgerAccountKind.ClearingPayable;
                return true;
            case "FX_CLEARING_PAYABLE":
                kind = LedgerAccountKind.FxClearingPayable;
                return true;
            case "ATM_NETWORK_PAYABLE":
                kind = LedgerAccountKind.AtmNetworkPayable;
                return true;
            case "ATM_CASH_DELIVERY_PAYABLE":
                kind = LedgerAccountKind.AtmCashDeliveryPayable;
                return true;
            case "PLACEMENT_FEE_PAYABLE":
                kind = LedgerAccountKind.PlacementFeePayable;
                return true;
            case "SETTLEMENT_PAYABLE":
                kind = LedgerAccountKind.SettlementPayable;
                return true;
            case "INCOMING_SETTLEMENT_SUSPENSE":
                kind = LedgerAccountKind.IncomingSettlementSuspense;
                return true;
            case "INTERBANK_BORROWING":
                kind = LedgerAccountKind.InterbankBorrowing;
                return true;
            case "CLIENT_BANK_SETTLEMENT_DEPOSIT":
                kind = LedgerAccountKind.ClientBankSettlementDeposit;
                return true;
            case "CENTRAL_BANK_SETTLEMENT_LIABILITY":
                kind = LedgerAccountKind.CentralBankSettlementLiability;
                return true;
            case "CENTRAL_BANK_INTRADAY_BORROWING":
                kind = LedgerAccountKind.CentralBankIntradayBorrowing;
                return true;
            case "CENTRAL_BANK_OVERNIGHT_BORROWING":
                kind = LedgerAccountKind.CentralBankOvernightBorrowing;
                return true;
            case "CASH_OUTSTANDING_LIABILITY":
                kind = LedgerAccountKind.CashOutstandingLiability;
                return true;
            case "BASE_MONEY_ISSUANCE_LIABILITY":
                kind = LedgerAccountKind.BaseMoneyIssuanceLiability;
                return true;
            case "ACCRUED_INTEREST_PAYABLE":
                kind = LedgerAccountKind.AccruedInterestPayable;
                return true;
            case "FEE_TAX_PAYABLE":
                kind = LedgerAccountKind.FeeTaxPayable;
                return true;
            case "SUSPENSE_LIABILITY":
                kind = LedgerAccountKind.SuspenseLiability;
                return true;
            case "PAID_IN_CAPITAL":
                kind = LedgerAccountKind.PaidInCapital;
                return true;
            case "CAPITAL_SURPLUS":
                kind = LedgerAccountKind.CapitalSurplus;
                return true;
            case "RETAINED_EARNINGS":
                kind = LedgerAccountKind.RetainedEarnings;
                return true;
            case "LOAN_INTEREST_REVENUE":
                kind = LedgerAccountKind.LoanInterestRevenue;
                return true;
            case "SECURITY_INTEREST_REVENUE":
                kind = LedgerAccountKind.SecurityInterestRevenue;
                return true;
            case "INTERBANK_INTEREST_REVENUE":
                kind = LedgerAccountKind.InterbankInterestRevenue;
                return true;
            case "FEE_REVENUE":
                kind = LedgerAccountKind.FeeRevenue;
                return true;
            case "OTHER_REVENUE":
                kind = LedgerAccountKind.OtherRevenue;
                return true;
            case "DEPOSIT_INTEREST_EXPENSE":
                kind = LedgerAccountKind.DepositInterestExpense;
                return true;
            case "INTERBANK_INTEREST_EXPENSE":
                kind = LedgerAccountKind.InterbankInterestExpense;
                return true;
            case "CENTRAL_BANK_INTEREST_EXPENSE":
                kind = LedgerAccountKind.CentralBankInterestExpense;
                return true;
            case "CLEARING_FEE_EXPENSE":
                kind = LedgerAccountKind.ClearingFeeExpense;
                return true;
            case "CREDIT_LOSS_EXPENSE":
                kind = LedgerAccountKind.CreditLossExpense;
                return true;
            case "OPERATING_EXPENSE":
                kind = LedgerAccountKind.OperatingExpense;
                return true;
            case "RESOLUTION_LOSS_EXPENSE":
                kind = LedgerAccountKind.ResolutionLossExpense;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static LedgerAccountKind ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out LedgerAccountKind kind)
            ? kind
            : throw InvariantViolationException.Create(InvariantViolationCode.LedgerAccountKindUnknown);
}
