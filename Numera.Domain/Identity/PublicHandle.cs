namespace Numera.Domain.Identity;

public readonly struct PublicHandle : IEquatable<PublicHandle>
{
    public const int MinimumLength = 3;
    public const int MaximumLength = 32;

    private readonly string value;

    private PublicHandle(string value) => this.value = value;

    public string Value => value ?? string.Empty;

    public static bool IsValid(ReadOnlySpan<char> candidate)
    {
        if (candidate.Length is < MinimumLength or > MaximumLength)
        {
            return false;
        }

        if (candidate[0] is < 'a' or > 'z')
        {
            return false;
        }

        if (candidate[^1] == '_')
        {
            return false;
        }

        char previous = '\0';
        foreach (char character in candidate)
        {
            bool permitted = character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_';
            if (!permitted)
            {
                return false;
            }

            if (character == '_' && previous == '_')
            {
                return false;
            }

            previous = character;
        }

        return true;
    }

    public static bool TryParse(ReadOnlySpan<char> candidate, out PublicHandle handle)
    {
        if (!IsValid(candidate))
        {
            handle = default;
            return false;
        }

        handle = new PublicHandle(candidate.ToString());
        return true;
    }

    public static PublicHandle Parse(ReadOnlySpan<char> candidate) =>
        TryParse(candidate, out PublicHandle handle)
            ? handle
            : throw InvariantViolationException.Create(InvariantViolationCode.PublicHandleInvalid);

    public bool Equals(PublicHandle other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is PublicHandle other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(PublicHandle left, PublicHandle right) => left.Equals(right);

    public static bool operator !=(PublicHandle left, PublicHandle right) => !left.Equals(right);
}
