using System.Globalization;

namespace Numera.Domain.Identity;

public readonly struct DiscordUserId : IEquatable<DiscordUserId>
{
    public const int MaximumTextLength = 20;

    private DiscordUserId(ulong value) => Value = value;

    public ulong Value { get; }

    public static DiscordUserId FromUInt64(ulong value) =>
        value > 0
            ? new DiscordUserId(value)
            : throw InvariantViolationException.Create(InvariantViolationCode.DiscordUserIdInvalid);

    public static bool TryParse(ReadOnlySpan<char> candidate, out DiscordUserId userId)
    {
        userId = default;
        if (candidate.Length is 0 or > MaximumTextLength)
        {
            return false;
        }

        ulong accumulated = 0;
        foreach (char character in candidate)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }

            ulong digit = (ulong)(character - '0');
            if (accumulated > (ulong.MaxValue - digit) / 10)
            {
                return false;
            }

            accumulated = (accumulated * 10) + digit;
        }

        if (accumulated == 0)
        {
            return false;
        }

        userId = new DiscordUserId(accumulated);
        return true;
    }

    public static DiscordUserId Parse(ReadOnlySpan<char> candidate) =>
        TryParse(candidate, out DiscordUserId userId)
            ? userId
            : throw InvariantViolationException.Create(InvariantViolationCode.DiscordUserIdInvalid);

    public bool Equals(DiscordUserId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is DiscordUserId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public static bool operator ==(DiscordUserId left, DiscordUserId right) => left.Equals(right);

    public static bool operator !=(DiscordUserId left, DiscordUserId right) => !left.Equals(right);
}
