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

    /// <summary>Opens a road segment kept by polity <see cref="Proposal.EntityId"/>. Conflicts per tile pair.</summary>
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
        internal override long? ConflictKey => ((long)Math.Min(TileA, TileB) << 32) | (uint)Math.Max(TileA, TileB);
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
        /// <summary>Planet tile of the event, or -1.</summary>
        public int Tile { get; init; } = -1;
        /// <summary>Id of an event from an earlier turn that caused this one, or -1.</summary>
        public int Cause { get; init; } = -1;
        /// <summary>Machine-readable detail.</summary>
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
        /// <summary>Entities involved and their roles.</summary>
        public IReadOnlyList<EventParticipant>? Participants { get; init; }
    }

    /// <summary>Records a historic site (a battlefield) on <see cref="Tile"/> for polity <see cref="Proposal.EntityId"/>; a tile that already has a site is left alone.</summary>
    public sealed class AddSite : Proposal
    {
        /// <inheritdoc />
        public override ProposalKind Kind => ProposalKind.AddSite;
        /// <summary>Tile the site sits on.</summary>
        public int Tile { get; init; }
        /// <summary>Geographic position of the tile.</summary>
        public GeoCoord Position { get; init; }
        internal override long? ConflictKey => Tile;
    }

    /// <summary>Leaves a <see cref="HistoryScar"/> of <see cref="ScarKind"/> on <see cref="Tile"/> for polity <see cref="Proposal.EntityId"/>; a tile that already carries that kind is left alone.</summary>
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
        internal override long? ConflictKey => Tile;
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
}
