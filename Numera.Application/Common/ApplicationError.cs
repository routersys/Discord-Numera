namespace Numera.Application.Common;

public enum ErrorCategory
{
    Validation = 1,
    NotFound = 2,
    Forbidden = 3,
    Conflict = 4,
    InsufficientFunds = 5,
    BankUnavailable = 6,
    AccountRestricted = 7,
    OperationExpired = 8,
    ConcurrencyConflict = 9,
    InfrastructureUnavailable = 10,
    Unexpected = 11,
}

public sealed class ApplicationError
{
    private ApplicationError(ErrorCategory category, string code, string? field)
    {
        Category = category;
        Code = code;
        Field = field;
    }

    public ErrorCategory Category { get; }

    public string Code { get; }

    public string? Field { get; }

    public static ApplicationError Create(ErrorCategory category, string code, string? field = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (!ErrorCodeFormat.IsValid(code, category))
        {
            throw new ArgumentException(ErrorCodeFormat.InvalidCodeMessage, nameof(code));
        }

        return new ApplicationError(category, code, field);
    }
}

public static class ErrorCategoryCatalog
{
    public static string ToToken(this ErrorCategory category) => category switch
    {
        ErrorCategory.Validation => "VAL",
        ErrorCategory.NotFound => "NOTFOUND",
        ErrorCategory.Forbidden => "FORBIDDEN",
        ErrorCategory.Conflict => "CONFLICT",
        ErrorCategory.InsufficientFunds => "FUNDS",
        ErrorCategory.BankUnavailable => "BANK",
        ErrorCategory.AccountRestricted => "ACCOUNT",
        ErrorCategory.OperationExpired => "EXPIRED",
        ErrorCategory.ConcurrencyConflict => "CONCURRENCY",
        ErrorCategory.InfrastructureUnavailable => "INFRA",
        ErrorCategory.Unexpected => "UNEXPECTED",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };
}

public static class ErrorCodeFormat
{
    public const string Prefix = "BANK-";
    public const int SequenceDigits = 3;
    internal const string InvalidCodeMessage = "error_code の形式が正準形式と一致しません。";

    public static string Compose(ErrorCategory category, int sequence)
    {
        if (sequence is < 1 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        return $"{Prefix}{category.ToToken()}-{sequence:D3}";
    }

    public static bool IsValid(ReadOnlySpan<char> code, ErrorCategory category)
    {
        ReadOnlySpan<char> categoryToken = category.ToToken();
        int expectedLength = Prefix.Length + categoryToken.Length + 1 + SequenceDigits;

        if (code.Length != expectedLength || !code.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> remainder = code[Prefix.Length..];
        if (!remainder.StartsWith(categoryToken, StringComparison.Ordinal) ||
            remainder[categoryToken.Length] != '-')
        {
            return false;
        }

        foreach (char character in remainder[(categoryToken.Length + 1)..])
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
