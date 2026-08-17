namespace Numera.Domain.Banking;

public readonly struct CustomerNumber : IEquatable<CustomerNumber>
{
    public const int MinimumLength = 6;
    public const int MaximumLength = 16;

    private readonly string value;

    private CustomerNumber(string value) => this.value = value;

    public string Value => value ?? string.Empty;

    public static bool IsValid(ReadOnlySpan<char> candidate) =>
        AsciiDigitCode.IsValid(candidate, MinimumLength, MaximumLength);

    public static bool TryParse(ReadOnlySpan<char> candidate, out CustomerNumber code)
    {
        if (!IsValid(candidate))
        {
            code = default;
            return false;
        }

        code = new CustomerNumber(candidate.ToString());
        return true;
    }

    public static CustomerNumber Parse(ReadOnlySpan<char> candidate) =>
        TryParse(candidate, out CustomerNumber code)
            ? code
            : throw InvariantViolationException.Create(InvariantViolationCode.CustomerNumberInvalid);

    public bool Equals(CustomerNumber other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is CustomerNumber other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(CustomerNumber left, CustomerNumber right) => left.Equals(right);

    public static bool operator !=(CustomerNumber left, CustomerNumber right) => !left.Equals(right);
}
