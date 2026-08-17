namespace Numera.Discord.Generators;

internal static class CommandNameRules
{
    internal const int MinimumNameLength = 1;
    internal const int MaximumNameLength = 32;
    internal const int MinimumDescriptionLength = 1;
    internal const int MaximumDescriptionLength = 100;
    internal const int MaximumOptionCount = 25;
    internal const int MaximumChoiceCount = 25;
    internal const int MaximumCustomIdLength = 100;
    internal const int MaximumGroupDepth = 2;
    internal const int MaximumChatInputCommands = 100;
    internal const int MaximumContextCommands = 15;
    internal const int MaximumModalTitleLength = 45;
    internal const int MaximumModalLabelLength = 45;
    internal const int MaximumModalPlaceholderLength = 100;

    internal static bool IsLengthValid(string? name) =>
        name is not null && name.Length >= MinimumNameLength && name.Length <= MaximumNameLength;

    internal static bool IsDescriptionLengthValid(string? description) =>
        description is not null
        && description.Length >= MinimumDescriptionLength
        && description.Length <= MaximumDescriptionLength;

    internal static bool IsNameFormatValid(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name![0] == '-' || name[name.Length - 1] == '-')
        {
            return false;
        }

        foreach (char character in name)
        {
            bool permitted = (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '-'
                || character == '_';

            if (!permitted)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool ContainsEmoji(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (int index = 0; index < text!.Length; index++)
        {
            char character = text[index];

            if (char.IsHighSurrogate(character) && index + 1 < text.Length)
            {
                int codePoint = char.ConvertToUtf32(character, text[index + 1]);
                if (IsEmojiCodePoint(codePoint))
                {
                    return true;
                }

                index++;
                continue;
            }

            if (IsEmojiCodePoint(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEmojiCodePoint(int codePoint) =>
        (codePoint >= 0x1F300 && codePoint <= 0x1FAFF)
        || (codePoint >= 0x1F000 && codePoint <= 0x1F2FF)
        || (codePoint >= 0x2600 && codePoint <= 0x27BF)
        || (codePoint >= 0x2190 && codePoint <= 0x21FF)
        || (codePoint >= 0x2B00 && codePoint <= 0x2BFF)
        || (codePoint >= 0xFE00 && codePoint <= 0xFE0F)
        || codePoint == 0x203C
        || codePoint == 0x2049
        || codePoint == 0x20E3;
}
