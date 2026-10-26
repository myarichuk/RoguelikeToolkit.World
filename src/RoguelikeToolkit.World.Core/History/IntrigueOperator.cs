using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 75, with <see cref="HistoryFeatures.Intrigue"/> only: loyalty at court and plots between rivals. All tuning is data
/// (<c>Peoples/Data/Intrigue/default.yaml</c>). The court's lean on war and treaties (<see cref="TradeProfile.Hawk"/>) is read
/// by <see cref="WarOperator"/> and <see cref="TreatyOperator"/>.
/// <list type="bullet">
/// <item>Every office holder's loyalty drifts toward <c>LoyaltyBase + PatronLoyalty [patron of the ruler] − RivalLoyalty ×
/// rivalries + AgreeablenessLoyalty × (A − 50)</c>. A general whose loyalty falls under <see cref="BattleDef.BetrayalBelow"/>
/// betrays the host in battle.</item>
/// <item>A polity whose court holds one of <c>PlotOffices</c> (its spymaster, else a councillor, seer, elder...) plots, with
/// <c>PlotChance × Intrigue/100</c> a turn, against the bordering rival it holds the heaviest grudge against. Every covert act
/// has a sponsor and an operative who can be caught later:
/// <c>sponsorship</c> pays the band working a road of the rival's (or, with outlaws on, hires one onto it); the band raids and,
/// with <c>SponsoredSabotage</c> a turn, cuts its road on the sponsor's account (<see cref="OutlawOperator"/>), and the sponsor
/// is named when a raid is traced or the hanged leader confesses. <c>spy_network</c> plants an agent
/// (<see cref="PersonKind.Agent"/>, serving the sponsor, seated in the rival's capital) that runs <c>AgentOps</c>: sabotage
/// (the road costs as severed on the logistics network), a bribe (the rival's general's loyalty), an assassination, a theft
/// from the rival's richest merchant. Each operation risks capture (<c>ExposeBase + ExposeSpy × (rival's spy − agent's
/// Intrigue)/100</c>), and counter-intelligence may uncover an idle agent; a caught agent is executed and names the sponsor, a
/// grudge and a cause for war.</item>
/// </list>
/// </summary>
public sealed class IntrigueOperator : IHistoryOperator
{
    /// <summary>Death cause of an assassinated person.</summary>
    public const string AssassinatedCause = "assassinated";

    /// <summary>Death cause of a caught agent.</summary>
    public const string ExecutedCause = "executed";

    /// <summary>Death cause (leaving the story) of an agent whose seat fell or ended.</summary>
    public const string RecalledCause = "recalled";

    private PeoplesStore? _store;
    private bool _loaded;
    private TradeProfile? _profile;

    /// <summary>Diagnostic name, <c>Intrigue</c>.</summary>
    public string Name => "Intrigue";

    /// <summary><see cref="StageOrder.Intrigue"/> (75).</summary>
    public int Order => StageOrder.Intrigue;

    internal TradeProfile Profile => _profile ??= TradeProfile.From(_store);

    /// <summary>Proposes the turn's loyalty drift and plots.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        if (!view.Has(HistoryFeatures.Intrigue) || view.Polities.Count == 0) return;
        if (!_loaded)
        {
            _store = view.LoadPeoples();
            _profile = null;
            _loaded = true;
        }
        var p = Profile;
        var routes = TradeProfile.Routes(view);
        foreach (var polity in view.Polities)
        {
            if (!polity.Alive) continue;
            Loyalty(view, sink, p, polity);
            Plot(view, sink, p, polity, routes);
            Agents(view, sink, p, polity, routes);
        }
    }

    private static void Loyalty(TurnView view, ProposalSink sink, TradeProfile p, TurnView.PolityState polity)
    {
        var d = p.Intrigue;
        int ruler = view.OfficeHolder(polity.Id, Proposal.SetTenure.RulerOffice);
        foreach (var office in p.Court.OfficesOf(polity.Form))
        {
            int holder = view.OfficeHolder(polity.Id, office.Name);
            if (holder < 0 || holder == ruler || !view.Persons[holder].Alive) continue;
            var person = view.Persons[holder];
            double target = d.LoyaltyBase + d.AgreeablenessLoyalty * (CourtProfile.Scale(person.Traits.Agreeableness) - 50);
            foreach (var r in view.RelationsOf(holder))
            {
                if (r.Kind == "patron" && (r.A == ruler || r.B == ruler)) target += d.PatronLoyalty;
                else if (r.Kind == "rival") target -= d.RivalLoyalty;
            }
            target = Math.Max(-100, Math.Min(100, target));
            int delta = (int)Math.Round(d.LoyaltyDrift * (target - person.Loyalty));
            if (delta != 0) sink.Emit(new Proposal.AdjustPerson { EntityId = polity.Id, Person = holder, Loyalty = delta });
        }
    }

    private void Plot(TurnView view, ProposalSink sink, TradeProfile p, TurnView.PolityState polity, List<TradeProfile.Route> routes)
    {
        var d = p.Intrigue;
        int spymaster = p.Plotter(view, polity.Id);
        if (spymaster < 0) return;
        int rival = -1, heaviest = 0;
        foreach (int q in view.BorderingPolities(polity.Id))
        {
            int g = view.GrudgeWeight(polity.Id, q);
            if (g > heaviest && !view.IsWindowed(polity.Id, q)) { heaviest = g; rival = q; }
        }
        if (rival < 0) return;
        double spy = view.Persons[spymaster].Intrigue;
        var rng = sink.Rng(polity.Id, "plot");
        double roll = rng.NextDouble(), pick = rng.NextDouble();
        if (roll >= d.PlotChance * spy / 100.0) return;

        // What can be started against the rival this turn: a band to pay (or hire), a spy network to plant.
        TradeProfile.Route? sponsorable = null, hireable = null;
        bool outlaws = view.Has(HistoryFeatures.Outlaws);
        int bands = 0;
        foreach (var r in routes) if (r.Band >= 0 && view.Persons[r.Band].Alive) bands++;
        bool room = view.Persons.Count < view.MaxPersons - p.Outlaws.PersonReserve;
        foreach (var r in routes)
        {
            bool touches = (r.KeeperA == rival || r.KeeperB == rival) && r.KeeperA != polity.Id && r.KeeperB != polity.Id;
            if (!touches) continue;
            if (sponsorable == null && r.Band >= 0 && r.Sponsor < 0 && view.Persons[r.Band].Alive) sponsorable = r;
            if (hireable == null && outlaws && room && (r.Band < 0 || !view.Persons[r.Band].Alive) && bands < view.MaxOutlawBands) hireable = r;
        }
        var agents = TradeProfile.PersonsOf(view, polity.Id, PersonKind.Agent);
        int seat = Capital(view, rival);
        bool planted = false;
        foreach (var a in agents) planted |= a.SeatSettlement == seat;
        int living = 0;
        foreach (var other in view.PersonsOf(polity.Id)) if (other.Kind != PersonKind.Ruler) living++;
        bool canPlant = seat >= 0 && !planted && agents.Count < d.MaxAgents && room
            && living < view.MaxLivingPersonsPerPolity - p.Commerce.CourtReserve;
        var options = new List<(string Plot, double Weight)>();
        foreach (var (plot, weight) in p.Plots)
        {
            bool can = plot switch
            {
                "sponsorship" => sponsorable != null || hireable != null,
                "spy_network" => canPlant,
                _ => false,
            };
            if (can && weight > 0) options.Add((plot, weight));
        }
        string? chosen = Pick(options, pick);
        if (chosen == null) return;
        bool creates = chosen == "spy_network" || sponsorable == null; // an agent, or a hired band
        if (creates && !sink.TrySpendEmergence()) return;

        var tags = new List<string> { $"plot:{chosen}", $"actor:{polity.Id}", $"target:{rival}", "secret" };
        var parts = new List<EventParticipant> { sink.Participant(spymaster, ParticipantRoles.PersonPrefix + "spymaster") };
        int tile = view.Polities[rival].CapitalTile;
        if (chosen == "spy_network")
        {
            int key = sink.StableKey(polity.Id, 700 + rival);
            int born = TradeProfile.BornAged(view, sink, _store, key, polity.FolkKey, d.AgentAgeMin, d.AgentAgeMax);
            int agent = sink.ProposePerson(p.Court.RollPerson(sink, _store, key, polity.Id, polity.FolkKey, polity.CultureKey, seat, PersonKind.Agent,
                born, -1, -1, view.TurnYears));
            sink.Emit(new Proposal.AddRelation { EntityId = polity.Id, A = agent, B = spymaster, RelationKind = "agent_of" });
            tags.Add($"person:{agent}");
            parts.Add(sink.Participant(agent, ParticipantRoles.PersonPrefix + "agent"));
            sink.Event(HistoryEventKind.Intrigue, polity.Id, tile, tags, participants: parts);
            return; // the agent's operations, and its capture, are its own
        }
        // Pay the band that works the rival's road, else hire one onto it.
        var road = sponsorable ?? hireable!;
        int band = road.Band;
        if (sponsorable == null)
        {
            int key = sink.StableKey(polity.Id, 500 + rival);
            int born = TradeProfile.BornAged(view, sink, _store, key, polity.FolkKey, p.Outlaws.AgeMin, p.Outlaws.AgeMax);
            band = sink.ProposePerson(p.Court.RollPerson(sink, _store, key, -1, polity.FolkKey, polity.CultureKey, -1, PersonKind.Outlaw,
                born, -1, -1, view.TurnYears));
            sink.Emit(new Proposal.SetRouteFlag { EntityId = polity.Id, RouteA = road.Key.A, RouteB = road.Key.B, Flag = RouteFlag.OutlawBand, Value = band });
            tags.Add("hired");
        }
        sink.Emit(new Proposal.SetRouteFlag { EntityId = polity.Id, RouteA = road.Key.A, RouteB = road.Key.B, Flag = RouteFlag.Sponsor, Value = polity.Id });
        tags.Add($"person:{band}");
        tags.Add($"route:{road.Key.A}-{road.Key.B}");
        sink.Event(HistoryEventKind.Intrigue, polity.Id, tile, tags, participants: parts);
        // Exposed only when the band raids or its leader hangs (OutlawOperator).
    }

    private static string? Pick(List<(string Name, double Weight)> options, double roll)
    {
        if (options.Count == 0) return null;
        double at = roll * options.Sum(o => o.Weight);
        foreach (var (name, weight) in options)
        {
            if (at < weight) return name;
            at -= weight;
        }
        return options[options.Count - 1].Name;
    }

    private static int Capital(TurnView view, int polity)
    {
        foreach (var s in view.Settlements)
            if (!s.Ended && s.PolityId == polity && s.Role == SettlementRole.Capital) return s.Index;
        return -1;
    }

    /// <summary>
    /// Every agent of <paramref name="polity"/> works the rival whose seat it sits in: an operation with <c>AgentOpChance × (0.5 +
    /// Intrigue/100)</c> (sabotage, a bribe, an assassination, a theft), each one a risk of capture; an idle agent may still be
    /// uncovered. A caught agent is executed and names its sponsor. An agent whose seat fell to its own sponsor, or ended, is
    /// recalled.
    /// </summary>
    private static void Agents(TurnView view, ProposalSink sink, TradeProfile p, TurnView.PolityState polity, List<TradeProfile.Route> routes)
    {
        var d = p.Intrigue;
        foreach (var agent in TradeProfile.PersonsOf(view, polity.Id, PersonKind.Agent))
        {
            if ((uint)agent.SeatSettlement >= (uint)view.Settlements.Count) continue;
            var seat = view.Settlements[agent.SeatSettlement];
            int rival = seat.PolityId;
            if (seat.Ended || rival == polity.Id || (uint)rival >= (uint)view.Polities.Count || !view.Polities[rival].Alive)
            {
                sink.Emit(new Proposal.EndPerson { EntityId = polity.Id, Person = agent.Id, Cause = RecalledCause });
                continue;
            }
            var rng = sink.Rng(agent.StableKey, "op");
            double act = rng.NextDouble(), pick = rng.NextDouble(), who = rng.NextDouble(), success = rng.NextDouble(), expose = rng.NextDouble();
            double theirs = p.Spy(view, rival);
            var tags = new List<string> { $"actor:{polity.Id}", $"target:{rival}", $"agent:{agent.Id}" };
            var parts = new List<EventParticipant> { sink.Participant(agent.Id, ParticipantRoles.PersonPrefix + "agent") };
            if (act >= d.AgentOpChance * (0.5 + agent.Intrigue / 100.0))
            {
                if (expose < d.AgentDiscovery * theirs / 100.0) Caught(sink, d, polity.Id, rival, agent.Id, seat.Tile, new List<string>(tags) { "plot:discovered" }, parts);
                continue;
            }

            // What the agent can reach.
            TradeProfile.Route? road = null;
            foreach (var r in routes)
                if ((r.Keeper >= 0 ? r.Keeper : r.KeeperA) == rival && !r.Severed && r.Sabotaged < view.Turn - 1) { road = r; break; }
            int general = -1;
            var targets = new List<int>();
            int theirRuler = view.OfficeHolder(rival, Proposal.SetTenure.RulerOffice);
            foreach (var office in p.Court.OfficesOf(view.Polities[rival].Form))
            {
                int holder = view.OfficeHolder(rival, office.Name);
                if (holder < 0 || holder == theirRuler || !view.Persons[holder].Alive) continue;
                targets.Add(holder);
                if (office.Kind == PersonKind.General && general < 0) general = holder;
            }
            TurnView.PersonState? mark = null;
            if (view.Has(HistoryFeatures.Commerce))
                foreach (var m in TradeProfile.PersonsOf(view, rival, PersonKind.MerchantPrince))
                    if (m.Wealth > 0 && (mark == null || m.Wealth > mark.Wealth)) mark = m;
            var options = new List<(string, double)>();
            foreach (var (op, weight) in p.AgentOps)
            {
                bool can = op switch
                {
                    "sabotage" => road != null,
                    "bribe" => general >= 0,
                    "assassination" => targets.Count > 0,
                    "theft" => mark != null,
                    _ => false,
                };
                if (can && weight > 0) options.Add((op, weight));
            }
            string? chosen = Pick(options, pick);
            if (chosen == null) continue;
            tags.Add($"plot:{chosen}");
            Proposal? deed = null; // a killing the event must not outlive (the victim may die otherwise this turn)
            switch (chosen)
            {
                case "sabotage":
                    sink.Emit(new Proposal.SetRouteFlag { EntityId = polity.Id, RouteA = road!.Key.A, RouteB = road.Key.B, Flag = RouteFlag.SabotagedTurn, Value = view.Turn });
                    tags.Add($"route:{road.Key.A}-{road.Key.B}");
                    break;
                case "bribe":
                    sink.Emit(new Proposal.AdjustPerson { EntityId = polity.Id, Person = general, Loyalty = -d.BribeLoyalty });
                    tags.Add($"person:{general}");
                    parts.Add(sink.Participant(general, ParticipantRoles.PersonPrefix + "bribed"));
                    break;
                case "theft":
                {
                    double take = mark!.Wealth * d.TheftShare;
                    sink.Emit(new Proposal.AdjustPerson { EntityId = rival, Person = mark.Id, Wealth = -take });
                    sink.Emit(new Proposal.AdjustPerson { EntityId = polity.Id, Person = agent.Id, Wealth = take });
                    tags.Add($"person:{mark.Id}");
                    parts.Add(sink.Participant(mark.Id, ParticipantRoles.PersonPrefix + "merchant"));
                    break;
                }
                default:
                {
                    int victim = targets[Math.Min(targets.Count - 1, (int)(who * targets.Count))];
                    tags.Add($"person:{victim}");
                    parts.Add(sink.Participant(victim, ParticipantRoles.PersonPrefix + "victim"));
                    bool killed = success < d.AssassinBase + d.AssassinSpy * (agent.Intrigue - theirs) / 100.0;
                    if (killed) sink.Emit(deed = new Proposal.EndPerson { EntityId = rival, Person = victim, Cause = AssassinatedCause });
                    tags.Add(killed ? "killed" : "failed");
                    break;
                }
            }
            if (expose < d.ExposeBase + d.ExposeSpy * (theirs - agent.Intrigue) / 100.0) Caught(sink, d, polity.Id, rival, agent.Id, seat.Tile, tags, parts);
            else
            {
                tags.Add("secret");
                if (deed != null) sink.EventIfApplied(deed, HistoryEventKind.Intrigue, polity.Id, seat.Tile, tags, participants: parts);
                else sink.Event(HistoryEventKind.Intrigue, polity.Id, seat.Tile, tags, participants: parts);
            }
        }
    }

    /// <summary>
    /// The rival takes the agent: executed, its sponsor named (a grudge, a cause for war). The grudge and the event wait on the
    /// execution: an agent dead of other causes this turn was never caught.
    /// </summary>
    private static void Caught(ProposalSink sink, IntrigueDef d, int sponsor, int rival, int agent, int tile, List<string> tags, List<EventParticipant> parts)
    {
        var execution = new Proposal.EndPerson { EntityId = sponsor, Person = agent, Cause = ExecutedCause };
        sink.Emit(execution);
        if (d.ExposedGrudge > 0) sink.Emit(new Proposal.AddGrudge { EntityId = rival, Other = sponsor, Weight = d.ExposedGrudge, Requires = execution });
        tags.Add("exposed");
        tags.Add("caught");
        sink.EventIfApplied(execution, HistoryEventKind.Intrigue, sponsor, tile, tags, participants: parts);
    }
}
