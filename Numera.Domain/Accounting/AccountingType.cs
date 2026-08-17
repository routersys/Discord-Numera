namespace Numera.Domain.Accounting;

public enum AccountingType
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Revenue = 4,
    Expense = 5,
    ContraAsset = 6,
}

public enum EntrySide
{
    Debit = 1,
    Credit = 2,
}

public static class AccountingTypeCatalog
{
    public static string ToToken(this AccountingType type) => type switch
    {
        AccountingType.Asset => "ASSET",
        AccountingType.Liability => "LIABILITY",
        AccountingType.Equity => "EQUITY",
        AccountingType.Revenue => "REVENUE",
        AccountingType.Expense => "EXPENSE",
        AccountingType.ContraAsset => "CONTRA_ASSET",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.AccountingTypeUnknown),
    };

    public static EntrySide NormalSide(this AccountingType type) => type switch
    {
        AccountingType.Asset => EntrySide.Debit,
        AccountingType.Expense => EntrySide.Debit,
        AccountingType.Liability => EntrySide.Credit,
        AccountingType.Equity => EntrySide.Credit,
        AccountingType.Revenue => EntrySide.Credit,
        AccountingType.ContraAsset => EntrySide.Credit,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.AccountingTypeUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AccountingType type)
    {
        switch (token)
        {
            case "ASSET":
                type = AccountingType.Asset;
                return true;
            case "LIABILITY":
                type = AccountingType.Liability;
                return true;
            case "EQUITY":
                type = AccountingType.Equity;
                return true;
            case "REVENUE":
                type = AccountingType.Revenue;
                return true;
            case "EXPENSE":
                type = AccountingType.Expense;
                return true;
            case "CONTRA_ASSET":
                type = AccountingType.ContraAsset;
                return true;
            default:
                type = default;
                return false;
        }
    }

    public static AccountingType ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AccountingType type)
            ? type
            : throw InvariantViolationException.Create(InvariantViolationCode.AccountingTypeUnknown);
}

public static class EntrySideCatalog
{
    public static string ToToken(this EntrySide side) => side switch
    {
        EntrySide.Debit => "DEBIT",
        EntrySide.Credit => "CREDIT",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.EntrySideUnknown),
    };

    public static EntrySide Opposite(this EntrySide side) => side switch
    {
        EntrySide.Debit => EntrySide.Credit,
        EntrySide.Credit => EntrySide.Debit,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.EntrySideUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out EntrySide side)
    {
        switch (token)
        {
            case "DEBIT":
                side = EntrySide.Debit;
                return true;
            case "CREDIT":
                side = EntrySide.Credit;
                return true;
            default:
                side = default;
                return false;
        }
    }

    public static EntrySide ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out EntrySide side)
            ? side
            : throw InvariantViolationException.Create(InvariantViolationCode.EntrySideUnknown);
}
