namespace Numera.Domain.Banking;

public readonly struct InstitutionCode : IEquatable<InstitutionCode>
{
    public const int MinimumLength = 4;
    public const int MaximumLength = 16;

    private readonly string value;

    private InstitutionCode(string value) => this.value = value;

    public string Value => value ?? string.Empty;

    public static bool IsValid(ReadOnlySpan<char> candidate)
    {
        if (candidate.Length is < MinimumLength or > MaximumLength)
        {
            return false;
        }

        foreach (char character in candidate)
        {
            if (character is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9')))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryParse(ReadOnlySpan<char> candidate, out InstitutionCode code)
    {
        if (!IsValid(candidate))
        {
            code = default;
            return false;
        }

        code = new InstitutionCode(candidate.ToString());
        return true;
    }

    public static InstitutionCode Parse(ReadOnlySpan<char> candidate) =>
        TryParse(candidate, out InstitutionCode code)
            ? code
            : throw InvariantViolationException.Create(InvariantViolationCode.InstitutionCodeInvalid);

    public bool Equals(InstitutionCode other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is InstitutionCode other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(InstitutionCode left, InstitutionCode right) => left.Equals(right);

    public static bool operator !=(InstitutionCode left, InstitutionCode right) => !left.Equals(right);
}
