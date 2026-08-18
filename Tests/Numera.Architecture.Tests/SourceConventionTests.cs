namespace Numera.Architecture.Tests;

[TestClass]
public sealed class SourceConventionTests
{
    private static readonly string[] DiscordCommandAttributes =
    [
        "SlashCommand",
        "UserCommand",
        "MessageCommand",
        "ComponentInteraction",
        "ModalInteraction",
        "AutocompleteCommand",
        "Group",
        "CommandContextType",
        "IntegrationType",
        "DefaultMemberPermissions",
    ];

    private static readonly string[] HandWrittenProjects =
    [
        "Numera.Domain",
        "Numera.Application",
        "Numera.Persistence.Sqlite",
        "Numera.Discord",
        "Numera.Discord.Abstractions",
        "Numera.Host",
    ];

    private const string TextCatalogFilePrefix = "TextCatalog";

    private const string EndpointDeclarationPrefix = "[Economy";

    [TestMethod]
    public void SourceScanReachesEveryHandWrittenProject()
    {
        foreach (string project in HandWrittenProjects)
        {
            Assert.IsGreaterThan(0, ProjectLayout.SourceFiles(project).Count(), project);
        }
    }

    [TestMethod]
    public void UserFacingTextLivesOnlyInTheTextCatalog()
    {
        List<string> offenders = [];
        int scanned = 0;

        foreach (string path in ProjectLayout.SourceFiles("Numera.Discord"))
        {
            scanned++;

            if (Path.GetFileName(path).StartsWith(TextCatalogFilePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (ContainsJapaneseLiteral(File.ReadAllLines(path)))
            {
                offenders.Add(Path.GetFileName(path));
            }
        }

        Assert.IsGreaterThan(1, scanned);
        CollectionAssert.AreEqual(Array.Empty<string>(), offenders);
    }

    [TestMethod]
    public void StandardCommandAttributesAreAbsentFromHandWrittenCode()
    {
        List<string> offenders = [];

        foreach (string project in HandWrittenProjects)
        {
            foreach (string path in ProjectLayout.SourceFiles(project))
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    if (ContainsCommandAttribute(line))
                    {
                        offenders.Add($"{Path.GetFileName(path)}: {line.Trim()}");
                    }
                }
            }
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), offenders);
    }

    private static bool ContainsCommandAttribute(string line)
    {
        foreach (string attribute in DiscordCommandAttributes)
        {
            int index = line.IndexOf($"[{attribute}", StringComparison.Ordinal);

            if (index < 0)
            {
                continue;
            }

            int after = index + attribute.Length + 1;

            if (after < line.Length && line[after] is '(' or ']')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsJapaneseLiteral(string[] lines)
    {
        foreach (string line in lines)
        {
            if (line.TrimStart().StartsWith(EndpointDeclarationPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            int comment = line.IndexOf("//", StringComparison.Ordinal);
            ReadOnlySpan<char> code = comment < 0 ? line : line.AsSpan(0, comment);

            if (ContainsJapaneseInsideQuotes(code))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsJapaneseInsideQuotes(ReadOnlySpan<char> code)
    {
        bool inside = false;

        foreach (char character in code)
        {
            if (character == '"')
            {
                inside = !inside;
                continue;
            }

            if (inside && IsJapanese(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJapanese(char character) =>
        character is (>= '぀' and <= 'ヿ') or (>= '一' and <= '鿿');
}
