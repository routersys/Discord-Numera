using System.Buffers;
using System.Globalization;
using System.Text;

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
        displayName = default;
        ReadOnlySpan<char> trimmed = candidate.Trim();
        if (trimmed.IsEmpty)
        {
            return false;
        }

        int codePoints = 0;
        for (int index = 0; index < trimmed.Length;)
        {
            if (Rune.DecodeFromUtf16(trimmed[index..], out Rune rune, out int consumed) != OperationStatus.Done)
            {
                return false;
            }

            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                return false;
            }

            codePoints++;
            if (codePoints > MaximumLength)
            {
                return false;
            }

            index += consumed;
        }

        if (codePoints < MinimumLength)
        {
            return false;
        }

        displayName = new DisplayName(trimmed.ToString());
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
