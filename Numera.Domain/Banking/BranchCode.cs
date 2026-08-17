namespace Numera.Domain.Banking;

public readonly struct BranchCode : IEquatable<BranchCode>
{
    public const int MinimumLength = 3;
    public const int MaximumLength = 8;

    private readonly string value;

    private BranchCode(string value) => this.value = value;

    public string Value => value ?? string.Empty;

    public static bool IsValid(ReadOnlySpan<char> candidate) =>
        AsciiDigitCode.IsValid(candidate, MinimumLength, MaximumLength);

    public static bool TryParse(ReadOnlySpan<char> candidate, out BranchCode code)
    {
        if (!IsValid(candidate))
        {
            code = default;
            return false;
        }

        code = new BranchCode(candidate.ToString());
        return true;
    }

    public static BranchCode Parse(ReadOnlySpan<char> candidate) =>
        TryParse(candidate, out BranchCode code)
            ? code
            : throw InvariantViolationException.Create(InvariantViolationCode.BranchCodeInvalid);

    public bool Equals(BranchCode other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is BranchCode other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(BranchCode left, BranchCode right) => left.Equals(right);

    public static bool operator !=(BranchCode left, BranchCode right) => !left.Equals(right);
}
