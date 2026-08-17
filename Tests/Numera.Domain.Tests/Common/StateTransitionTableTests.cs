using Numera.Domain.Common;

namespace Numera.Domain.Tests.Common;

[TestClass]
public sealed class StateTransitionTableTests
{
    private enum SampleState
    {
        Pending = 1,
        Active = 2,
        Restricted = 3,
        Closed = 4,
    }

    private const string ViolationCode = "SAMPLE_TRANSITION_INVALID";

    private static StateTransitionTable<SampleState> Table() =>
        StateTransitionTable<SampleState>.Create(ViolationCode)
            .AllowCreation(SampleState.Pending)
            .Allow(SampleState.Pending, SampleState.Active)
            .Allow(SampleState.Active, SampleState.Restricted, SampleState.Closed)
            .Allow(SampleState.Restricted, SampleState.Active, SampleState.Closed)
            .Build();

    [TestMethod]
    public void DeclaredTransitionsAreAllowed()
    {
        StateTransitionTable<SampleState> table = Table();

        Assert.IsTrue(table.IsAllowed(SampleState.Pending, SampleState.Active));
        Assert.IsTrue(table.IsAllowed(SampleState.Active, SampleState.Restricted));
        Assert.IsTrue(table.IsAllowed(SampleState.Restricted, SampleState.Active));
        Assert.IsTrue(table.IsAllowed(SampleState.Active, SampleState.Closed));
    }

    [TestMethod]
    public void UndeclaredTransitionsAreRejected()
    {
        StateTransitionTable<SampleState> table = Table();

        Assert.IsFalse(table.IsAllowed(SampleState.Pending, SampleState.Restricted));
        Assert.IsFalse(table.IsAllowed(SampleState.Pending, SampleState.Closed));
        Assert.IsFalse(table.IsAllowed(SampleState.Closed, SampleState.Active));

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => table.EnsureAllowed(SampleState.Closed, SampleState.Active));

        Assert.AreEqual(ViolationCode, exception.Code);
    }

    [TestMethod]
    public void EveryStatePairIsEitherDeclaredOrRejected()
    {
        StateTransitionTable<SampleState> table = Table();
        int allowed = 0;

        foreach (SampleState from in Enum.GetValues<SampleState>())
        {
            foreach (SampleState to in Enum.GetValues<SampleState>())
            {
                if (table.IsAllowed(from, to))
                {
                    allowed++;
                    Assert.AreNotEqual(from, to);
                }
            }
        }

        Assert.AreEqual(5, allowed);
    }

    [TestMethod]
    public void CreationIsRestrictedToDeclaredInitialStates()
    {
        StateTransitionTable<SampleState> table = Table();

        Assert.IsTrue(table.IsCreatable(SampleState.Pending));
        Assert.IsFalse(table.IsCreatable(SampleState.Active));

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => table.EnsureCreatable(SampleState.Active));

        Assert.AreEqual(ViolationCode, exception.Code);
    }

    [TestMethod]
    public void TerminalStateHasNoOutgoingTransition()
    {
        StateTransitionTable<SampleState> table = Table();

        Assert.IsTrue(table.IsTerminal(SampleState.Closed));
        Assert.IsFalse(table.IsTerminal(SampleState.Active));
        Assert.IsFalse(table.IsTerminal(SampleState.Pending));
    }

    [TestMethod]
    public void SelfTransitionIsRejectedAtDeclaration()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => StateTransitionTable<SampleState>.Create(ViolationCode)
                .Allow(SampleState.Active, SampleState.Active));

        Assert.AreEqual(InvariantViolationCode.StateTransitionSelfLoop, exception.Code);
    }

    [TestMethod]
    public void EnsureAllowedReturnsTargetState() =>
        Assert.AreEqual(SampleState.Active, Table().EnsureAllowed(SampleState.Pending, SampleState.Active));

    [TestMethod]
    public void BlankViolationCodeIsRejected() =>
        Assert.ThrowsExactly<ArgumentException>(() => StateTransitionTable<SampleState>.Create("  "));

    [TestMethod]
    public void DuplicateDeclarationsDoNotChangeBehaviour()
    {
        StateTransitionTable<SampleState> table = StateTransitionTable<SampleState>.Create(ViolationCode)
            .Allow(SampleState.Active, SampleState.Closed)
            .Allow(SampleState.Active, SampleState.Closed)
            .Build();

        Assert.IsTrue(table.IsAllowed(SampleState.Active, SampleState.Closed));
        Assert.IsTrue(table.IsTerminal(SampleState.Closed));
    }
}
