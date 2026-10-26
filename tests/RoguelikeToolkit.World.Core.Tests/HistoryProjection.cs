using System.Security.Cryptography;
using System.Text;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// A schema-independent fingerprint of a history catalog: every simulation fact, with interned ids written as their names
/// and event ids renumbered by rank among the projected events. A schema bump, new interned names or new catalog lists
/// leave it unchanged; only a change to what the legacy simulation decided moves it. Raw <see cref="HistoryCatalog.ToBytes"/>
/// hashes pin the encoding separately.
/// </summary>
internal static class HistoryProjection
{
    /// <summary>The last event kind of the pre-expansion simulation; later kinds are expansion detail.</summary>
    public const HistoryEventKind LastLegacyKind = HistoryEventKind.Plugin;

    /// <summary>SHA-256 of <see cref="Write"/> over events of kinds up to <paramref name="lastKind"/>.</summary>
    public static string Hash(HistoryCatalog h, HistoryEventKind lastKind = LastLegacyKind)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) Write(w, h, lastKind);
        return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
    }

    public static void Write(BinaryWriter w, HistoryCatalog h, HistoryEventKind lastKind)
    {
        string Name(int id) => id < 0 ? "" : h.Strings.NameOf(id);

        w.Write(h.Seed);
        w.Write(h.SimulatedTurns);
        w.Write(h.Seeds.Count);
        foreach (var p in h.Seeds) { w.Write(p.FolkKey); w.Write(p.CultureKey); }

        w.Write(h.Scalars.Count);
        for (int i = 0; i < h.Scalars.Count; i++) w.Write(h.Scalars[i].Name);
        w.Write(h.Polities.Count);
        foreach (var p in h.Polities)
        {
            w.Write(p.Id); w.Write((int)p.Form); w.Write(p.CapitalTile); w.Write(p.FolkKey); w.Write(p.CultureKey);
            w.Write(p.FoundedTurn); w.Write(p.Population); w.Write(p.Pressure); w.Write(p.CultureOverlay ?? "");
            w.Write(p.RulerGeneration); w.Write(p.OpennessDrift); w.Write(p.HeirBornTurn); w.Write(p.HasVillain);
            w.Write(p.EndedTurn); w.Write(Name(p.EndCause)); w.Write(p.Parent); w.Write(p.AbsorbedInto);
            for (int k = 0; k < h.Scalars.Count; k++) w.Write(p.GetScalar(k, h.Scalars));
        }
        w.Write(h.Ownership.Length);
        foreach (int o in h.Ownership) w.Write(o);

        var projected = h.Events.Where(e => e.Kind <= lastKind).ToList();
        var rank = new Dictionary<int, int>();
        for (int i = 0; i < projected.Count; i++) rank[projected[i].Id] = i;
        w.Write(projected.Count);
        foreach (var e in projected)
        {
            w.Write(e.Kind.ToString()); w.Write(e.PluginKind); w.Write(e.Turn); w.Write(e.PolityId); w.Write(e.TileIndex);
            w.Write(e.Tags.Count);
            foreach (var t in e.Tags) w.Write(t);
            // A cause outside the projection (an expansion event) reads as -2, so the legacy chain stays comparable.
            w.Write(e.Cause < 0 ? -1 : rank.TryGetValue(e.Cause, out int c) ? c : -2);
            w.Write((int)e.Importance);
            w.Write(e.Participants.Count);
            foreach (var part in e.Participants) { w.Write(part.EntityId); w.Write(Name(part.Role)); }
        }
        w.Write(h.DroppedEvents);
        w.Write(h.Compactions);

        w.Write(h.Settlements.Count);
        foreach (var s in h.Settlements)
        {
            w.Write(s.TileIndex); w.Write(s.PolityId); w.Write(s.FoundedTurn); w.Write(s.Position.Latitude); w.Write(s.Position.Longitude);
            w.Write(s.Habitability); w.Write(s.Active); w.Write(s.EndedTurn); w.Write(s.EndCause); w.Write((int)s.Tier);
            w.Write((int)s.Role); w.Write(s.Parent); w.Write(s.Population);
        }
        w.Write(h.Displaced.Count);
        foreach (var g in h.Displaced) { w.Write(g.PolityId); w.Write(g.Culture); w.Write(g.Turn); w.Write(g.Amount); w.Write(g.Origin); w.Write(g.Cause < 0 ? -1 : rank.GetValueOrDefault(g.Cause, -2)); }
        w.Write(h.PopulationLost);
        w.Write(h.Sites.Count);
        foreach (var s in h.Sites) { w.Write(s.TileIndex); w.Write(s.PolityId); w.Write(s.FoundedTurn); w.Write(s.Active); }
        w.Write(h.Roads.Count);
        foreach (var r in h.Roads)
        {
            w.Write(r.TileA); w.Write(r.TileB); w.Write(r.PolityId); w.Write(r.RouteA); w.Write(r.RouteB);
            w.Write(r.FoundedTurn); w.Write(r.Severed); w.Write(r.SeveredTurn);
        }
        w.Write(h.Grudges.Count);
        foreach (var g in h.Grudges) { w.Write(g.A); w.Write(g.B); w.Write(g.Weight); w.Write(g.FirstTurn); w.Write(g.LastTurn); }
        w.Write(h.Treaties.Count);
        foreach (var t in h.Treaties) { w.Write(t.A); w.Write(t.B); w.Write(t.UntilTurn); }
        w.Write(h.Faiths.Count);
        foreach (var f in h.Faiths)
        {
            w.Write(f.PolityId); w.Write(f.Form);
            w.Write(string.Join("|", f.Powers)); w.Write(string.Join("|", f.MythSlots));
        }
        w.Write(h.HolySites.Count);
        foreach (var s in h.HolySites) { w.Write(s.TileIndex); w.Write(s.PolityId); w.Write(s.FoundedTurn); w.Write(s.Active); }
        w.Write(h.Scars.Count);
        foreach (var s in h.Scars) { w.Write(s.TileIndex); w.Write(s.PolityId); w.Write(s.FoundedTurn); w.Write(s.Kind); w.Write(s.Cause); w.Write(s.Active); }
        w.Write(h.Villains.Count);
        foreach (var v in h.Villains) { w.Write(v.Id); w.Write(v.PolityId); w.Write(v.FolkKey); w.Write(v.RisenTurn); w.Write(v.Agenda); }
        foreach (var (key, version, data) in h.PluginState.Entries()) { w.Write(key); w.Write(version); w.Write(Convert.ToHexString(data)); }

        w.Write(h.Timeline.BaseOwnership.Length);
        foreach (int o in h.Timeline.BaseOwnership) w.Write(o);
        w.Write(h.Timeline.Changes.Count);
        foreach (var c in h.Timeline.Changes) { w.Write(c.Turn); w.Write(c.Tile); w.Write(c.Owner); }
        w.Write(h.Timeline.Samples.Count);
        foreach (var s in h.Timeline.Samples)
        {
            w.Write(s.Turn); w.Write(s.PolityId); w.Write(s.Population);
            foreach (double v in s.Scalars) w.Write(v);
        }
    }
}
