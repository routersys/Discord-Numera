using System.Collections.Frozen;

namespace Numera.Domain.Common;

public sealed class StateTransitionTable<TState>
    where TState : struct, Enum
{
    private readonly FrozenSet<(TState From, TState To)> transitions;
    private readonly FrozenSet<TState> creatable;
    private readonly string violationCode;

    private StateTransitionTable(
        FrozenSet<(TState From, TState To)> transitions,
        FrozenSet<TState> creatable,
        string violationCode)
    {
        this.transitions = transitions;
        this.creatable = creatable;
        this.violationCode = violationCode;
    }

    public static StateTransitionTableBuilder<TState> Create(string violationCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(violationCode);
        return new StateTransitionTableBuilder<TState>(violationCode);
    }

    public bool IsCreatable(TState state) => creatable.Contains(state);

    public bool IsAllowed(TState from, TState to) => transitions.Contains((from, to));

    public bool IsTerminal(TState state)
    {
        foreach ((TState from, TState _) in transitions)
        {
            if (EqualityComparer<TState>.Default.Equals(from, state))
            {
                return false;
            }
        }

        return true;
    }

    public void EnsureCreatable(TState state)
    {
        if (!IsCreatable(state))
        {
            throw InvariantViolationException.Create(violationCode);
        }
    }

    public TState EnsureAllowed(TState from, TState to)
    {
        if (!IsAllowed(from, to))
        {
            throw InvariantViolationException.Create(violationCode);
        }

        return to;
    }

    internal static StateTransitionTable<TState> Build(
        IEnumerable<(TState From, TState To)> transitions,
        IEnumerable<TState> creatable,
        string violationCode) =>
        new(transitions.ToFrozenSet(), creatable.ToFrozenSet(), violationCode);
}

public sealed class StateTransitionTableBuilder<TState>
    where TState : struct, Enum
{
    private readonly HashSet<(TState From, TState To)> transitions = [];
    private readonly HashSet<TState> creatable = [];
    private readonly string violationCode;

    internal StateTransitionTableBuilder(string violationCode) => this.violationCode = violationCode;

    public StateTransitionTableBuilder<TState> AllowCreation(params TState[] states)
    {
        ArgumentNullException.ThrowIfNull(states);
        foreach (TState state in states)
        {
            creatable.Add(state);
        }

        return this;
    }

    public StateTransitionTableBuilder<TState> Allow(TState from, params TState[] targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        foreach (TState target in targets)
        {
            if (EqualityComparer<TState>.Default.Equals(from, target))
            {
                throw InvariantViolationException.Create(InvariantViolationCode.StateTransitionSelfLoop);
            }

            transitions.Add((from, target));
        }

        return this;
    }

    public StateTransitionTable<TState> Build() =>
        StateTransitionTable<TState>.Build(transitions, creatable, violationCode);
}
