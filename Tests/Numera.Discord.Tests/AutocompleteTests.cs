using Discord;
using Numera.Discord.Abstractions;
using Numera.Discord.Commands;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class DiscordLimitConformanceTests
{
    [TestMethod]
    public void AutocompleteOptionLimitsMatchDiscordNet()
    {
        _ = new AutocompleteResult(new string('a', EconomyAutocompleteOption.MaximumNameLength), "v");
        _ = new AutocompleteResult("n", new string('a', EconomyAutocompleteOption.MaximumValueLength));

        Assert.ThrowsExactly<ArgumentException>(
            () => new AutocompleteResult(new string('a', EconomyAutocompleteOption.MaximumNameLength + 1), "v"));
        Assert.ThrowsExactly<ArgumentException>(
            () => new AutocompleteResult(new string('a', EconomyAutocompleteOption.MinimumNameLength - 1), "v"));
        Assert.ThrowsExactly<ArgumentException>(
            () => new AutocompleteResult("n", new string('a', EconomyAutocompleteOption.MaximumValueLength + 1)));
    }

    [TestMethod]
    public void CommandLimitsMatchDiscordNet()
    {
        Dictionary<string, int> library = new(StringComparer.Ordinal)
        {
            ["name"] = SlashCommandBuilder.MaxNameLength,
            ["description"] = SlashCommandBuilder.MaxDescriptionLength,
            ["options"] = SlashCommandBuilder.MaxOptionsCount,
            ["choices"] = SlashCommandOptionBuilder.MaxChoiceCount,
        };

        Dictionary<string, int> expected = new(StringComparer.Ordinal)
        {
            ["name"] = 32,
            ["description"] = 100,
            ["options"] = 25,
            ["choices"] = AutocompleteResultSet.MaximumResults,
        };

        CollectionAssert.AreEquivalent(expected, library);
    }
}

[TestClass]
public sealed class EconomyAutocompleteOptionTests
{
    [TestMethod]
    public void CanonicalOptionIsAccepted()
    {
        EconomyAutocompleteOption option = EconomyAutocompleteOption.Create("第一銀行", "NUM0001");

        Assert.AreEqual("第一銀行", option.Name);
        Assert.AreEqual("NUM0001", option.Value);
    }

    [TestMethod]
    public void EmptyNameIsRejected() =>
        Assert.IsFalse(EconomyAutocompleteOption.TryCreate(string.Empty, "v", out _));

    [TestMethod]
    public void OverlongNameIsRejected() =>
        Assert.IsFalse(EconomyAutocompleteOption.TryCreate(new string('a', 101), "v", out _));

    [TestMethod]
    public void OverlongValueIsRejected() =>
        Assert.IsFalse(EconomyAutocompleteOption.TryCreate("n", new string('a', 101), out _));

    [TestMethod]
    public void EmptyValueIsAccepted() =>
        Assert.IsTrue(EconomyAutocompleteOption.TryCreate("n", string.Empty, out _));

    [TestMethod]
    public void CreateRaisesForOutOfRangeInput() =>
        Assert.ThrowsExactly<ArgumentException>(() => EconomyAutocompleteOption.Create(string.Empty, "v"));
}

[TestClass]
public sealed class AutocompleteResultSetTests
{
    private static AutocompleteCandidate Candidate(string name, string value) => new(name, value);

    private static IReadOnlyList<AutocompleteCandidate> SampleBanks() =>
    [
        Candidate("みどり銀行", "MIDORI01"),
        Candidate("あおぞら銀行", "AOZORA01"),
        Candidate("第一みどり信用金庫", "DAIICHI1"),
        Candidate("さくら銀行", "SAKURA01"),
    ];

    [TestMethod]
    public void EmptyInputReturnsEveryCandidateInOrdinalOrder()
    {
        AutocompleteDelivery delivery = AutocompleteResultSet.Build(SampleBanks(), string.Empty);

        Assert.AreEqual(4, delivery.Options.Count);
        Assert.IsFalse(delivery.Truncated);
        CollectionAssert.AreEqual(
            new[] { "あおぞら銀行", "さくら銀行", "みどり銀行", "第一みどり信用金庫" },
            delivery.Options.Select(static option => option.Name).ToArray());
    }

    [TestMethod]
    public void PrefixMatchesPrecedeSubstringMatches()
    {
        AutocompleteDelivery delivery = AutocompleteResultSet.Build(SampleBanks(), "みどり");

        CollectionAssert.AreEqual(
            new[] { "みどり銀行", "第一みどり信用金庫" },
            delivery.Options.Select(static option => option.Name).ToArray());
    }

    [TestMethod]
    public void NonMatchingCandidatesAreExcluded()
    {
        AutocompleteDelivery delivery = AutocompleteResultSet.Build(SampleBanks(), "存在しない");

        Assert.AreEqual(0, delivery.Options.Count);
        Assert.IsFalse(delivery.Truncated);
    }

    [TestMethod]
    public void MatchingIsCaseInsensitiveAndOrdinal()
    {
        IReadOnlyList<AutocompleteCandidate> candidates = [Candidate("Numera Bank", "NUM0001")];

        Assert.AreEqual(1, AutocompleteResultSet.Build(candidates, "numera").Options.Count);
        Assert.AreEqual(1, AutocompleteResultSet.Build(candidates, "NUMERA").Options.Count);
        Assert.AreEqual(1, AutocompleteResultSet.Build(candidates, "bank").Options.Count);
    }

    [TestMethod]
    public void ResultsAreCappedAtDiscordLimit()
    {
        List<AutocompleteCandidate> candidates = [];
        for (int index = 0; index < 40; index++)
        {
            candidates.Add(Candidate($"銀行{index:D2}", $"BANK{index:D4}"));
        }

        AutocompleteDelivery delivery = AutocompleteResultSet.Build(candidates, string.Empty);

        Assert.AreEqual(AutocompleteResultSet.MaximumResults, delivery.Options.Count);
        Assert.IsTrue(delivery.Truncated);
        Assert.AreEqual("銀行00", delivery.Options[0].Name);
        Assert.AreEqual("銀行24", delivery.Options[^1].Name);
    }

    [TestMethod]
    public void OrderingIsDeterministicAcrossRuns()
    {
        AutocompleteDelivery first = AutocompleteResultSet.Build(SampleBanks(), "銀行");
        AutocompleteDelivery second = AutocompleteResultSet.Build(SampleBanks().Reverse().ToArray(), "銀行");

        CollectionAssert.AreEqual(
            first.Options.Select(static option => option.Value).ToArray(),
            second.Options.Select(static option => option.Value).ToArray());
    }

    [TestMethod]
    public void OutOfRangeCandidatesAreRejectedWithoutFailingTheResponse()
    {
        IReadOnlyList<AutocompleteCandidate> candidates =
        [
            Candidate(new string('a', 101), "TOOLONG1"),
            Candidate(string.Empty, "EMPTY001"),
            Candidate("正常な銀行", new string('a', 101)),
            Candidate("有効な銀行", "VALID001"),
        ];

        AutocompleteDelivery delivery = AutocompleteResultSet.Build(candidates, string.Empty);

        Assert.AreEqual(1, delivery.Options.Count);
        Assert.AreEqual("有効な銀行", delivery.Options[0].Name);
        Assert.AreEqual(3, delivery.RejectedCount);
    }

    [TestMethod]
    public void ProviderOverflowIsTruncatedDefensively()
    {
        List<EconomyAutocompleteOption> provided = [];
        for (int index = 0; index < 30; index++)
        {
            provided.Add(EconomyAutocompleteOption.Create($"項目{index:D2}", $"V{index:D4}"));
        }

        AutocompleteDelivery delivery = AutocompleteResultSet.Enforce(provided);

        Assert.AreEqual(AutocompleteResultSet.MaximumResults, delivery.Options.Count);
        Assert.IsTrue(delivery.Truncated);
    }

    [TestMethod]
    public void ProviderWithinLimitIsPassedThrough()
    {
        List<EconomyAutocompleteOption> provided =
        [
            EconomyAutocompleteOption.Create("一つ目", "V1"),
            EconomyAutocompleteOption.Create("二つ目", "V2"),
        ];

        AutocompleteDelivery delivery = AutocompleteResultSet.Enforce(provided);

        Assert.AreEqual(2, delivery.Options.Count);
        Assert.IsFalse(delivery.Truncated);
    }

    [TestMethod]
    public void EmptyInputIsClassifiedAsPrefixMatch() =>
        Assert.AreEqual(AutocompleteMatchKind.Prefix, AutocompleteResultSet.Classify("任意", string.Empty));

    [TestMethod]
    public void ClassificationDistinguishesPrefixFromSubstring()
    {
        Assert.AreEqual(AutocompleteMatchKind.Prefix, AutocompleteResultSet.Classify("みどり銀行", "みどり"));
        Assert.AreEqual(AutocompleteMatchKind.Substring, AutocompleteResultSet.Classify("第一みどり", "みどり"));
        Assert.AreEqual(AutocompleteMatchKind.None, AutocompleteResultSet.Classify("さくら", "みどり"));
    }
}
