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
