using RoguelikeToolkit.World.Core.Actors;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One requested change to the world, emitted by an <see cref="IHistoryOperator"/> during a turn and
/// applied by <see cref="HistoryDriver"/> in canonical order. Operators never mutate the catalog
/// directly, so no operator sees another's same-turn output (snapshot reads, decision A1).
/// </summary>
public abstract class Proposal
{
    /// <summary>What the proposal does; the second sort key of the commit order.</summary>
    public abstract ProposalKind Kind { get; }

    /// <summary>Entity the proposal belongs to (acting polity id, or -1 for none); the first sort key.</summary>
    public int EntityId { get; init; } = -1;

    /// <summary>Emitting operator's name; third sort key. Set by the sink.</summary>
    public string Operator { get; internal set; } = string.Empty;

    /// <summary>Per-operator, per-turn emission number; fourth sort key. Set by the sink.</summary>
    public int Sequence { get; internal set; }

    /// <summary>
    /// A proposal this one only makes sense after (the event narrating a capture, the ruin a razing leaves), or
    /// <c>null</c>. It commits after every independent proposal of the turn, and only when <see cref="Requires"/> was
    /// applied: a capture lost to a rival's razing must not leave a "captured" event behind.
    /// </summary>
    public Proposal? Requires { get; init; }

    /// <summary>
    /// Key of the resource a structural proposal claims, or <c>null</c> when proposals of this kind never
    /// conflict. A second proposal with the same <c>(Kind, ConflictKey)</c> in one turn is rejected.
    /// </summary>
    internal virtual long? ConflictKey => null;

    /// <summary>Moves <see cref="Tile"/> to polity <see cref="Proposal.EntityId"/>. Conflicts per tile.</summary>
    public sealed class ReassignTile : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.ReassignTile;
        /// <summary>Planet tile changing hands.</summary>
        public int Tile { get; init; }
        internal override long? ConflictKey => Tile;
    }

    /// <summary>Opens a road segment kept by polity <see cref="Proposal.EntityId"/>. Conflicts per tile pair within one route: two
    /// routes laid the same turn may share a segment (each keeps its own, so severing one leaves the other whole).</summary>
    public sealed class AddRoad : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AddRoad;
        /// <summary>One endpoint tile.</summary>
        public int TileA { get; init; }
        /// <summary>The other endpoint tile.</summary>
        public int TileB { get; init; }
        /// <summary>Route endpoint: the smaller capital tile.</summary>
        public int RouteA { get; init; }
        /// <summary>Route endpoint: the larger capital tile.</summary>
        public int RouteB { get; init; }
        internal override long? ConflictKey
        {
            get
            {
                long pair = ((long)Math.Min(TileA, TileB) << 32) | (uint)Math.Max(TileA, TileB);
                long route = ((long)RouteA << 32) | (uint)RouteB;
                return unchecked(pair * -7046029254386353131L ^ route); // golden-ratio mix of (segment, route)
            }
        }
    }

    /// <summary>Severs every active segment between two capital tiles. Conflicts per route.</summary>
    public sealed class SeverRoute : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.SeverRoute;
        /// <summary>Route endpoint: the smaller capital tile.</summary>
        public int RouteA { get; init; }
        /// <summary>Route endpoint: the larger capital tile.</summary>
        public int RouteB { get; init; }
        internal override long? ConflictKey => ((long)RouteA << 32) | (uint)RouteB;
    }

    /// <summary>Raises a holy site on <see cref="Tile"/> for polity <see cref="Proposal.EntityId"/>. Conflicts per tile.</summary>
    public sealed class AddHolySite : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AddHolySite;
        /// <summary>Planet tile of the site.</summary>
        public int Tile { get; init; }
        /// <summary>Geographic position of the tile.</summary>
        public GeoCoord Position { get; init; }
        internal override long? ConflictKey => Tile;
    }

    /// <summary>Adds <see cref="Weight"/> to the grudge between <see cref="Proposal.EntityId"/> and <see cref="Other"/>.</summary>
    public sealed class AddGrudge : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AddGrudge;
        /// <summary>The other polity of the pair.</summary>
        public int Other { get; init; }
        /// <summary>Positive weight to add.</summary>
        public int Weight { get; init; }
    }

    /// <summary>Repays the grudge between <see cref="Proposal.EntityId"/> and <see cref="Other"/>.</summary>
    public sealed class RepayGrudge : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.RepayGrudge;
        /// <summary>The other polity of the pair.</summary>
        public int Other { get; init; }
    }

    /// <summary>Opens a non-aggression window between <see cref="Proposal.EntityId"/> and <see cref="Other"/>.</summary>
    public sealed class AddWindow : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AddWindow;
        /// <summary>The other polity of the pair.</summary>
        public int Other { get; init; }
        /// <summary>Last turn the window covers (inclusive).</summary>
        public int UntilTurn { get; init; }
    }

    /// <summary>Appends an event on behalf of polity <see cref="Proposal.EntityId"/>.</summary>
    public sealed class AppendEvent : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AppendEvent;
        /// <summary>What happened.</summary>
        public HistoryEventKind EventKind { get; init; }
        /// <summary>The plugin kind name when <see cref="EventKind"/> is <see cref="HistoryEventKind.Plugin"/>; empty otherwise.</summary>
        public string PluginKind { get; init; } = string.Empty;
        /// <summary>Planet tile of the event, or -1.</summary>
        public int Tile { get; init; } = -1;
        /// <summary>Id of an event from an earlier turn that caused this one, or -1.</summary>
        public int Cause { get; init; } = -1;
        /// <summary>Machine-readable detail.</summary>
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
        /// <summary>Entities involved and their roles.</summary>
        public IReadOnlyList<EventParticipant>? Participants { get; init; }
    }

    /// <summary>Leaves a <see cref="HistoryScar"/> of <see cref="ScarKind"/> on <see cref="Tile"/> for polity <see cref="Proposal.EntityId"/>; a tile that already carries that kind is left alone. Conflicts per tile and kind.</summary>
    public sealed class AddScar : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AddScar;
        /// <summary>Tile the scar sits on.</summary>
        public int Tile { get; init; }
        /// <summary>Geographic position of the tile.</summary>
        public GeoCoord Position { get; init; }
        /// <summary>What is left behind (<see cref="HistoryScar.Ruin"/>, <see cref="HistoryScar.Crater"/>...); must be non-blank.</summary>
        public string ScarKind { get; init; } = HistoryScar.Ruin;
        /// <summary>The shock kind or end cause behind it.</summary>
        public string Cause { get; init; } = string.Empty;
        // Per tile and kind: a battlefield and a ruin on the same tile in one turn must both land.
        internal override long? ConflictKey => ((long)Tile << 32) | KindHash(ScarKind);

        private static uint KindHash(string kind)
        {
            uint h = 2166136261;
            foreach (char c in kind) h = (h ^ c) * 16777619;
            return h;
        }
    }

    /// <summary>Polity <see cref="Proposal.EntityId"/> takes standing settlement <see cref="Settlement"/> from its owner. Tile ownership is unchanged: the settlement is an enclave. Rejected if the captor is dead or at the settlement cap. Conflicts per settlement.</summary>
    public sealed class CaptureSettlement : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.CaptureSettlement;
        /// <summary>Index into <see cref="HistoryCatalog.Settlements"/>.</summary>
        public int Settlement { get; init; }
        internal override long? ConflictKey => Settlement;
    }

    /// <summary>
    /// Moves standing settlement <see cref="Settlement"/> (owned by polity <see cref="Proposal.EntityId"/>) to <see cref="Tier"/> and logs
    /// <see cref="HistoryEventKind.SettlementTierChanged"/>. The commit refuses anything but a one-step change, a capital below
    /// <see cref="TierOperator.CapitalFloor"/>, and an ended or foreign settlement. Conflicts per settlement.
    /// </summary>
    public sealed class ChangeTier : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.ChangeTier;
        /// <summary>Index into <see cref="HistoryCatalog.Settlements"/>.</summary>
        public int Settlement { get; init; }
        /// <summary>The new size class, one step from the current one.</summary>
        public SettlementTier Tier { get; init; }
        internal override long? ConflictKey => Settlement;
    }

    /// <summary>Sets polity <see cref="Proposal.EntityId"/>'s culture overlay to <see cref="Culture"/> (empty clears it).</summary>
    public sealed class SetOverlay : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.SetOverlay;
        /// <summary>Culture key the polity is rendered through.</summary>
        public string Culture { get; init; } = string.Empty;
    }

    /// <summary>Records that polity <see cref="Proposal.EntityId"/>'s treaty heir was born on <see cref="HeirBornTurn"/>.</summary>
    public sealed class SetHeir : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.SetHeir;
        /// <summary>Turn the heir was born.</summary>
        public int HeirBornTurn { get; init; }
    }

    /// <summary>
    /// Founds polity <see cref="Proposal.EntityId"/> (the next free id) with capital <see cref="CapitalTile"/>.
    /// The commit rejects it when the id is not the next one or the polity cap is full, then re-floods
    /// ownership from every capital (user pins win) and syncs holy-site owners.
    /// </summary>
    public sealed class AddPolity : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AddPolity;
        /// <summary>Government form.</summary>
        public PolityForm Form { get; init; }
        /// <summary>Capital tile.</summary>
        public int CapitalTile { get; init; }
        /// <summary>Founding folk key.</summary>
        public string FolkKey { get; init; } = string.Empty;
        /// <summary>Founding culture key.</summary>
        public string CultureKey { get; init; } = string.Empty;
        /// <summary>Parent polity id, or -1.</summary>
        public int Parent { get; init; } = -1;
    }

    /// <summary>
    /// Ends polity <see cref="Proposal.EntityId"/> in a collapse: one successor polity is founded on each tile of
    /// <see cref="SuccessorTiles"/> (ids are assigned by the commit, in order), the old capital settlement is razed, and
    /// ownership is re-flooded from the living capitals, so the old territory (and any pin on it) passes to the successors.
    /// Rejected when the polity is not alive, fewer than two successors fit under the polity cap, or a tile is not owned by it.
    /// </summary>
    public sealed class SplitPolity : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.SplitPolity;
        /// <summary>Capital tiles of the successors, owned by the splitting polity and not its capital.</summary>
        public IReadOnlyList<int> SuccessorTiles { get; init; } = Array.Empty<int>();
        /// <summary>Short cause key stored as the polity's end cause (default <c>collapse</c>).</summary>
        public string EndCause { get; init; } = "collapse";
        internal override long? ConflictKey => EntityId;
    }

    /// <summary>
    /// Ends polity <see cref="Proposal.EntityId"/> by absorption into <see cref="Into"/>: the land it owned, its standing
    /// settlements and its holy sites pass to the absorber, and its end cause is <c>absorbed</c>. Rejected when either polity
    /// is not alive, they are the same, or the absorbed one was founded this turn.
    /// </summary>
    public sealed class AbsorbPolity : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AbsorbPolity;
        /// <summary>Id of the polity that takes over.</summary>
        public int Into { get; init; }
        internal override long? ConflictKey => EntityId;
    }

    /// <summary>
    /// A villain with <see cref="Agenda"/> rises to rule polity <see cref="Proposal.EntityId"/> and freezes its ruler
    /// generation at <see cref="RulerGeneration"/>. The commit refuses it past <see cref="HistoryContext.MaxVillains"/>
    /// or for a polity that already has one.
    /// </summary>
    public sealed class AddVillain : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AddVillain;
        /// <summary>Agenda driving the villain.</summary>
        public string Agenda { get; init; } = string.Empty;
        /// <summary>Ruler generation the polity freezes at.</summary>
        public int RulerGeneration { get; init; }
    }

    /// <summary>
    /// Founds a settlement for polity <see cref="Proposal.EntityId"/> on <see cref="Tile"/> and logs
    /// <see cref="HistoryEventKind.SettlementFounded"/>. The commit refuses it for an unknown or ended polity, a tile
    /// the polity does not own, a tile that already holds a standing settlement, or a polity at
    /// <see cref="HistoryContext.MaxSettlementsPerPolity"/>. Conflicts per tile.
    /// </summary>
    public sealed class AddSettlement : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AddSettlement;
        /// <summary>Planet tile of the settlement.</summary>
        public int Tile { get; init; }
        /// <summary>Why the settlement exists.</summary>
        public SettlementRole Role { get; init; } = SettlementRole.Colony;
        /// <summary>Starting size class.</summary>
        public SettlementTier Tier { get; init; } = SettlementTier.Hamlet;
        /// <summary>Index of the settlement this one grows out of, or -1.</summary>
        public int Parent { get; init; } = -1;
        /// <summary>Id of an event from an earlier turn that caused the founding, or -1.</summary>
        public int Cause { get; init; } = -1;
        /// <summary>
        /// Index into <see cref="HistoryCatalog.Displaced"/> of a pool group of this polity whose whole remainder seeds the new
        /// settlement (a refuge), or -1 for the usual parent colonist share. A bad or foreign group falls back to -1.
        /// </summary>
        public int FromGroup { get; init; } = -1;
        /// <summary>Optional founding motive logged as a <c>why:</c> tag (garrison, pilgrimage, route, sedentarized), or empty.</summary>
        public string Why { get; init; } = "";
        internal override long? ConflictKey => Tile;
    }

    /// <summary>
    /// Ends the standing settlement at index <see cref="Settlement"/> (owned by polity <see cref="Proposal.EntityId"/>)
    /// with <see cref="EndCause"/> and logs <see cref="HistoryEventKind.SettlementEnded"/>. Conflicts per settlement.
    /// </summary>
    public sealed class EndSettlement : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.EndSettlement;
        /// <summary>Index into <see cref="HistoryCatalog.Settlements"/>.</summary>
        public int Settlement { get; init; }
        /// <summary>Short cause key (<c>razed</c>, <c>abandoned</c>, ...).</summary>
        public string EndCause { get; init; } = string.Empty;
        /// <summary>Id of an event from an earlier turn that caused the end, or -1.</summary>
        public int Cause { get; init; } = -1;
        /// <summary>Share of the people who die in the ending (0..1, default 0) before the rest are released into the pool.</summary>
        public double LostShare { get; init; }
        internal override long? ConflictKey => Settlement;
    }

    /// <summary>Raises polity <see cref="Proposal.EntityId"/>'s openness drift by one, bounded by <see cref="HistoryDemography.MaxOpennessDrift"/>.</summary>
    public sealed class DriftOpenness : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.DriftOpenness;
    }

    /// <summary>Sets polity <see cref="Proposal.EntityId"/>'s population index and ruler generation (absolute, not a delta).</summary>
    public sealed class SetPopulation : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.SetPopulation;
        /// <summary>New population index.</summary>
        public double Population { get; init; }
        /// <summary>New count of ruler successions so far.</summary>
        public int RulerGeneration { get; init; }
    }

    /// <summary>
    /// Adds <see cref="Delta"/> to the population of standing settlement <see cref="Settlement"/> (owned by polity
    /// <see cref="Proposal.EntityId"/>); the result is floored at zero and the owner's total is re-summed at commit.
    /// A delta, not an absolute, so it commutes with a colony taking its seed from the same settlement.
    /// </summary>
    public sealed class GrowSettlement : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.GrowSettlement;
        /// <summary>Index into <see cref="HistoryCatalog.Settlements"/>.</summary>
        public int Settlement { get; init; }
        /// <summary>Signed population change.</summary>
        public double Delta { get; init; }
    }

    /// <summary>
    /// Moves <see cref="Share"/> (0..1] of standing settlement <see cref="Settlement"/>'s people (owned by polity
    /// <see cref="Proposal.EntityId"/>) into the displaced pool.
    /// </summary>
    public sealed class ReleasePopulation : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.ReleasePopulation;
        /// <summary>Index into <see cref="HistoryCatalog.Settlements"/>.</summary>
        public int Settlement { get; init; }
        /// <summary>Fraction of the settlement's people that leave, in (0, 1].</summary>
        public double Share { get; init; } = 1;
        /// <summary>Id of an event from an earlier turn that caused the exodus, or -1.</summary>
        public int Cause { get; init; } = -1;
    }

    /// <summary>
    /// Settles up to <see cref="Amount"/> people of displaced group <see cref="Group"/> into standing settlement
    /// <see cref="Settlement"/>. Both must belong to polity <see cref="Proposal.EntityId"/> (cross-polity asylum comes with M2.9);
    /// the amount is clamped to what the group still holds at commit.
    /// </summary>
    public sealed class AbsorbDisplaced : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AbsorbDisplaced;
        /// <summary>Index into <see cref="HistoryCatalog.Displaced"/> at the snapshot.</summary>
        public int Group { get; init; }
        /// <summary>Index into <see cref="HistoryCatalog.Settlements"/> that takes them in.</summary>
        public int Settlement { get; init; }
        /// <summary>People to settle, in capacity units.</summary>
        public double Amount { get; init; }
    }

    /// <summary>
    /// Kills <see cref="Share"/> of the people of standing settlement <see cref="Settlement"/> (owned by polity
    /// <see cref="Proposal.EntityId"/>); they are recorded in <see cref="HistoryCatalog.PopulationLost"/>. Commits after
    /// <see cref="ReleasePopulation"/>, so the share applies to whoever stayed.
    /// </summary>
    public sealed class KillPopulation : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.KillPopulation;
        /// <summary>Index into <see cref="HistoryCatalog.Settlements"/>.</summary>
        public int Settlement { get; init; }
        /// <summary>Fraction of the remaining people who die, in (0, 1].</summary>
        public double Share { get; init; }
    }

    /// <summary>Adds <see cref="Delta"/> to scalar <see cref="ScalarId"/> of polity <see cref="Proposal.EntityId"/>.</summary>
    public sealed class AdjustScalar : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AdjustScalar;
        /// <summary>Declared scalar id (see <see cref="ScalarRegistry"/>).</summary>
        public int ScalarId { get; init; }
        /// <summary>Signed amount; all deltas to one scalar in a turn are summed, then clamped once.</summary>
        public double Delta { get; init; }
    }

    /// <summary>
    /// Stores <see cref="Data"/> in the plugin state bag under <see cref="Key"/> at <see cref="Version"/>. Later writes win in
    /// canonical commit order; the key's namespace must be the emitting operator's (the sink checks).
    /// </summary>
    public sealed class SetPluginState : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.SetPluginState;
        /// <summary>State bag key, <c>namespace/name</c>.</summary>
        public string Key { get; init; } = string.Empty;
        /// <summary>Blob format version.</summary>
        public int Version { get; init; }
        /// <summary>The blob; copied on commit.</summary>
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }

    /// <summary>FNV-1a of a name, for conflict keys over open vocabularies (offices, flags).</summary>
    private static uint NameHash(string name)
    {
        uint h = 2166136261;
        foreach (char c in name ?? string.Empty) h = (h ^ c) * 16777619;
        return h;
    }

    /// <summary>
    /// Adds a person loyal to polity <see cref="Proposal.EntityId"/> (-1 for none). Emit it through
    /// <see cref="ProposalSink.ProposePerson"/>, which returns a pending handle that this operator's other proposals of the turn
    /// may use wherever they name a person. The commit applies persons in a pre-pass after the polity pre-pass, assigns ids in
    /// canonical order and refuses a person past <see cref="HistoryContext.MaxPersons"/> or, unless a ruler, past
    /// <see cref="HistoryContext.MaxLivingPersonsPerPolity"/>; proposals naming a refused person are dropped.
    /// </summary>
    public sealed class AddPerson : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AddPerson;
        /// <summary>The person's role in the story.</summary>
        public PersonKind PersonKind { get; init; }
        /// <summary>Folk key.</summary>
        public string FolkKey { get; init; } = string.Empty;
        /// <summary>Culture key.</summary>
        public string CultureKey { get; init; } = string.Empty;
        /// <summary>Index into <see cref="HistoryCatalog.Settlements"/> of the seat, or -1.</summary>
        public int SeatSettlement { get; init; } = -1;
        /// <summary>Person id (or this operator's pending handle) of the house founder, or -1.</summary>
        public int HouseId { get; init; } = -1;
        /// <summary>Person id (or this operator's pending handle) of a parent, or -1.</summary>
        public int Parent { get; init; } = -1;
        /// <summary>Grammatical gender.</summary>
        public bool Feminine { get; init; }
        /// <summary>Birth turn (an adult appointed now was born turns ago, so it may be negative).</summary>
        public int BornTurn { get; init; }
        /// <summary>Year within the birth turn.</summary>
        public int BornYear { get; init; }
        /// <summary>Personality, rolled by the proposer from <see cref="StableKey"/>.</summary>
        public ActorTraits Traits { get; init; }
        /// <summary>Command in the field, 0..100.</summary>
        public byte Martial { get; init; }
        /// <summary>Plots and spycraft, 0..100.</summary>
        public byte Intrigue { get; init; }
        /// <summary>Running a household or a realm, 0..100.</summary>
        public byte Stewardship { get; init; }
        /// <summary>Swaying people, 0..100.</summary>
        public byte Charisma { get; init; }
        /// <summary>Epithet names from the traits data (at most three are kept).</summary>
        public IReadOnlyList<string> Epithets { get; init; } = Array.Empty<string>();
        /// <summary>Starting renown.</summary>
        public int Renown { get; init; }
        /// <summary>Starting wealth.</summary>
        public double Wealth { get; init; }
        /// <summary>Starting loyalty to the ruler, -100..100.</summary>
        public sbyte Loyalty { get; init; }
        /// <summary>The key every draw about the person derives from (<see cref="ProposalSink.StableKey"/>).</summary>
        public int StableKey { get; init; }
        /// <summary>This operator's pending handle for the person (-2, -3, ...); set by <see cref="ProposalSink.ProposePerson"/>.</summary>
        public int Pending { get; internal set; }
    }

    /// <summary>Ends person <see cref="Person"/> with <see cref="Cause"/>. A person already dead (this turn included) is refused.</summary>
    public sealed class EndPerson : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.EndPerson;
        /// <summary>Person id or this operator's pending handle.</summary>
        public int Person { get; init; } = -1;
        /// <summary>Death cause name (<c>age</c>, <c>battle</c>, <c>executed</c>...).</summary>
        public string Cause { get; init; } = string.Empty;
    }

    /// <summary>
    /// Appoints <see cref="Person"/> to office <see cref="Office"/> of polity <see cref="Proposal.EntityId"/>, closing the open
    /// tenure of that office; <see cref="Person"/> -1 only vacates it. Refused for a dead polity or person, and past
    /// <see cref="HistoryContext.MaxAppointmentsPerTurn"/> (the <c>ruler</c> office is exempt). Taking the <c>ruler</c> office
    /// makes the person's kind <see cref="PersonKind.Ruler"/>. Conflicts per polity and office.
    /// </summary>
    public sealed class SetTenure : Proposal
    {
        /// <summary>The office every polity has: its ruler. Exempt from the appointment budget.</summary>
        public const string RulerOffice = "ruler";
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.SetTenure;
        /// <summary>Office name (<c>ruler</c>, <c>chancellor</c>, <c>marshal</c>... or any name data adds).</summary>
        public string Office { get; init; } = string.Empty;
        /// <summary>Person id or this operator's pending handle; -1 vacates.</summary>
        public int Person { get; init; } = -1;
        internal override long? ConflictKey => ((long)EntityId << 32) | NameHash(Office);
    }

    /// <summary>
    /// Opens a bond of <see cref="RelationKind"/> from <see cref="A"/> to <see cref="B"/>; an open bond of the same kind deepens by
    /// <see cref="Weight"/> instead. Refused for a person with itself, a dead person, or past
    /// <see cref="HistoryContext.MaxOpenRelationsPerPerson"/>.
    /// </summary>
    public sealed class AddRelation : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AddRelation;
        /// <summary>Person id or pending handle of the first party.</summary>
        public int A { get; init; } = -1;
        /// <summary>Person id or pending handle of the second party.</summary>
        public int B { get; init; } = -1;
        /// <summary>Relation kind name (<c>parent</c>, <c>spouse</c>, <c>rival</c>...).</summary>
        public string RelationKind { get; init; } = string.Empty;
        /// <summary>Strength added.</summary>
        public int Weight { get; init; } = 1;
    }

    /// <summary>Closes the open bond of <see cref="RelationKind"/> from <see cref="A"/> to <see cref="B"/>; refused when there is none.</summary>
    public sealed class EndRelation : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.EndRelation;
        /// <summary>Person id or pending handle of the first party.</summary>
        public int A { get; init; } = -1;
        /// <summary>Person id or pending handle of the second party.</summary>
        public int B { get; init; } = -1;
        /// <summary>Relation kind name.</summary>
        public string RelationKind { get; init; } = string.Empty;
    }

    /// <summary>
    /// Adds deltas to person <see cref="Person"/>. All deltas to one person in a turn are summed, then applied and clamped once
    /// (loyalty to -100..100, wealth and pledge at or above zero), so the order operators emit them in never matters.
    /// </summary>
    public sealed class AdjustPerson : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AdjustPerson;
        /// <summary>Person id or pending handle.</summary>
        public int Person { get; init; } = -1;
        /// <summary>Renown delta.</summary>
        public int Renown { get; init; }
        /// <summary>Wealth delta.</summary>
        public double Wealth { get; init; }
        /// <summary>Loyalty delta.</summary>
        public int Loyalty { get; init; }
        /// <summary>Pledged-funds delta (negative when a campaign spends them).</summary>
        public double Pledged { get; init; }
    }

    /// <summary>
    /// Records an engagement (attacker polity <see cref="Proposal.EntityId"/>) as a <see cref="HistoryBattle"/>, then appends the
    /// <see cref="EventKind"/> event that narrates it with a <c>battle:{id}</c> tag. Names (terrain, tactics, units, beats, siege
    /// end) are interned at commit. Refused for unknown polities, non-finite or negative strengths and loss shares outside 0..1.
    /// </summary>
    public sealed class AddBattle : Proposal
    {
        /// <summary>One side as proposed: names rather than interned ids, a person id or pending handle for the general.</summary>
        public sealed record Side(int Polity, int General, double Strength, double LossShare, double Supply, double Morale,
            string Tactic = "", string Unit = "");

        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AddBattle;
        /// <summary>Kind of the narrating event (<see cref="HistoryEventKind.War"/> for a field battle, <see cref="HistoryEventKind.Siege"/>...).</summary>
        public HistoryEventKind EventKind { get; init; } = HistoryEventKind.War;
        /// <summary>Tags of the narrating event; the commit adds <c>battle:{id}</c>.</summary>
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
        /// <summary>Participants of the narrating event.</summary>
        public IReadOnlyList<EventParticipant>? Participants { get; init; }
        /// <summary>Year within the turn.</summary>
        public int Year { get; init; }
        /// <summary>Season.</summary>
        public HistorySeason Season { get; init; }
        /// <summary>How it was fought.</summary>
        public BattleKind BattleKind { get; init; }
        /// <summary>Planet tile of the field.</summary>
        public int Tile { get; init; } = -1;
        /// <summary>Terrain class name, or empty.</summary>
        public string Terrain { get; init; } = string.Empty;
        /// <summary>Besieged settlement index, or -1.</summary>
        public int Settlement { get; init; } = -1;
        /// <summary>The attacking side.</summary>
        public Side Attacker { get; init; } = new(-1, -1, 0, 0, 0, 0);
        /// <summary>The defending side.</summary>
        public Side Defender { get; init; } = new(-1, -1, 0, 0, 0, 0);
        /// <summary>How it ended.</summary>
        public BattleOutcome Outcome { get; init; }
        /// <summary>Beat names in order.</summary>
        public IReadOnlyList<string> Beats { get; init; } = Array.Empty<string>();
        /// <summary>Months a siege lasted.</summary>
        public int SiegeMonths { get; init; }
        /// <summary>Siege end name, or empty.</summary>
        public string SiegeEnd { get; init; } = string.Empty;
        /// <summary>The siege this relief battle broke into, proposed the same turn, or <c>null</c>.</summary>
        public AddBattle? ParentBattle { get; init; }
    }

    /// <summary>
    /// Adds deltas to the state of route (<see cref="RouteA"/>, <see cref="RouteB"/>), creating it for a route that has road
    /// segments. All deltas to one route in a turn are summed, then applied and clamped once (safety and traffic to 0..1,
    /// forts and waystations at or above zero).
    /// </summary>
    public sealed class AdjustRoute : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AdjustRoute;
        /// <summary>Route endpoint: the smaller capital tile.</summary>
        public int RouteA { get; init; }
        /// <summary>Route endpoint: the larger capital tile.</summary>
        public int RouteB { get; init; }
        /// <summary>Safety delta.</summary>
        public double Safety { get; init; }
        /// <summary>Traffic delta.</summary>
        public double Traffic { get; init; }
        /// <summary>Forts delta.</summary>
        public int Forts { get; init; }
        /// <summary>Waystations delta.</summary>
        public int Waystations { get; init; }
    }

    /// <summary>
    /// Sets <see cref="Flag"/> of route (<see cref="RouteA"/>, <see cref="RouteB"/>) to <see cref="Value"/> (a person id or pending
    /// handle for <see cref="RouteFlag.OutlawBand"/>, a polity id for <see cref="RouteFlag.Sponsor"/>, a turn otherwise; -1 clears).
    /// Conflicts per route and flag.
    /// </summary>
    public sealed class SetRouteFlag : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.SetRouteFlag;
        /// <summary>Route endpoint: the smaller capital tile.</summary>
        public int RouteA { get; init; }
        /// <summary>Route endpoint: the larger capital tile.</summary>
        public int RouteB { get; init; }
        /// <summary>Which field.</summary>
        public RouteFlag Flag { get; init; }
        /// <summary>The new value.</summary>
        public int Value { get; init; } = -1;
        internal override long? ConflictKey => unchecked((((long)RouteA << 32) | (uint)RouteB) * -7046029254386353131L ^ (long)Flag);
    }
}
