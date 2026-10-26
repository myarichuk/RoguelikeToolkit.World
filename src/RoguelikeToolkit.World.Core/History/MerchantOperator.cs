using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 78, with <see cref="HistoryFeatures.Commerce"/> only: merchant princes. All tuning is data
/// (<c>Peoples/Data/Commerce/default.yaml</c>).
/// <list type="bullet">
/// <item>A town or city (<c>MerchantTier</c>) on an active road or the coast gains a merchant prince
/// (<see cref="PersonKind.MerchantPrince"/>, seated there) with <c>MerchantChance</c> a turn, at most
/// <c>MaxMerchantsPerSettlement</c> per settlement and <c>MaxMerchantsPerPolity</c> per polity, leaving <c>CourtReserve</c> of the
/// polity's living places to the court.</item>
/// <item>A merchant gains <c>WealthGrowth × traffic × (0.5 + Stewardship/100)</c> a turn from the busiest road near its seat.
/// While its polity nurses a grudge of at least <c>PledgeGrudge</c> against a neighbour, an unpledged merchant pledges
/// <c>PledgeShare</c> of its wealth above <c>PledgeFloor</c>; the polity's next war spends the pledges on mercenaries (<see cref="WarOperator"/>).</item>
/// <item>A trading merchant (not an office holder) poorer than <c>RuinBelow</c> is ruined (with outlaws on, <see cref="OutlawOperator"/> decides its end).</item>
/// </list>
/// </summary>
public sealed class MerchantOperator : IHistoryOperator
{
    private PeoplesStore? _store;
    private bool _loaded;
    private TradeProfile? _profile;

    /// <summary>Diagnostic name, <c>Merchant</c>.</summary>
    public string Name => "Merchant";

    /// <summary><see cref="StageOrder.Merchant"/> (78).</summary>
    public int Order => StageOrder.Merchant;

    internal TradeProfile Profile => _profile ??= TradeProfile.From(_store);

    /// <summary>Proposes the turn's merchant wealth, pledges, ruins and new merchants.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        if (!view.Has(HistoryFeatures.Commerce) || view.Polities.Count == 0) return;
        if (!_loaded)
        {
            _store = view.LoadPeoples();
            _profile = null;
            _loaded = true;
        }
        var p = Profile;
        var c = p.Commerce;
        var routes = TradeProfile.Routes(view);
        var vectors = view.Map.DataStore.GetTileVectors();
        // Each standing settlement's road: the busiest active road near it (-1: none near).
        var access = new double[view.Settlements.Count];
        foreach (var s in view.Settlements)
        {
            access[s.Index] = -1;
            if (s.Ended) continue;
            foreach (var r in p.RoutesNear(vectors, routes, s.Tile))
                if (!r.Severed) access[s.Index] = Math.Max(access[s.Index], r.Traffic);
        }
        foreach (var polity in view.Polities)
        {
            if (!polity.Alive) continue;
            var merchants = TradeProfile.PersonsOf(view, polity.Id, PersonKind.MerchantPrince);
            bool grudge = false;
            foreach (int q in view.BorderingPolities(polity.Id))
                if (view.GrudgeWeight(polity.Id, q) >= Math.Max(1, c.PledgeGrudge)) { grudge = true; break; }
            foreach (var m in merchants)
            {
                if (m.Wealth < c.RuinBelow && !p.HoldsOffice(view, polity.Id, m.Id))
                {
                    if (!view.Has(HistoryFeatures.Outlaws))
                        sink.Emit(new Proposal.EndPerson { EntityId = polity.Id, Person = m.Id, Cause = OutlawOperator.RuinedCause });
                    continue;
                }
                double traffic = (uint)m.SeatSettlement < (uint)access.Length ? Math.Max(0, access[m.SeatSettlement]) : 0;
                double growth = c.WealthGrowth * traffic * (0.5 + m.Stewardship / 100.0);
                double pledge = grudge && m.Pledged <= 0 ? c.PledgeShare * Math.Max(0, m.Wealth + growth - c.PledgeFloor) : 0;
                if (growth > 0 || pledge > 0)
                    sink.Emit(new Proposal.AdjustPerson { EntityId = polity.Id, Person = m.Id, Wealth = growth - pledge, Pledged = pledge });
            }
            Rise(view, sink, p, polity, merchants, access);
        }
    }

    private void Rise(TurnView view, ProposalSink sink, TradeProfile p, TurnView.PolityState polity, List<TurnView.PersonState> merchants,
        double[] access)
    {
        var c = p.Commerce;
        if (merchants.Count >= c.MaxMerchantsPerPolity || view.Persons.Count >= view.MaxPersons - c.PersonReserve) return;
        int living = 0;
        foreach (var other in view.PersonsOf(polity.Id))
            if (other.Kind != PersonKind.Ruler) living++;
        if (living >= view.MaxLivingPersonsPerPolity - c.CourtReserve) return;
        TurnView.SettlementState? seat = null;
        foreach (var s in view.Settlements)
        {
            if (s.Ended || s.PolityId != polity.Id || s.Tier < p.MerchantTier) continue;
            int here = 0;
            foreach (var m in merchants) if (m.SeatSettlement == s.Index) here++;
            if (here >= c.MaxMerchantsPerSettlement) continue;
            if (access[s.Index] < 0 && !TradeProfile.Coastal(view, s.Tile)) continue;
            if (seat == null || s.Tier > seat.Value.Tier) seat = s; // the largest, the oldest of equals
        }
        if (seat == null) return;
        var rng = sink.Rng(polity.Id, "merchant");
        if (rng.NextDouble() >= c.MerchantChance || !sink.TrySpendEmergence()) return;
        int key = sink.StableKey(seat.Value.Index, merchants.Count);
        int born = TradeProfile.BornAged(view, sink, _store, key, polity.FolkKey, c.AgeMin, c.AgeMax);
        var person = p.Court.RollPerson(sink, _store, key, polity.Id, polity.FolkKey, polity.CultureKey, seat.Value.Index, PersonKind.MerchantPrince,
            born, -1, -1, view.TurnYears);
        person = new Proposal.AddPerson
        {
            EntityId = person.EntityId, PersonKind = person.PersonKind, FolkKey = person.FolkKey, CultureKey = person.CultureKey,
            SeatSettlement = person.SeatSettlement, HouseId = person.HouseId, Parent = person.Parent, Feminine = person.Feminine,
            BornTurn = person.BornTurn, BornYear = person.BornYear, Traits = person.Traits, Martial = person.Martial, Intrigue = person.Intrigue,
            Stewardship = person.Stewardship, Charisma = person.Charisma, Epithets = person.Epithets, StableKey = person.StableKey,
            Wealth = c.MerchantWealth,
        };
        int id = sink.ProposePerson(person);
        sink.Event(HistoryEventKind.PersonRose, polity.Id, seat.Value.Tile,
            new[] { $"person:{id}", "merchant", $"settlement:{seat.Value.Index}" },
            participants: new[] { sink.Participant(id, ParticipantRoles.PersonPrefix + "merchant") });
    }
}
