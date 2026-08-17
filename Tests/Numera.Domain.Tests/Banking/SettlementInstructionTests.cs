using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class SettlementInstructionTests
{
    private static readonly SettlementInstructionId Identifier =
        SettlementInstructionId.FromValue(EntityIdValue.FromBits(1));

    private static readonly BusinessOperationId Operation =
        BusinessOperationId.FromValue(EntityIdValue.FromBits(2));

    private static readonly CurrencyId Currency = CurrencyId.FromValue(EntityIdValue.FromBits(3));
    private static readonly BankId Source = BankId.FromValue(EntityIdValue.FromBits(4));
    private static readonly BankId Destination = BankId.FromValue(EntityIdValue.FromBits(5));
    private static readonly UtcTimestamp CreatedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);
    private static readonly UtcTimestamp LaterAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_600_000);

    private static SettlementInstruction Create(long amount = 1_000) => SettlementInstruction.Create(
        Identifier, Operation, Currency, Source, Destination, MoneyMinor.FromMinor(amount), CreatedAt);

    [TestMethod]
    public void CreationStartsAsCreatedWithoutFinalityFacts()
    {
        SettlementInstruction instruction = Create();

        Assert.AreEqual(SettlementInstructionStatus.Created, instruction.Status);
        Assert.IsNull(instruction.LockedAt);
        Assert.IsNull(instruction.SettledAt);
        Assert.IsFalse(instruction.IsFinal);
    }

    [TestMethod]
    public void QueuedInstructionCanStillBeLockedAndSettled()
    {
        SettlementInstruction instruction = Create();

        instruction.Queue();
        instruction.LockForSettlement(CreatedAt);
        instruction.Settle(LaterAt);

        Assert.IsTrue(instruction.IsFinal);
        Assert.AreEqual(CreatedAt, instruction.LockedAt);
        Assert.AreEqual(LaterAt, instruction.SettledAt);
    }

    [TestMethod]
    public void SettlementRequiresTheLockedState()
    {
        SettlementInstruction instruction = Create();

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => instruction.Settle(LaterAt));

        Assert.AreEqual(InvariantViolationCode.SettlementInstructionTransitionInvalid, exception.Code);
    }

    [TestMethod]
    public void CancellationIsForbiddenOnceLocked()
    {
        SettlementInstruction instruction = Create();
        instruction.LockForSettlement(CreatedAt);

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            instruction.Cancel);

        Assert.AreEqual(InvariantViolationCode.SettlementInstructionTransitionInvalid, exception.Code);
    }

    [TestMethod]
    public void SettledInstructionIsTerminal()
    {
        SettlementInstruction instruction = Create();
        instruction.LockForSettlement(CreatedAt);
        instruction.Settle(LaterAt);

        Assert.ThrowsExactly<InvariantViolationException>(instruction.Fail);
        Assert.ThrowsExactly<InvariantViolationException>(instruction.Cancel);
        Assert.ThrowsExactly<InvariantViolationException>(instruction.Queue);
    }

    [TestMethod]
    public void EachTransitionAdvancesTheOptimisticVersion()
    {
        SettlementInstruction instruction = Create();
        long initial = instruction.Version;

        instruction.Queue();
        instruction.LockForSettlement(CreatedAt);
        instruction.Settle(LaterAt);

        Assert.AreEqual(initial + 3, instruction.Version);
        Assert.AreEqual(initial, instruction.PersistedVersion);
    }

    [TestMethod]
    public void SameBankEndpointsAreRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => SettlementInstruction.Create(
                Identifier, Operation, Currency, Source, Source, MoneyMinor.FromMinor(100), CreatedAt));

        Assert.AreEqual(InvariantViolationCode.SettlementInstructionEndpointsInvalid, exception.Code);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void NonPositiveAmountIsRejected(long amount)
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Create(amount));

        Assert.AreEqual(InvariantViolationCode.SettlementInstructionAmountInvalid, exception.Code);
    }

    [TestMethod]
    public void RehydrationRejectsSettledWithoutASettlementTimestamp()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => SettlementInstruction.Rehydrate(
                Identifier,
                Operation,
                Currency,
                Source,
                Destination,
                MoneyMinor.FromMinor(100),
                SettlementInstructionStatus.Settled,
                CreatedAt,
                CreatedAt,
                settledAt: null,
                1));

        Assert.AreEqual(InvariantViolationCode.SettlementInstructionFinalityInconsistent, exception.Code);
    }

    [TestMethod]
    public void RehydrationRejectsLockedWithoutALockTimestamp()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => SettlementInstruction.Rehydrate(
                Identifier,
                Operation,
                Currency,
                Source,
                Destination,
                MoneyMinor.FromMinor(100),
                SettlementInstructionStatus.LockedForSettlement,
                CreatedAt,
                lockedAt: null,
                settledAt: null,
                1));

        Assert.AreEqual(InvariantViolationCode.SettlementInstructionFinalityInconsistent, exception.Code);
    }

    [TestMethod]
    public void EveryStatusTokenRoundTrips()
    {
        foreach (SettlementInstructionStatus status in Enum.GetValues<SettlementInstructionStatus>())
        {
            Assert.AreEqual(status, SettlementInstructionCatalog.ParseStatusToken(status.ToToken()));
        }
    }
}

[TestClass]
public sealed class SettlementParticipationTests
{
    private static readonly SettlementParticipationId Identifier =
        SettlementParticipationId.FromValue(EntityIdValue.FromBits(1));

    private static readonly BankId Bank = BankId.FromValue(EntityIdValue.FromBits(2));
    private static readonly BankId Agent = BankId.FromValue(EntityIdValue.FromBits(3));

    private static readonly CentralBankSettlementAccountId Account =
        CentralBankSettlementAccountId.FromValue(EntityIdValue.FromBits(4));

    private static readonly UtcTimestamp EffectiveFrom = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);

    private static SettlementParticipation Direct() => SettlementParticipation.Enroll(
        Identifier, Bank, SettlementParticipationMode.Direct, null, Account, EffectiveFrom);

    [TestMethod]
    public void DirectParticipationRequiresACentralBankAccount()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => SettlementParticipation.Enroll(
                Identifier, Bank, SettlementParticipationMode.Direct, null, null, EffectiveFrom));

        Assert.AreEqual(InvariantViolationCode.SettlementParticipationModeInconsistent, exception.Code);
    }

    [TestMethod]
    public void DirectParticipationRejectsASettlementAgent()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => SettlementParticipation.Enroll(
                Identifier, Bank, SettlementParticipationMode.Direct, Agent, Account, EffectiveFrom));

        Assert.AreEqual(InvariantViolationCode.SettlementParticipationModeInconsistent, exception.Code);
    }

    [TestMethod]
    public void IndirectParticipationRequiresASettlementAgent()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => SettlementParticipation.Enroll(
                Identifier, Bank, SettlementParticipationMode.Indirect, null, null, EffectiveFrom));

        Assert.AreEqual(InvariantViolationCode.SettlementParticipationModeInconsistent, exception.Code);
    }

    [TestMethod]
    public void OnlyActiveDirectParticipationSettlesDirectly()
    {
        SettlementParticipation participation = Direct();

        Assert.IsFalse(participation.SettlesDirectly);

        participation.Activate();
        Assert.IsTrue(participation.SettlesDirectly);

        participation.Suspend();
        Assert.IsFalse(participation.SettlesDirectly);
    }

    [TestMethod]
    public void EndedParticipationIsTerminal()
    {
        SettlementParticipation participation = Direct();
        participation.Activate();
        participation.End(EffectiveFrom);

        Assert.ThrowsExactly<InvariantViolationException>(participation.Activate);
    }

    [TestMethod]
    public void EveryTokenRoundTrips()
    {
        foreach (SettlementParticipationMode mode in Enum.GetValues<SettlementParticipationMode>())
        {
            Assert.AreEqual(mode, SettlementParticipationCatalog.ParseModeToken(mode.ToToken()));
        }

        foreach (SettlementParticipationStatus status in Enum.GetValues<SettlementParticipationStatus>())
        {
            Assert.AreEqual(status, SettlementParticipationCatalog.ParseStatusToken(status.ToToken()));
        }

        foreach (CentralBankSettlementAccountStatus status in Enum.GetValues<CentralBankSettlementAccountStatus>())
        {
            Assert.AreEqual(status, SettlementParticipationCatalog.ParseAccountStatusToken(status.ToToken()));
        }
    }
}
