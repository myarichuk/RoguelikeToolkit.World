namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// History simulation configuration plus the per-turn budget ledger. Built once per world by
/// <c>WorldBuilder.WithHistory</c> (fixed turn of 10 years by default), attached to the map next
/// to the <see cref="HistoryCatalog"/>, and read by every history stage. The P2b operators spend
/// decisions and emergences through <see cref="TrySpendDecision"/> /
/// <see cref="TrySpendEmergence"/>; the event cap is enforced by the catalog on append.
/// </summary>
public sealed class HistoryContext
{
    private int _turnYears = 10;
    private int _turnCount = 30;
    private int _maxDecisionsPerTurn = 8;
    private int _maxEmergencesPerTurn = 4;
    private int _maxSettlementsPerPolity = 6;
    private int _maxEventsPerTurn = 64;
    private int _maxEvents = 4096;
    private int _maxPolities = 8;
    private int _maxVillains = 2;
    private int _maxSuccessors = 6;
    private double _hazardRate = 1;

    /// <summary>Default peoples roster: one human and one non-human folk, matching the shipped tables.</summary>
    public HistoryContext()
    {
        Peoples.Add(new HistoryPeople { FolkKey = "human", CultureKey = "human_common" });
        Peoples.Add(new HistoryPeople { FolkKey = "dwarf", CultureKey = "dwarven_clan" });
    }

    /// <summary>Years per history turn; the fixed step of the whole sim (default 10). Must be positive.</summary>
    public int TurnYears
    {
        get => _turnYears;
        set => _turnYears = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "TurnYears must be positive.");
    }

    /// <summary>How many turns the default span simulates (turns 0..<see cref="TurnCount"/>; default 30). Must be non-negative.</summary>
    public int TurnCount
    {
        get => _turnCount;
        set => _turnCount = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "TurnCount cannot be negative.");
    }

    /// <summary>Per-turn decision budget per operator (default 8, the N in "N decisions / M emergences / K events"). Must be non-negative.</summary>
    public int MaxDecisionsPerTurn
    {
        get => _maxDecisionsPerTurn;
        set => _maxDecisionsPerTurn = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Budget cannot be negative.");
    }

    /// <summary>Per-turn emergence budget per operator (default 4). Must be non-negative.</summary>
    public int MaxEmergencesPerTurn
    {
        get => _maxEmergencesPerTurn;
        set => _maxEmergencesPerTurn = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Budget cannot be negative.");
    }

    /// <summary>Per-turn event budget for the whole catalog (default 64, the K in "N / M / K"). Must be non-negative.</summary>
    public int MaxEventsPerTurn
    {
        get => _maxEventsPerTurn;
        set => _maxEventsPerTurn = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Budget cannot be negative.");
    }

    /// <summary>Event-log cap; appending beyond it compacts the oldest non-founding events (default 4096). Must be positive.</summary>
    public int MaxEvents
    {
        get => _maxEvents;
        set => _maxEvents = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "MaxEvents must be positive.");
    }

    /// <summary>Upper bound on polity count (default 8). Zero means history simulates with no polities. Must be non-negative.</summary>
    public int MaxPolities
    {
        get => _maxPolities;
        set => _maxPolities = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "MaxPolities cannot be negative.");
    }

    /// <summary>
    /// Upper bound on successor polities collapses may found over a whole history (default 6). Founding by
    /// <see cref="MaxPolities"/> and by collapse are separate budgets: a collapse ends a polity, so it is not held to the
    /// living-polity count. Zero means no polity ever collapses. Must be non-negative.
    /// </summary>
    public int MaxSuccessors
    {
        get => _maxSuccessors;
        set => _maxSuccessors = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "MaxSuccessors cannot be negative.");
    }

    /// <summary>Upper bound on risen villains (default 2). Zero means no villain ever rises. Must be non-negative.</summary>
    public int MaxVillains
    {
        get => _maxVillains;
        set => _maxVillains = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "MaxVillains cannot be negative.");
    }

    /// <summary>Upper bound on standing settlements per polity, capital included (default 6). Must be non-negative.</summary>
    public int MaxSettlementsPerPolity
    {
        get => _maxSettlementsPerPolity;
        set => _maxSettlementsPerPolity = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "MaxSettlementsPerPolity cannot be negative.");
    }

    /// <summary>Turns a displaced group waits for a taker before the pool re-absorbs it into its origin polity (or records it lost). Default 3.</summary>
    public int DisplacedDecayTurns { get; set; } = 3;

    /// <summary>Multiplier on every hazard's per-turn chance (default 1; 0 switches physics-driven shocks off). Must be non-negative.</summary>
    public double HazardRate
    {
        get => _hazardRate;
        set => _hazardRate = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "HazardRate cannot be negative.");
    }

    /// <summary>The (folk, culture) pairs <see cref="CultureSeedStage"/> seeds the world with, in priority order.</summary>
    public List<HistoryPeople> Peoples { get; } = new();

    /// <summary>Current history turn; advanced by <see cref="BeginTurn"/>.</summary>
    public int CurrentTurn { get; private set; }

    /// <summary>Decisions spent this turn.</summary>
    public int DecisionsSpent { get; private set; }

    /// <summary>Emergences spent this turn.</summary>
    public int EmergencesSpent { get; private set; }

    private readonly Dictionary<(string Operator, int Turn), int> _operatorDecisions = new();
    private readonly Dictionary<(string Operator, int Turn), int> _operatorEmergences = new();

    /// <summary>Opens <paramref name="turn"/>: sets <see cref="CurrentTurn"/> and resets the per-turn ledger.</summary>
    public void BeginTurn(int turn)
    {
        if (turn < 0) throw new ArgumentOutOfRangeException(nameof(turn), "Turn cannot be negative.");
        CurrentTurn = turn;
        DecisionsSpent = 0;
        EmergencesSpent = 0;
    }

    /// <summary>Spends one decision when the per-turn budget allows; returns false (spending nothing) when exhausted.</summary>
    public bool TrySpendDecision()
    {
        if (DecisionsSpent >= MaxDecisionsPerTurn) return false;
        DecisionsSpent++;
        return true;
    }

    /// <summary>Spends one emergence when the per-turn budget allows; returns false (spending nothing) when exhausted.</summary>
    public bool TrySpendEmergence()
    {
        if (EmergencesSpent >= MaxEmergencesPerTurn) return false;
        EmergencesSpent++;
        return true;
    }

    /// <summary>
    /// Spends one decision from <paramref name="operatorName"/>'s own per-turn budget (see
    /// <see cref="MaxDecisionsPerTurn"/>); returns false (spending nothing) when that operator
    /// exhausted its budget for <paramref name="turn"/>. Operators run the whole span one after
    /// another, so a shared ledger would let a noisy war starve every later operator: per-operator
    /// ledgers keep each stage's spend isolated. Never touches <see cref="DecisionsSpent"/>.
    /// </summary>
    /// <param name="operatorName">Stage name (e.g. <c>WarOperator</c>); must be non-blank.</param>
    /// <param name="turn">History turn the spend belongs to; must be non-negative.</param>
    public bool TrySpendOperatorDecision(string operatorName, int turn)
    {
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("Operator name must be non-blank.", nameof(operatorName));
        if (turn < 0) throw new ArgumentOutOfRangeException(nameof(turn), "Turn cannot be negative.");
        var key = (operatorName, turn);
        _operatorDecisions.TryGetValue(key, out int spent);
        if (spent >= MaxDecisionsPerTurn) return false;
        _operatorDecisions[key] = spent + 1;
        return true;
    }

    /// <summary>
    /// Spends one emergence from <paramref name="operatorName"/>'s own per-turn budget (see
    /// <see cref="MaxEmergencesPerTurn"/>); returns false (spending nothing) when that operator
    /// exhausted its budget for <paramref name="turn"/>. Never touches <see cref="EmergencesSpent"/>.
    /// </summary>
    /// <param name="operatorName">Stage name (e.g. <c>RebellionOperator</c>); must be non-blank.</param>
    /// <param name="turn">History turn the spend belongs to; must be non-negative.</param>
    public bool TrySpendOperatorEmergence(string operatorName, int turn)
    {
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("Operator name must be non-blank.", nameof(operatorName));
        if (turn < 0) throw new ArgumentOutOfRangeException(nameof(turn), "Turn cannot be negative.");
        var key = (operatorName, turn);
        _operatorEmergences.TryGetValue(key, out int spent);
        if (spent >= MaxEmergencesPerTurn) return false;
        _operatorEmergences[key] = spent + 1;
        return true;
    }

    /// <summary>
    /// Forgets <paramref name="operatorName"/>'s ledger, so a re-run starts from a full budget.
    /// Each operator calls this first in <c>Apply</c>; the turn driver (<c>HistoryDriver</c>)
    /// calls <see cref="ClearOperatorBudgets"/> for the whole band.
    /// </summary>
    /// <param name="operatorName">Stage name; must be non-blank.</param>
    public void ResetOperatorBudget(string operatorName)
    {
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("Operator name must be non-blank.", nameof(operatorName));
        var decisions = new List<(string Operator, int Turn)>();
        foreach (var key in _operatorDecisions.Keys)
            if (key.Operator == operatorName) decisions.Add(key);
        foreach (var key in decisions) _operatorDecisions.Remove(key);
        var emergences = new List<(string Operator, int Turn)>();
        foreach (var key in _operatorEmergences.Keys)
            if (key.Operator == operatorName) emergences.Add(key);
        foreach (var key in emergences) _operatorEmergences.Remove(key);
    }

    /// <summary>Clears every operator ledger; called by the turn driver at the head of the history band.</summary>
    public void ClearOperatorBudgets()
    {
        _operatorDecisions.Clear();
        _operatorEmergences.Clear();
    }
}
