using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal readonly record struct PostingLine(
    LedgerAccount Ledger,
    EntrySide Side,
    MoneyMinor Amount,
    MoneyMinor HoldRelease,
    bool EnforceDepositInvariants)
{
    internal static PostingLine Deposit(LedgerAccount ledger, EntrySide side, MoneyMinor amount) =>
        new(ledger, side, amount, MoneyMinor.Zero, EnforceDepositInvariants: true);

    internal static PostingLine DepositReleasingHold(
        LedgerAccount ledger,
        EntrySide side,
        MoneyMinor amount,
        MoneyMinor holdRelease) =>
        new(ledger, side, amount, holdRelease, EnforceDepositInvariants: true);

    internal static PostingLine Institutional(LedgerAccount ledger, EntrySide side, MoneyMinor amount) =>
        new(ledger, side, amount, MoneyMinor.Zero, EnforceDepositInvariants: false);
}

internal sealed class LedgerPostingBuilder
{
    private readonly List<PostingLine> lines = [];

    internal void Add(PostingLine line) => lines.Add(line);

    internal IReadOnlyList<PostingLine> Lines => lines;

    internal LedgerAccount[] OrderedAccounts()
    {
        List<LedgerAccount> accounts = [];

        foreach (PostingLine line in lines)
        {
            bool known = false;

            foreach (LedgerAccount account in accounts)
            {
                known |= account.Id == line.Ledger.Id;
            }

            if (!known)
            {
                accounts.Add(line.Ledger);
            }
        }

        LedgerAccount[] ordered = [.. accounts];
        Array.Sort(ordered, static (left, right) => left.Id.Value.CompareTo(right.Id.Value));
        return ordered;
    }

    internal JournalEntryDraft[] BuildDrafts(LedgerAccount[] ordered, IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(idGenerator);

        List<JournalEntryDraft> drafts = new(lines.Count);

        foreach (LedgerAccount account in ordered)
        {
            foreach (PostingLine line in lines)
            {
                if (line.Ledger.Id == account.Id)
                {
                    drafts.Add(new JournalEntryDraft(
                        JournalEntryId.FromValue(idGenerator.NextId()), account.Id, line.Side, line.Amount));
                }
            }
        }

        return [.. drafts];
    }

    internal void ApplyProjections(
        IBankingUnitOfWork unitOfWork,
        LedgerAccount[] ordered,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        foreach (LedgerAccount account in ordered)
        {
            LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(account.Id) ?? LedgerBalance.Empty;
            bool enforceDepositInvariants = false;

            foreach (PostingLine line in lines)
            {
                if (line.Ledger.Id != account.Id)
                {
                    continue;
                }

                balance = balance.ApplyPosting(line.Side, account.NormalSide, line.Amount);
                enforceDepositInvariants |= line.EnforceDepositInvariants;

                if (line.HoldRelease.IsPositive)
                {
                    balance = balance.DecreaseHold(line.HoldRelease);
                }
            }

            unitOfWork.LedgerAccounts.UpsertProjection(
                account.Id,
                enforceDepositInvariants ? balance.EnsureDepositAccountInvariants() : balance,
                now);
        }
    }
}
