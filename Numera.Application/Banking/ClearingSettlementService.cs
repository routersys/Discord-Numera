using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal sealed record ClearingSettlementOutcome(
    ClearingCycleId CycleId,
    IReadOnlyList<BusinessOperationId> SettledOperations);

internal sealed class ClearingSettlementService
{
    internal const string SettlementOperationType = "CLEARING_NET_SETTLEMENT";
    internal const string SettlementTransactionType = "CLEARING_NET_SETTLEMENT";
    internal const string AgentTransactionType = "CLEARING_AGENT_SETTLEMENT";
    internal const string CentralBankTransactionType = "CLEARING_CENTRAL_BANK_SETTLEMENT";
    internal const string DescriptionCode = "CLEARING_SETTLEMENT";

    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    internal ClearingSettlementService(IClock clock, IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    internal Result Lock(IBankingUnitOfWork unitOfWork, ClearingCycleId cycleId)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        if (Reload(unitOfWork, cycleId) is not { } cycle)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.ClearingCycleNotFound);
        }

        if (cycle.Status != ClearingCycleStatus.Open)
        {
            return Result.Success();
        }

        cycle.Lock(clock.Now());
        unitOfWork.Clearing.UpdateCycle(cycle);

        foreach (ClearingInstruction instruction in unitOfWork.Clearing.ListInstructions(cycleId))
        {
            if (instruction.Status != ClearingInstructionStatus.Accepted)
            {
                continue;
            }

            instruction.Lock();
            unitOfWork.Clearing.UpdateInstruction(instruction);
        }

        return Result.Success();
    }

    internal Result<ClearingSettlementOutcome> Settle(IBankingUnitOfWork unitOfWork, ClearingCycleId cycleId)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        if (Reload(unitOfWork, cycleId) is not { } cycle)
        {
            return Result<ClearingSettlementOutcome>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.ClearingCycleNotFound);
        }

        if (cycle.Status != ClearingCycleStatus.Locked)
        {
            return Result<ClearingSettlementOutcome>.Failure(
                ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        IReadOnlyList<ClearingPosition> positions = unitOfWork.Clearing.ListPositions(cycleId);

        if (positions.Count == 0)
        {
            return Result<ClearingSettlementOutcome>.Failure(
                ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        ClearingPosition.EnsureBalanced([.. positions]);

        UtcTimestamp now = clock.Now();
        BusinessDate businessDate = BusinessDateOf(now);

        Result<ParticipantSettlement[]> participants = ResolveParticipants(
            unitOfWork,
            positions,
            cycle.CurrencyId,
            SplitsOf(unitOfWork, cycleId, FxApplicationService.ClearingInstructionKind),
            SplitsOf(unitOfWork, cycleId, AtmApplicationService.ClearingInstructionKind));

        if (!participants.IsSuccess)
        {
            return Result<ClearingSettlementOutcome>.Failure(participants.Error!);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            SettlementOperationType,
            cycle.EconomyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            IdempotencyKey.Create(SettlementOperationType, cycle.Id.Value.ToString()),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        cycle.BeginSettling();
        unitOfWork.Clearing.UpdateCycle(cycle);

        Result posted = PostParticipantLegs(
            unitOfWork, participants.Value, operation, cycle.CurrencyId, businessDate, now);

        if (!posted.IsSuccess)
        {
            return Result<ClearingSettlementOutcome>.Failure(posted.Error!);
        }

        Result centralBank = PostCentralBankLeg(
            unitOfWork, participants.Value, operation, cycle.CurrencyId, businessDate, now);

        if (!centralBank.IsSuccess)
        {
            return Result<ClearingSettlementOutcome>.Failure(centralBank.Error!);
        }

        List<BusinessOperationId> settledOperations = [];

        foreach (ClearingInstruction instruction in unitOfWork.Clearing.ListInstructions(cycleId))
        {
            if (instruction.Status != ClearingInstructionStatus.Locked)
            {
                continue;
            }

            instruction.Settle(now);
            unitOfWork.Clearing.UpdateInstruction(instruction);
            settledOperations.Add(instruction.BusinessOperationId);

            SettleFxComponents(unitOfWork, instruction.Id, now);

            if (instruction.PaymentOrderId is not { } orderId ||
                unitOfWork.PaymentOrders.Find(orderId) is not { } order ||
                order.Status != PaymentOrderStatus.Accepted)
            {
                continue;
            }

            order.BeginSettling();
            order.RecordSettlementFinality(now);
            order.Settle();
            unitOfWork.PaymentOrders.Update(order);
        }

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        return Result<ClearingSettlementOutcome>.Success(
            new ClearingSettlementOutcome(cycleId, settledOperations));
    }

    private static void SettleFxComponents(
        IBankingUnitOfWork unitOfWork,
        ClearingInstructionId instructionId,
        UtcTimestamp now)
    {
        foreach (FxSettlementLegComponent component in
            unitOfWork.Fx.ListClearingComponents(instructionId))
        {
            if (component.Status != FxSettlementLegComponentStatus.Clearing)
            {
                continue;
            }

            component.Settle(now);
            unitOfWork.Fx.UpdateSettlementLegComponent(component);

            bool outstanding = false;

            foreach (FxSettlementLegComponent sibling in
                unitOfWork.Fx.ListSettlementLegComponents(component.LegId))
            {
                outstanding |= sibling.Id != component.Id &&
                    sibling.Status == FxSettlementLegComponentStatus.Clearing;
            }

            if (outstanding ||
                unitOfWork.Fx.FindSettlementLeg(component.LegId) is not
                    { Status: FxSettlementLegStatus.Clearing } leg)
            {
                continue;
            }

            leg.Settle();
            unitOfWork.Fx.UpdateSettlementLeg(leg);
        }
    }

    internal Result Close(IBankingUnitOfWork unitOfWork, ClearingCycleId cycleId)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        if (Reload(unitOfWork, cycleId) is not { } cycle)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.ClearingCycleNotFound);
        }

        if (cycle.Status == ClearingCycleStatus.Closed)
        {
            return Result.Success();
        }

        if (cycle.Status != ClearingCycleStatus.Settling)
        {
            return Result.Failure(ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        foreach (ClearingInstruction instruction in unitOfWork.Clearing.ListInstructions(cycleId))
        {
            if (!instruction.IsFinal)
            {
                return Result.Failure(
                    ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
            }
        }

        cycle.Close(clock.Now());
        unitOfWork.Clearing.UpdateCycle(cycle);

        return Result.Success();
    }

    private readonly record struct ClearingSplit(MoneyMinor Payable, MoneyMinor Receivable);

    private readonly record struct ParticipantLeg(
        LedgerAccount? Payable,
        LedgerAccount? Receivable,
        MoneyMinor GrossPayable,
        MoneyMinor GrossReceivable);

    private readonly record struct ParticipantSettlement(
        SettlementSide Side,
        ParticipantLeg Retail,
        ParticipantLeg Foreign,
        ParticipantLeg Network,
        MoneyMinor Net);

    private static Dictionary<BankId, ClearingSplit> SplitsOf(
        IBankingUnitOfWork unitOfWork,
        ClearingCycleId cycleId,
        string instructionKind)
    {
        Dictionary<BankId, ClearingSplit> splits = [];

        foreach (ClearingInstruction instruction in unitOfWork.Clearing.ListInstructions(cycleId))
        {
            if (instruction.InstructionKind != instructionKind ||
                instruction.Status != ClearingInstructionStatus.Locked)
            {
                continue;
            }

            splits.TryGetValue(instruction.SourceBankId, out ClearingSplit source);
            splits[instruction.SourceBankId] = source with
            {
                Payable = source.Payable.Add(instruction.Amount),
            };

            splits.TryGetValue(instruction.DestinationBankId, out ClearingSplit destination);
            splits[instruction.DestinationBankId] = destination with
            {
                Receivable = destination.Receivable.Add(instruction.Amount),
            };
        }

        return splits;
    }

    private static Result<ParticipantLeg> ResolveLeg(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CurrencyId currencyId,
        LedgerAccountKind payableKind,
        LedgerAccountKind receivableKind,
        MoneyMinor grossPayable,
        MoneyMinor grossReceivable)
    {
        LedgerAccount? payable = grossPayable.IsPositive
            ? unitOfWork.LedgerAccounts.FindPostingByKind(
                bank.GeneralLedgerBookId, payableKind, currencyId)
            : null;

        LedgerAccount? receivable = grossReceivable.IsPositive
            ? unitOfWork.LedgerAccounts.FindPostingByKind(
                bank.GeneralLedgerBookId, receivableKind, currencyId)
            : null;

        return (grossPayable.IsPositive && payable is null) ||
            (grossReceivable.IsPositive && receivable is null)
            ? Result<ParticipantLeg>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAccountUnavailable)
            : Result<ParticipantLeg>.Success(
                new ParticipantLeg(payable, receivable, grossPayable, grossReceivable));
    }

    private static Result<ParticipantSettlement[]> ResolveParticipants(
        IBankingUnitOfWork unitOfWork,
        IReadOnlyList<ClearingPosition> positions,
        CurrencyId currencyId,
        Dictionary<BankId, ClearingSplit> foreignSplits,
        Dictionary<BankId, ClearingSplit> networkSplits)
    {
        ParticipantSettlement[] participants = new ParticipantSettlement[positions.Count];

        for (int index = 0; index < positions.Count; index++)
        {
            ClearingPosition position = positions[index];

            if (unitOfWork.Banks.Find(position.BankId) is not { } bank)
            {
                return Result<ParticipantSettlement[]>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
            }

            Result<SettlementSide> side = InterbankSettlementPolicy.ResolveSettlementSide(
                unitOfWork, bank, currencyId);

            if (!side.IsSuccess)
            {
                return Result<ParticipantSettlement[]>.Failure(side.Error!);
            }

            foreignSplits.TryGetValue(position.BankId, out ClearingSplit foreign);
            networkSplits.TryGetValue(position.BankId, out ClearingSplit network);

            Result<ParticipantLeg> retail = ResolveLeg(
                unitOfWork,
                bank,
                currencyId,
                LedgerAccountKind.ClearingPayable,
                LedgerAccountKind.ClearingReceivable,
                position.GrossPayable.Subtract(foreign.Payable).Subtract(network.Payable),
                position.GrossReceivable.Subtract(foreign.Receivable).Subtract(network.Receivable));

            if (!retail.IsSuccess)
            {
                return Result<ParticipantSettlement[]>.Failure(retail.Error!);
            }

            Result<ParticipantLeg> external = ResolveLeg(
                unitOfWork,
                bank,
                currencyId,
                LedgerAccountKind.FxClearingPayable,
                LedgerAccountKind.FxClearingReceivable,
                foreign.Payable,
                foreign.Receivable);

            if (!external.IsSuccess)
            {
                return Result<ParticipantSettlement[]>.Failure(external.Error!);
            }

            Result<ParticipantLeg> networkLeg = ResolveLeg(
                unitOfWork,
                bank,
                currencyId,
                LedgerAccountKind.AtmNetworkPayable,
                LedgerAccountKind.AtmNetworkReceivable,
                network.Payable,
                network.Receivable);

            if (!networkLeg.IsSuccess)
            {
                return Result<ParticipantSettlement[]>.Failure(networkLeg.Error!);
            }

            participants[index] = new ParticipantSettlement(
                side.Value, retail.Value, external.Value, networkLeg.Value, position.Net);
        }

        return Result<ParticipantSettlement[]>.Success(participants);
    }

    private Result PostParticipantLegs(
        IBankingUnitOfWork unitOfWork,
        ParticipantSettlement[] participants,
        BusinessOperation operation,
        CurrencyId currencyId,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        foreach (ParticipantSettlement participant in participants)
        {
            LedgerPostingBuilder unwind = new();

            Unwind(unwind, participant.Retail);
            Unwind(unwind, participant.Foreign);
            Unwind(unwind, participant.Network);

            if (participant.Net.IsPositive)
            {
                unwind.Add(PostingLine.Institutional(
                    participant.Side.SettlementAsset, EntrySide.Debit, participant.Net));
            }
            else if (participant.Net.IsNegative)
            {
                unwind.Add(PostingLine.Institutional(
                    participant.Side.SettlementAsset, EntrySide.Credit, participant.Net.Negate()));
            }

            if (unwind.Lines.Count == 0)
            {
                continue;
            }

            Result posted = Post(
                unitOfWork,
                participant.Side.Bank.GeneralLedgerBookId,
                unwind,
                operation,
                currencyId,
                businessDate,
                now,
                SettlementTransactionType);

            if (!posted.IsSuccess)
            {
                return posted;
            }

            Result agent = PostAgentLeg(unitOfWork, participant, operation, currencyId, businessDate, now);

            if (!agent.IsSuccess)
            {
                return agent;
            }
        }

        return Result.Success();
    }

    private static void Unwind(LedgerPostingBuilder builder, ParticipantLeg leg)
    {
        if (leg.GrossPayable.IsPositive)
        {
            builder.Add(PostingLine.Institutional(leg.Payable!, EntrySide.Debit, leg.GrossPayable));
        }

        if (leg.GrossReceivable.IsPositive)
        {
            builder.Add(PostingLine.Institutional(leg.Receivable!, EntrySide.Credit, leg.GrossReceivable));
        }
    }

    private Result PostAgentLeg(
        IBankingUnitOfWork unitOfWork,
        ParticipantSettlement participant,
        BusinessOperation operation,
        CurrencyId currencyId,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        if (!participant.Side.IsIndirect || participant.Net.IsZero)
        {
            return Result.Success();
        }

        LedgerAccount clientDeposit = participant.Side.AgentClientDeposit!;
        LedgerAccount agentReserve = participant.Side.SettlingReserve;

        LedgerPostingBuilder posting = new();

        if (participant.Net.IsPositive)
        {
            posting.Add(PostingLine.Institutional(agentReserve, EntrySide.Debit, participant.Net));
            posting.Add(PostingLine.Institutional(clientDeposit, EntrySide.Credit, participant.Net));
        }
        else
        {
            MoneyMinor amount = participant.Net.Negate();
            posting.Add(PostingLine.Institutional(clientDeposit, EntrySide.Debit, amount));
            posting.Add(PostingLine.Institutional(agentReserve, EntrySide.Credit, amount));
        }

        return Post(
            unitOfWork,
            participant.Side.SettlingBank.GeneralLedgerBookId,
            posting,
            operation,
            currencyId,
            businessDate,
            now,
            AgentTransactionType);
    }

    private Result PostCentralBankLeg(
        IBankingUnitOfWork unitOfWork,
        ParticipantSettlement[] participants,
        BusinessOperation operation,
        CurrencyId currencyId,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        LedgerPostingBuilder posting = new();
        AccountingBookId? centralBankBookId = null;

        foreach (ParticipantSettlement participant in participants)
        {
            if (participant.Net.IsZero)
            {
                continue;
            }

            centralBankBookId ??= participant.Side.CentralBankLiability.BookId;

            if (centralBankBookId != participant.Side.CentralBankLiability.BookId)
            {
                return Result.Failure(
                    ErrorCategory.BankUnavailable, BankingErrorCodes.CentralBankAccountUnavailable);
            }

            posting.Add(participant.Net.IsPositive
                ? PostingLine.Institutional(
                    participant.Side.CentralBankLiability, EntrySide.Credit, participant.Net)
                : PostingLine.Institutional(
                    participant.Side.CentralBankLiability, EntrySide.Debit, participant.Net.Negate()));
        }

        return centralBankBookId is { } bookId
            ? Post(
                unitOfWork,
                bookId,
                posting,
                operation,
                currencyId,
                businessDate,
                now,
                CentralBankTransactionType)
            : Result.Success();
    }

    private Result Post(
        IBankingUnitOfWork unitOfWork,
        AccountingBookId bookId,
        LedgerPostingBuilder posting,
        BusinessOperation operation,
        CurrencyId currencyId,
        BusinessDate businessDate,
        UtcTimestamp now,
        string transactionType)
    {
        if (unitOfWork.AccountingPeriods.FindOpen(bookId, businessDate) is not { } period)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bookId,
                operation.Id,
                currencyId,
                businessDate,
                now,
                now,
                transactionType,
                DescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            period);

        posting.ApplyProjections(unitOfWork, ordered, now);

        return Result.Success();
    }

    private static ClearingCycle? Reload(IBankingUnitOfWork unitOfWork, ClearingCycleId cycleId) =>
        unitOfWork.Clearing.FindCycleById(cycleId);

    private static BusinessDate BusinessDateOf(UtcTimestamp at) => BusinessDate.FromDayNumber(
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(at.UnixMilliseconds).UtcDateTime).DayNumber);
}
