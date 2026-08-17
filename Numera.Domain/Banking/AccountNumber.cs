namespace Numera.Domain.Banking;

public readonly struct AccountNumber : IEquatable<AccountNumber>
{
    public const int MinimumLength = 6;
    public const int MaximumLength = 16;
    public const int SuffixLength = 4;

    private readonly string value;

    private AccountNumber(string value) => this.value = value;

    public string Value => value ?? string.Empty;

    public string Suffix => Value is { Length: >= SuffixLength } number
        ? number[^SuffixLength..]
        : Value;

    public static bool IsValid(ReadOnlySpan<char> candidate) =>
        AsciiDigitCode.IsValid(candidate, MinimumLength, MaximumLength);

    public static bool TryParse(ReadOnlySpan<char> candidate, out AccountNumber code)
    {
        if (!IsValid(candidate))
        {
            code = default;
            return false;
        }

        code = new AccountNumber(candidate.ToString());
        return true;
    }

    public static AccountNumber Parse(ReadOnlySpan<char> candidate) =>
        TryParse(candidate, out AccountNumber code)
            ? code
            : throw InvariantViolationException.Create(InvariantViolationCode.AccountNumberInvalid);

    public bool Equals(AccountNumber other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is AccountNumber other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AccountNumber left, AccountNumber right) => left.Equals(right);

    public static bool operator !=(AccountNumber left, AccountNumber right) => !left.Equals(right);
}
