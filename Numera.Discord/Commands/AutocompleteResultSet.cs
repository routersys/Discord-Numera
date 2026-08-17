using Numera.Discord.Abstractions;

namespace Numera.Discord.Commands;

public enum AutocompleteMatchKind
{
    None = 0,
    Prefix = 1,
    Substring = 2,
}

public sealed record AutocompleteCandidate(string DisplayName, string Value);

public sealed record AutocompleteDelivery(
    IReadOnlyList<EconomyAutocompleteOption> Options,
    bool Truncated,
    int RejectedCount);

public static class AutocompleteResultSet
{
    public const int MaximumResults = 25;

    public static AutocompleteMatchKind Classify(string displayName, string input)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(input);

        if (input.Length == 0)
        {
            return AutocompleteMatchKind.Prefix;
        }

        if (displayName.StartsWith(input, StringComparison.OrdinalIgnoreCase))
        {
            return AutocompleteMatchKind.Prefix;
        }

        return displayName.Contains(input, StringComparison.OrdinalIgnoreCase)
            ? AutocompleteMatchKind.Substring
            : AutocompleteMatchKind.None;
    }

    public static AutocompleteDelivery Build(IEnumerable<AutocompleteCandidate> candidates, string input)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(input);

        List<(AutocompleteMatchKind Match, AutocompleteCandidate Candidate)> matched = [];
        int rejected = 0;

        foreach (AutocompleteCandidate candidate in candidates)
        {
            if (!EconomyAutocompleteOption.IsAcceptable(candidate.DisplayName, candidate.Value))
            {
                rejected++;
                continue;
            }

            AutocompleteMatchKind match = Classify(candidate.DisplayName, input);
            if (match != AutocompleteMatchKind.None)
            {
                matched.Add((match, candidate));
            }
        }

        matched.Sort(static (left, right) =>
        {
            int byMatch = ((int)left.Match).CompareTo((int)right.Match);
            return byMatch != 0
                ? byMatch
                : string.CompareOrdinal(left.Candidate.DisplayName, right.Candidate.DisplayName);
        });

        bool truncated = matched.Count > MaximumResults;
        int take = truncated ? MaximumResults : matched.Count;

        List<EconomyAutocompleteOption> options = new(take);
        for (int index = 0; index < take; index++)
        {
            options.Add(EconomyAutocompleteOption.Create(
                matched[index].Candidate.DisplayName,
                matched[index].Candidate.Value));
        }

        return new AutocompleteDelivery(options, truncated, rejected);
    }

    public static AutocompleteDelivery Enforce(IReadOnlyList<EconomyAutocompleteOption> provided)
    {
        ArgumentNullException.ThrowIfNull(provided);

        bool truncated = provided.Count > MaximumResults;
        int take = truncated ? MaximumResults : provided.Count;

        List<EconomyAutocompleteOption> options = new(take);
        for (int index = 0; index < take; index++)
        {
            options.Add(provided[index]);
        }

        return new AutocompleteDelivery(options, truncated, RejectedCount: 0);
    }
}
