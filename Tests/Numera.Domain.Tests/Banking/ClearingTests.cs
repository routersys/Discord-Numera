using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class ClearingCycleTests
{
    private static readonly ClearingCycleId Identifier = ClearingCycleId.FromValue(EntityIdValue.FromBits(1));
    private static readonly EconomyScopeId Scope = EconomyScopeId.FromValue(EntityIdValue.FromBits(2));
    private static readonly CurrencyId Currency = CurrencyId.FromValue(EntityIdValue.FromBits(3));
    private static readonly UtcTimestamp OpenedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);
    private static readonly UtcTimestamp LaterAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_600_000);

    private static ClearingCycle Open() =>
        ClearingCycle.Open(Identifier, Scope, Currency, "2026-04-12T01", OpenedAt);

    [TestMethod]
    public void OpenCycleAcceptsNewInstructions()
    {
        ClearingCycle cycle = Open();

        Assert.AreEqual(ClearingCycleStatus.Open, cycle.Status);
        Assert.IsTrue(cycle.AcceptsNewInstructions);
    }

    [TestMethod]
    public void LockedCycleStopsAcceptingNewInstructions()
    {
        ClearingCycle cycle = Open();

        cycle.Lock(LaterAt);

        Assert.IsFalse(cycle.AcceptsNewInstructions);
        Assert.AreEqual(LaterAt, cycle.LockedAt);
    }

    [TestMethod]
    public void CanonicalLifecycleRunsOpenLockSettleClose()
    {
        ClearingCycle cycle = Open();

        cycle.Lock(LaterAt);
        cycle.BeginSettling();
        cycle.Close(LaterAt);

        Assert.AreEqual(ClearingCycleStatus.Closed, cycle.Status);
        Assert.AreEqual(LaterAt, cycle.ClosedAt);
    }

    [TestMethod]
    public void SettlingCannotBeSkipped()
    {
        ClearingCycle cycle = Open();
        cycle.Lock(LaterAt);

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => cycle.Close(LaterAt));

        Assert.AreEqual(InvariantViolationCode.ClearingCycleTransitionInvalid, exception.Code);
    }

    [TestMethod]
    public void ClosedCycleIsTerminal()
    {
        ClearingCycle cycle = Open();
        cycle.Lock(LaterAt);
        cycle.BeginSettling();
        cycle.Close(LaterAt);

        Assert.ThrowsExactly<InvariantViolationException>(() => cycle.Lock(LaterAt));
        Assert.ThrowsExactly<InvariantViolationException>(cycle.BeginSettling);
    }

    [TestMethod]
    public void EmptyCycleKeyIsRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => ClearingCycle.Open(Identifier, Scope, Currency, string.Empty, OpenedAt));

        Assert.AreEqual(InvariantViolationCode.ClearingCycleKeyInvalid, exception.Code);
    }

    [TestMethod]
    public void RehydrationRejectsLockedWithoutALockTimestamp()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => ClearingCycle.Rehydrate(
                Identifier, Scope, Currency, "k", ClearingCycleStatus.Locked, OpenedAt, null, null, 1));

        Assert.AreEqual(InvariantViolationCode.ClearingCycleTransitionInvalid, exception.Code);
    }

    [TestMethod]
    public void EveryStatusTokenRoundTrips()
    {
        foreach (ClearingCycleStatus status in Enum.GetValues<ClearingCycleStatus>())
        {
            Assert.AreEqual(status, ClearingCycleCatalog.ParseToken(status.ToToken()));
        }
    }
}

[TestClass]
public sealed class ClearingInstructionTests
{
    private static readonly ClearingInstructionId Identifier =
        ClearingInstructionId.FromValue(EntityIdValue.FromBits(1));

    private static readonly BusinessOperationId Operation =
        BusinessOperationId.FromValue(EntityIdValue.FromBits(2));

    private static readonly CurrencyId Currency = CurrencyId.FromValue(EntityIdValue.FromBits(3));
    private static readonly BankId Source = BankId.FromValue(EntityIdValue.FromBits(4));
    private static readonly BankId Destination = BankId.FromValue(EntityIdValue.FromBits(5));
    private static readonly ClearingCycleId Cycle = ClearingCycleId.FromValue(EntityIdValue.FromBits(6));
    private static readonly UtcTimestamp CreatedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);

    private static ClearingInstruction Create(long amount = 100) => ClearingInstruction.Create(
        Identifier,
        Operation,
        paymentOrderId: null,
        Currency,
        Source,
        Destination,
        MoneyMinor.FromMinor(amount),
        "CUSTOMER_TRANSFER",
        CreatedAt);

    [TestMethod]
    public void CanonicalLifecycleRunsCreatedAcceptedLockedSettled()
    {
        ClearingInstruction instruction = Create();

        instruction.Accept(Cycle);
        instruction.Lock();
        instruction.Settle(CreatedAt);

        Assert.IsTrue(instruction.IsFinal);
        Assert.AreEqual(Cycle, instruction.ClearingCycleId);
    }

    [TestMethod]
    public void LockingRequiresACycle()
    {
        ClearingInstruction instruction = Create();

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            instruction.Lock);

        Assert.AreEqual(InvariantViolationCode.ClearingInstructionCycleMissing, exception.Code);
    }

    [TestMethod]
    public void CancellationIsForbiddenOnceLocked()
    {
        ClearingInstruction instruction = Create();
        instruction.Accept(Cycle);
        instruction.Lock();

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            instruction.Cancel);

        Assert.AreEqual(InvariantViolationCode.ClearingInstructionTransitionInvalid, exception.Code);
    }

    [TestMethod]
    public void SettledInstructionIsTerminal()
    {
        ClearingInstruction instruction = Create();
        instruction.Accept(Cycle);
        instruction.Lock();
        instruction.Settle(CreatedAt);

        Assert.ThrowsExactly<InvariantViolationException>(instruction.Fail);
        Assert.ThrowsExactly<InvariantViolationException>(instruction.Cancel);
    }

    [TestMethod]
    public void SameBankEndpointsAreRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => ClearingInstruction.Create(
                Identifier,
                Operation,
                null,
                Currency,
                Source,
                Source,
                MoneyMinor.FromMinor(100),
                "CUSTOMER_TRANSFER",
                CreatedAt));

        Assert.AreEqual(InvariantViolationCode.ClearingInstructionEndpointsInvalid, exception.Code);
    }

    [TestMethod]
    public void RehydrationRejectsLockedWithoutACycle()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => ClearingInstruction.Rehydrate(
                Identifier,
                Operation,
                null,
                null,
                Currency,
                Source,
                Destination,
                MoneyMinor.FromMinor(100),
                "CUSTOMER_TRANSFER",
                ClearingInstructionStatus.Locked,
                CreatedAt,
                null,
                1));

        Assert.AreEqual(InvariantViolationCode.ClearingInstructionCycleMissing, exception.Code);
    }

    [TestMethod]
    public void EveryStatusTokenRoundTrips()
    {
        foreach (ClearingInstructionStatus status in Enum.GetValues<ClearingInstructionStatus>())
        {
            Assert.AreEqual(status, ClearingInstructionCatalog.ParseToken(status.ToToken()));
        }
    }
}

[TestClass]
public sealed class ClearingPositionTests
{
    private static readonly ClearingCycleId Cycle = ClearingCycleId.FromValue(EntityIdValue.FromBits(1));
    private static readonly CurrencyId Currency = CurrencyId.FromValue(EntityIdValue.FromBits(2));

    private static ClearingPosition Position(int seed, long receivable, long payable) =>
        ClearingPosition.Create(
            ClearingPositionId.FromValue(EntityIdValue.FromBits((ulong)seed)),
            Cycle,
            BankId.FromValue(EntityIdValue.FromBits((ulong)(seed + 100))),
            Currency,
            MoneyMinor.FromMinor(receivable),
            MoneyMinor.FromMinor(payable));

    [TestMethod]
    public void NetIsReceivableMinusPayable()
    {
        Assert.AreEqual(-20L, Position(1, 80, 100).Net.Value);
        Assert.AreEqual(20L, Position(2, 100, 80).Net.Value);
    }

    [TestMethod]
    public void CycleParticipantNetsSumToZero()
    {
        ClearingPosition[] positions = [Position(1, 80, 100), Position(2, 100, 80)];

        Assert.AreEqual(0L, ClearingPosition.NetTotal(positions).Value);
        ClearingPosition.EnsureBalanced(positions);
    }

    [TestMethod]
    public void UnbalancedCycleIsRejected()
    {
        ClearingPosition[] positions = [Position(1, 80, 100), Position(2, 100, 90)];

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => ClearingPosition.EnsureBalanced(positions));

        Assert.AreEqual(InvariantViolationCode.ClearingPositionInconsistent, exception.Code);
    }

    [TestMethod]
    public void NegativeGrossAmountsAreRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Position(1, -1, 0));

        Assert.AreEqual(InvariantViolationCode.ClearingPositionInconsistent, exception.Code);
    }

    [TestMethod]
    public void NettingUsesWideIntermediateArithmetic()
    {
        ClearingPosition[] positions =
        [
            Position(1, long.MaxValue, 0),
            Position(2, 0, long.MaxValue),
        ];

        Assert.AreEqual(0L, ClearingPosition.NetTotal(positions).Value);
    }
}
