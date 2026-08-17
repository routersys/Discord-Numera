namespace Numera.Domain.Banking;

public enum FeeType
{
    AccountOpening = 1,
    AccountMaintenance = 2,
    SameBankTransfer = 3,
    InterbankTransfer = 4,
    CashCardIssue = 5,
    CashCardReplace = 6,
    DebitCardIssue = 7,
    DebitCardReplace = 8,
    DebitPurchase = 9,
    AtmOwnWithdrawal = 10,
    AtmPartnerWithdrawal = 11,
    AtmOwnDeposit = 12,
    AtmPartnerDeposit = 13,
    AtmBalanceInquiry = 14,
    AtmTransferChannel = 15,
    AtmPlacement = 16,
    DormancyWeekly = 17,
    AccountClose = 18,
}

public enum FeeChannel
{
    Any = 1,
    Discord = 2,
    Atm = 3,
    Scheduled = 4,
    DirectDebit = 5,
    Merchant = 6,
    Fx = 7,
    System = 8,
}

public enum FeeRuleDayClass
{
    Any = 1,
    BusinessDay = 2,
    NonBusinessDay = 3,
}

public static class FeeCatalog
{
    public static string ToToken(this FeeType feeType) => feeType switch
    {
        FeeType.AccountOpening => "ACCOUNT_OPENING",
        FeeType.AccountMaintenance => "ACCOUNT_MAINTENANCE",
        FeeType.SameBankTransfer => "SAME_BANK_TRANSFER",
        FeeType.InterbankTransfer => "INTERBANK_TRANSFER",
        FeeType.CashCardIssue => "CASH_CARD_ISSUE",
        FeeType.CashCardReplace => "CASH_CARD_REPLACE",
        FeeType.DebitCardIssue => "DEBIT_CARD_ISSUE",
        FeeType.DebitCardReplace => "DEBIT_CARD_REPLACE",
        FeeType.DebitPurchase => "DEBIT_PURCHASE",
        FeeType.AtmOwnWithdrawal => "ATM_OWN_WITHDRAWAL",
        FeeType.AtmPartnerWithdrawal => "ATM_PARTNER_WITHDRAWAL",
        FeeType.AtmOwnDeposit => "ATM_OWN_DEPOSIT",
        FeeType.AtmPartnerDeposit => "ATM_PARTNER_DEPOSIT",
        FeeType.AtmBalanceInquiry => "ATM_BALANCE_INQUIRY",
        FeeType.AtmTransferChannel => "ATM_TRANSFER_CHANNEL",
        FeeType.AtmPlacement => "ATM_PLACEMENT",
        FeeType.DormancyWeekly => "DORMANCY_WEEKLY",
        FeeType.AccountClose => "ACCOUNT_CLOSE",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.FeeTypeUnknown),
    };

    public static bool TryParseFeeTypeToken(ReadOnlySpan<char> token, out FeeType feeType)
    {
        switch (token)
        {
            case "ACCOUNT_OPENING":
                feeType = FeeType.AccountOpening;
                return true;
            case "ACCOUNT_MAINTENANCE":
                feeType = FeeType.AccountMaintenance;
                return true;
            case "SAME_BANK_TRANSFER":
                feeType = FeeType.SameBankTransfer;
                return true;
            case "INTERBANK_TRANSFER":
                feeType = FeeType.InterbankTransfer;
                return true;
            case "CASH_CARD_ISSUE":
                feeType = FeeType.CashCardIssue;
                return true;
            case "CASH_CARD_REPLACE":
                feeType = FeeType.CashCardReplace;
                return true;
            case "DEBIT_CARD_ISSUE":
                feeType = FeeType.DebitCardIssue;
                return true;
            case "DEBIT_CARD_REPLACE":
                feeType = FeeType.DebitCardReplace;
                return true;
            case "DEBIT_PURCHASE":
                feeType = FeeType.DebitPurchase;
                return true;
            case "ATM_OWN_WITHDRAWAL":
                feeType = FeeType.AtmOwnWithdrawal;
                return true;
            case "ATM_PARTNER_WITHDRAWAL":
                feeType = FeeType.AtmPartnerWithdrawal;
                return true;
            case "ATM_OWN_DEPOSIT":
                feeType = FeeType.AtmOwnDeposit;
                return true;
            case "ATM_PARTNER_DEPOSIT":
                feeType = FeeType.AtmPartnerDeposit;
                return true;
            case "ATM_BALANCE_INQUIRY":
                feeType = FeeType.AtmBalanceInquiry;
                return true;
            case "ATM_TRANSFER_CHANNEL":
                feeType = FeeType.AtmTransferChannel;
                return true;
            case "ATM_PLACEMENT":
                feeType = FeeType.AtmPlacement;
                return true;
            case "DORMANCY_WEEKLY":
                feeType = FeeType.DormancyWeekly;
                return true;
            case "ACCOUNT_CLOSE":
                feeType = FeeType.AccountClose;
                return true;
            default:
                feeType = default;
                return false;
        }
    }

    public static FeeType ParseFeeTypeToken(ReadOnlySpan<char> token) =>
        TryParseFeeTypeToken(token, out FeeType feeType)
            ? feeType
            : throw InvariantViolationException.Create(InvariantViolationCode.FeeTypeUnknown);

    public static string ToToken(this FeeChannel channel) => channel switch
    {
        FeeChannel.Any => "ANY",
        FeeChannel.Discord => "DISCORD",
        FeeChannel.Atm => "ATM",
        FeeChannel.Scheduled => "SCHEDULED",
        FeeChannel.DirectDebit => "DIRECT_DEBIT",
        FeeChannel.Merchant => "MERCHANT",
        FeeChannel.Fx => "FX",
        FeeChannel.System => "SYSTEM",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.FeeChannelUnknown),
    };

    public static bool TryParseChannelToken(ReadOnlySpan<char> token, out FeeChannel channel)
    {
        switch (token)
        {
            case "ANY":
                channel = FeeChannel.Any;
                return true;
            case "DISCORD":
                channel = FeeChannel.Discord;
                return true;
            case "ATM":
                channel = FeeChannel.Atm;
                return true;
            case "SCHEDULED":
                channel = FeeChannel.Scheduled;
                return true;
            case "DIRECT_DEBIT":
                channel = FeeChannel.DirectDebit;
                return true;
            case "MERCHANT":
                channel = FeeChannel.Merchant;
                return true;
            case "FX":
                channel = FeeChannel.Fx;
                return true;
            case "SYSTEM":
                channel = FeeChannel.System;
                return true;
            default:
                channel = default;
                return false;
        }
    }

    public static FeeChannel ParseChannelToken(ReadOnlySpan<char> token) =>
        TryParseChannelToken(token, out FeeChannel channel)
            ? channel
            : throw InvariantViolationException.Create(InvariantViolationCode.FeeChannelUnknown);

    public static string ToToken(this FeeRuleDayClass dayClass) => dayClass switch
    {
        FeeRuleDayClass.Any => "ANY",
        FeeRuleDayClass.BusinessDay => "BUSINESS_DAY",
        FeeRuleDayClass.NonBusinessDay => "NON_BUSINESS_DAY",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.FeeRuleDayClassUnknown),
    };

    public static bool TryParseDayClassToken(ReadOnlySpan<char> token, out FeeRuleDayClass dayClass)
    {
        switch (token)
        {
            case "ANY":
                dayClass = FeeRuleDayClass.Any;
                return true;
            case "BUSINESS_DAY":
                dayClass = FeeRuleDayClass.BusinessDay;
                return true;
            case "NON_BUSINESS_DAY":
                dayClass = FeeRuleDayClass.NonBusinessDay;
                return true;
            default:
                dayClass = default;
                return false;
        }
    }

    public static FeeRuleDayClass ParseDayClassToken(ReadOnlySpan<char> token) =>
        TryParseDayClassToken(token, out FeeRuleDayClass dayClass)
            ? dayClass
            : throw InvariantViolationException.Create(InvariantViolationCode.FeeRuleDayClassUnknown);

    public static bool Accepts(this FeeRuleDayClass ruleDayClass, BusinessDayClass dayClass) => ruleDayClass switch
    {
        FeeRuleDayClass.Any => true,
        FeeRuleDayClass.BusinessDay => dayClass == BusinessDayClass.BusinessDay,
        FeeRuleDayClass.NonBusinessDay => dayClass == BusinessDayClass.NonBusinessDay,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.FeeRuleDayClassUnknown),
    };
}
