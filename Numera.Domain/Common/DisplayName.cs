namespace Numera.Domain.Common;

public readonly struct DisplayName : IEquatable<DisplayName>
{
    public const int MinimumLength = 1;
    public const int MaximumLength = 64;

    private readonly string value;

    private DisplayName(string value) => this.value = value;

    public string Value => value ?? string.Empty;

    public static bool TryParse(ReadOnlySpan<char> candidate, out DisplayName displayName)
    {
        if (!BoundedTextValidator.TryNormalize(candidate, MinimumLength, MaximumLength, out string normalized))
        {
            displayName = default;
            return false;
        }

        displayName = new DisplayName(normalized);
        return true;
    }

    public static DisplayName Parse(ReadOnlySpan<char> candidate) =>
        TryParse(candidate, out DisplayName displayName)
            ? displayName
            : throw InvariantViolationException.Create(InvariantViolationCode.DisplayNameInvalid);

    public bool Equals(DisplayName other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DisplayName other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(DisplayName left, DisplayName right) => left.Equals(right);

    public static bool operator !=(DisplayName left, DisplayName right) => !left.Equals(right);
}
