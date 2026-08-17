namespace Numera.Domain.Common;

internal static class AsciiDigitCode
{
    internal static bool IsValid(ReadOnlySpan<char> candidate, int minimumLength, int maximumLength)
    {
        if (candidate.Length < minimumLength || candidate.Length > maximumLength)
        {
            return false;
        }

        foreach (char character in candidate)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
