namespace Numera.Domain.Banking;

public readonly struct BankName : IEquatable<BankName>
{
    public const int MinimumLength = 1;
    public const int MaximumLength = 80;

    private readonly string value;

    private BankName(string value) => this.value = value;

    public string Value => value ?? string.Empty;

    public static bool TryParse(ReadOnlySpan<char> candidate, out BankName name)
    {
        if (!BoundedTextValidator.TryNormalize(candidate, MinimumLength, MaximumLength, out string normalized))
        {
            name = default;
            return false;
        }

        name = new BankName(normalized);
        return true;
    }

    public static BankName Parse(ReadOnlySpan<char> candidate) =>
        TryParse(candidate, out BankName name)
            ? name
            : throw InvariantViolationException.Create(InvariantViolationCode.BankNameInvalid);

    public bool Equals(BankName other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is BankName other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(BankName left, BankName right) => left.Equals(right);

    public static bool operator !=(BankName left, BankName right) => !left.Equals(right);
}
