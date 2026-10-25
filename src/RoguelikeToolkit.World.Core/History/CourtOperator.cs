using RoguelikeToolkit.World.Core.Actors;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 38, with <see cref="HistoryFeatures.Persons"/> only: the court. An observer of the legacy simulation: it reads
/// successions, treaties, villains and endings and writes persons, offices and relations, never anything the legacy operators
/// read, so turning it on leaves every legacy decision unchanged. All tuning is data (<see cref="CourtDef"/>,
/// <c>Peoples/Data/Court/default.yaml</c>; epithets in <c>Peoples/Data/Traits/epithets.yaml</c>).
/// <list type="bullet">
/// <item>Every living polity has one ruler. A ruler's reign ends on the legacy succession tick (one per folk generation,
/// <see cref="HistoryDemography.GenerationTurns"/>; none while a villain rules), when a villain rises, or at the age cap.
/// The eldest living heir succeeds, else a kinsman; a new ruler is drawn to die at the next tick at an age in
/// <see cref="CourtDef.RulerDeathAgeMin"/>..<see cref="CourtDef.RulerDeathAgeMax"/> of the lifespan.</item>
/// <item>A treaty heir is born a person; a marriage betroths the two heirs, a fostering binds the heir to the other ruler.</item>
/// <item>Offices follow the government form; a vacancy draws <see cref="CourtDef.Candidates"/> candidates and appoints the
/// fittest, or, under a ruler of low agreeableness, the first (a favourite, with a patron bond). Holders of high extraversion
/// and low agreeableness become rivals.</item>
/// <item>Everyone but rulers dies by a Gompertz hazard; nobody outlives <see cref="CourtDef.HardCap"/> lifespans. The people of
/// an ended polity leave the story (<c>exile</c>).</item>
/// </list>
/// Every draw keys on a polity id or a person's <see cref="HistoryPerson.StableKey"/>, never on a person id.
/// </summary>
public sealed class CourtOperator : IHistoryOperator
{
    /// <summary>Death cause of the people of an ended polity.</summary>
    public const string ExileCause = "exile";

    /// <summary>Death cause of a ruler a villain displaced.</summary>
    public const string OverthrownCause = "overthrown";

    /// <summary>Death cause of old age.</summary>
    public const string AgeCause = "age";

    private const int RulerSlot = 0, HeirSlot = 1, OfficeSlots = 10;

    private PeoplesStore? _store;
    private bool _loaded;
    private CourtProfile? _profile;

    /// <summary>Diagnostic name, <c>Court</c>.</summary>
    public string Name => "Court";

    /// <summary><see cref="StageOrder.Court"/> (38).</summary>
    public int Order => StageOrder.Court;

    internal CourtProfile Profile => _profile ??= CourtProfile.From(_store);

    /// <summary>Proposes the turn's deaths, successions, heirs, betrothals and appointments.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        if (!view.Has(HistoryFeatures.Persons) || view.Polities.Count == 0) return;
        if (!_loaded)
        {
            _store = view.LoadPeoples();
            _profile = null;
            _loaded = true;
        }
        var turn = new Turn(this, view, sink);
        foreach (var polity in view.Polities) turn.Succession(polity);
        foreach (var polity in view.Polities) turn.Bonds(polity);
        foreach (var polity in view.Polities) turn.Offices(polity);
    }

    /// <summary>One turn's court work: what is decided so far, so later passes see earlier ones.</summary>
    private sealed class Turn
    {
        private readonly CourtOperator _op;
        private readonly TurnView _view;
        private readonly ProposalSink _sink;
        private readonly CourtProfile _profile;
        private readonly Dictionary<int, List<TurnView.PersonState>> _living = new();
        private readonly HashSet<int> _dying = new();
        private readonly Dictionary<int, (int Person, ActorTraits Traits)> _rulers = new();
        private readonly Dictionary<int, int> _newHeirs = new();
        private readonly Dictionary<int, int> _events = new();
        private readonly Dictionary<int, int> _added = new();

        public Turn(CourtOperator op, TurnView view, ProposalSink sink)
        {
            _op = op;
            _view = view;
            _sink = sink;
            _profile = op.Profile;
            foreach (var p in view.Persons)
            {
                if (!p.Alive || p.PolityId < 0) continue;
                if (!_living.TryGetValue(p.PolityId, out var list)) _living[p.PolityId] = list = new List<TurnView.PersonState>();
                list.Add(p);
            }
        }

        private int T => _view.Turn;
        private int Years => _view.TurnYears;
        private CourtDef Data => _profile.Data;

        private IReadOnlyList<TurnView.PersonState> LivingOf(int polity)
            => _living.TryGetValue(polity, out var list) ? list : Array.Empty<TurnView.PersonState>();

        private HistoryDemography.FolkParams Folk(string folkKey) => HistoryDemography.ResolveFolk(folkKey, _op._store);

        /// <summary>Age in years at the start of <paramref name="turn"/> of someone born in absolute year <paramref name="born"/>.</summary>
        private int AgeAt(int born, int turn) => turn * Years - born;

        private static int BornAbs(TurnView.PersonState p, int years) => p.BornTurn * years + p.BornYear;

        /// <summary>True when the person would pass the age cap during this turn.</summary>
        private bool PastCap(TurnView.PersonState p)
            => AgeAt(BornAbs(p, Years), T + 1) > Data.HardCap * Folk(p.FolkKey).LifespanYears;

        private bool Event(int polity) => _events.GetValueOrDefault(polity) < 2;

        private void Narrate(TurnView.PolityState polity, HistoryEventKind kind, int person, string role, params string[] tags)
        {
            if (!Event(polity.Id)) return;
            _events[polity.Id] = _events.GetValueOrDefault(polity.Id) + 1;
            var all = new List<string>(tags.Length + 1) { $"person:{person}" };
            all.AddRange(tags);
            _sink.Event(kind, polity.Id, polity.CapitalTile, all, participants: new[] { _sink.Participant(person, ParticipantRoles.PersonPrefix + role) });
        }

        private void End(TurnView.PersonState person, string cause)
        {
            _dying.Add(person.Id);
            _sink.Emit(new Proposal.EndPerson { EntityId = person.PolityId, Person = person.Id, Cause = cause });
        }

        private string DeathCause(TurnView.PersonState person, int ageYears)
        {
            int common = Data.UntimelyDeathCauses.Count, feminine = person.Feminine ? Data.FeminineDeathCauses.Count : 0;
            if (ageYears >= Data.OldAge * Folk(person.FolkKey).LifespanYears || common + feminine == 0) return AgeCause;
            var rng = _sink.Rng(person.StableKey, "cause");
            int pick = (int)rng.NextUInt((uint)(common + feminine));
            return pick < common ? Data.UntimelyDeathCauses[pick] : Data.FeminineDeathCauses[pick - common];
        }

        private int Seat(int polity)
        {
            foreach (var s in _view.Settlements)
                if (!s.Ended && s.PolityId == polity && s.Role == SettlementRole.Capital) return s.Index;
            return -1;
        }

        /// <summary>Rolls a person of <paramref name="polity"/>'s folk and culture keyed by (polity, slot); not yet proposed.</summary>
        private Proposal.AddPerson Roll(TurnView.PolityState polity, int slot, PersonKind kind, int bornAbs, int house, int parent)
        {
            int key = _sink.StableKey(polity.Id, slot);
            var rng = _sink.Rng(key, "person");
            FolkDef? folk = null;
            CultureDef? culture = null;
            _op._store?.Folks.TryGetValue(polity.FolkKey, out folk);
            _op._store?.Cultures.TryGetValue(polity.CultureKey, out culture);
            var priors = folk?.ToTraitWeights() ?? new[] { TraitWeight.Balanced, TraitWeight.Balanced, TraitWeight.Balanced, TraitWeight.Balanced, TraitWeight.Balanced };
            var traits = CourtProfile.RollTraits(ref rng, priors, culture);
            bool feminine = rng.NextDouble() < Data.FeminineShare;
            var skills = _profile.RollSkills(ref rng, traits);
            var epithets = _profile.Epithets(ref rng, traits, skills);
            int bornTurn = (int)Math.Floor(bornAbs / (double)Years);
            return new Proposal.AddPerson
            {
                EntityId = polity.Id, PersonKind = kind, FolkKey = polity.FolkKey, CultureKey = polity.CultureKey, SeatSettlement = Seat(polity.Id),
                HouseId = house, Parent = parent, Feminine = feminine, BornTurn = bornTurn, BornYear = bornAbs - bornTurn * Years,
                Traits = traits, Martial = skills[0], Intrigue = skills[1], Stewardship = skills[2], Charisma = skills[3], Epithets = epithets,
                StableKey = key,
            };
        }

        /// <summary>An absolute birth year for someone <paramref name="minShare"/>..<paramref name="maxShare"/> of a lifespan old now, at least of age.</summary>
        private int BornAged(TurnView.PolityState polity, int slot, double minShare, double maxShare)
        {
            var folk = Folk(polity.FolkKey);
            var rng = _sink.Rng(_sink.StableKey(polity.Id, slot), "age");
            double share = minShare + (maxShare - minShare) * rng.NextDouble();
            int age = Math.Max(folk.MaturationYears, (int)Math.Round(share * folk.LifespanYears));
            return T * Years - age;
        }

        /// <summary>Ends what must end and seats a ruler on every living polity.</summary>
        public void Succession(TurnView.PolityState polity)
        {
            var living = LivingOf(polity.Id);
            if (!polity.Alive)
            {
                foreach (var p in living) End(p, ExileCause);
                return;
            }
            var folk = Folk(polity.FolkKey);
            int generation = HistoryDemography.GenerationTurns(folk.LifespanYears, Years);
            int since = T - polity.FoundedTurn;
            bool tick = !polity.HasVillain && since >= generation && since % generation == 0;
            TurnView.VillainState? villain = null;
            foreach (var v in _view.Villains)
                if (v.PolityId == polity.Id && v.RisenTurn == T - 1) villain = v;

            int rulerId = _view.OfficeHolder(polity.Id, Proposal.SetTenure.RulerOffice);
            TurnView.PersonState? ruler = rulerId >= 0 ? _view.Persons[rulerId] : null;
            foreach (var p in living)
            {
                if (p.Id == rulerId) continue;
                int age = AgeAt(BornAbs(p, Years), T);
                var deathRng = _sink.Rng(p.StableKey, "death");
                if (PastCap(p) || deathRng.NextDouble() < _profile.DeathChance(age, Folk(p.FolkKey).LifespanYears, Years))
                    End(p, DeathCause(p, age));
            }
            if (polity.HeirBornTurn == T - 1)
            {
                var rng = _sink.Rng(polity.Id, "heir");
                int born = (T - 1) * Years + (int)rng.NextUInt((uint)Years);
                int house = ruler == null ? -1 : ruler.HouseId >= 0 ? ruler.HouseId : ruler.Id;
                _newHeirs[polity.Id] = Propose(Roll(polity, HeirSlot, PersonKind.Heir, born, house, ruler?.Id ?? -1));
            }

            if (ruler != null && !tick && villain == null && !PastCap(ruler))
            {
                _rulers[polity.Id] = (ruler.Id, ruler.Traits);
                return;
            }
            if (ruler != null)
            {
                int age = AgeAt(BornAbs(ruler, Years), T);
                string cause = villain != null ? OverthrownCause : DeathCause(ruler, age);
                End(ruler, cause);
                Narrate(polity, HistoryEventKind.PersonDied, ruler.Id, "deceased", cause, "office:ruler");
            }

            // The successor: the villain, the eldest living heir, else a kinsman of the old ruler (or a founder).
            int successor;
            ActorTraits traits;
            var tags = new List<string> { "office:ruler" };
            TurnView.PersonState? heir = null;
            if (villain == null)
                foreach (var p in living)
                    if (p.Kind == PersonKind.Heir && !_dying.Contains(p.Id) && (heir == null || BornAbs(p, Years) < BornAbs(heir, Years))) heir = p;
            if (villain != null)
            {
                var v = villain.Value;
                var person = Roll(polity, RulerSlot, PersonKind.Ruler, BornAged(polity, RulerSlot, Data.OfficeAgeMin, Data.OfficeAgeMax), -1, -1);
                successor = Propose(person);
                traits = person.Traits;
                tags.Add("villain");
                tags.Add($"agenda:{v.Agenda}");
            }
            else if (heir != null)
            {
                successor = heir.Id;
                traits = heir.Traits;
                tags.Add("succession");
                if (AgeAt(BornAbs(heir, Years), T) < folk.MaturationYears) tags.Add("regency");
            }
            else
            {
                // Drawn to die at the next succession tick, at an age in the data's band (never a child on the throne).
                int nextTick = polity.HasVillain ? T + generation : polity.FoundedTurn + (since / generation + 1) * generation;
                var rng = _sink.Rng(_sink.StableKey(polity.Id, RulerSlot), "reign");
                double share = Data.RulerDeathAgeMin + (Data.RulerDeathAgeMax - Data.RulerDeathAgeMin) * rng.NextDouble();
                int born = Math.Min(nextTick * Years - (int)Math.Round(share * folk.LifespanYears), T * Years - folk.MaturationYears);
                int house = ruler == null ? -1 : ruler.HouseId >= 0 ? ruler.HouseId : ruler.Id;
                var person = Roll(polity, RulerSlot, PersonKind.Ruler, born, house, ruler?.Id ?? -1);
                successor = Propose(person);
                traits = person.Traits;
                tags.Add(ruler != null ? "kinsman" : polity.FoundedTurn == T - 1 ? "founder" : "accession");
            }
            _sink.Emit(new Proposal.SetTenure { EntityId = polity.Id, Office = Proposal.SetTenure.RulerOffice, Person = successor });
            _rulers[polity.Id] = (successor, traits);
            Narrate(polity, HistoryEventKind.PersonRose, successor, "ruler", tags.ToArray());
        }

        private int Propose(Proposal.AddPerson person)
        {
            if (person.PersonKind != PersonKind.Ruler) _added[person.EntityId] = _added.GetValueOrDefault(person.EntityId) + 1;
            return _sink.ProposePerson(person);
        }

        /// <summary>Marriages betroth the two new heirs; a fostering binds the elder party's heir to the other ruler.</summary>
        public void Bonds(TurnView.PolityState polity)
        {
            if (!polity.Alive) return;
            foreach (var e in _view.EventsOf(polity.Id))
            {
                if (e.Kind != HistoryEventKind.Treaty || e.Turn != T - 1) continue;
                int other = -1;
                foreach (var tag in e.Tags)
                    if (tag.StartsWith("party:", StringComparison.Ordinal)
                        && int.TryParse(tag.Substring(6), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int party)
                        && party != polity.Id)
                        other = party;
                if (other < 0) continue;
                if (e.Tags.Contains("marriage") && _newHeirs.TryGetValue(polity.Id, out int mine) && _newHeirs.TryGetValue(other, out int theirs))
                    _sink.Emit(new Proposal.AddRelation { EntityId = polity.Id, A = mine, B = theirs, RelationKind = "betrothed" });
                else if (e.Tags.Contains("foster") && _rulers.TryGetValue(other, out var host))
                {
                    foreach (var p in LivingOf(polity.Id))
                        if (p.Kind == PersonKind.Heir && !_dying.Contains(p.Id))
                        {
                            _sink.Emit(new Proposal.AddRelation { EntityId = polity.Id, A = p.Id, B = host.Person, RelationKind = "fostered_by" });
                            break;
                        }
                }
            }
        }

        /// <summary>Fills vacant offices of the polity's form and lets clashing holders become rivals.</summary>
        public void Offices(TurnView.PolityState polity)
        {
            if (!polity.Alive || !_rulers.TryGetValue(polity.Id, out var ruler)) return;
            var offices = _profile.OfficesOf(polity.Form);
            int room = _view.MaxLivingPersonsPerPolity - _added.GetValueOrDefault(polity.Id);
            foreach (var p in LivingOf(polity.Id))
                if (p.Kind != PersonKind.Ruler && !_dying.Contains(p.Id)) room--;
            var holders = new List<TurnView.PersonState>();
            for (int i = 0; i < offices.Count; i++)
            {
                var office = offices[i];
                int holder = _view.OfficeHolder(polity.Id, office.Name);
                if (holder >= 0 && !_dying.Contains(holder))
                {
                    holders.Add(_view.Persons[holder]);
                    continue;
                }
                if (room <= 0) continue;
                Proposal.AddPerson? chosen = null;
                bool favourite = CourtProfile.Scale(ruler.Traits.Agreeableness) < Data.FavouriteBelowAgreeableness;
                int candidates = Math.Max(1, Data.Candidates);
                for (int c = 0; c < candidates && !(favourite && chosen != null); c++)
                {
                    int slot = OfficeSlots + i * candidates + c;
                    var candidate = Roll(polity, slot, office.Kind, BornAged(polity, slot, Data.OfficeAgeMin, Data.OfficeAgeMax), -1, -1);
                    if (chosen == null || SkillOf(candidate, office.Skill) > SkillOf(chosen, office.Skill)) chosen = candidate;
                }
                int appointee = Propose(chosen!);
                room--;
                _sink.Emit(new Proposal.SetTenure { EntityId = polity.Id, Office = office.Name, Person = appointee });
                if (favourite) _sink.Emit(new Proposal.AddRelation { EntityId = polity.Id, A = ruler.Person, B = appointee, RelationKind = "patron" });
                if (favourite) Narrate(polity, HistoryEventKind.PersonRose, appointee, "appointee", $"office:{office.Name}", "favourite");
                else Narrate(polity, HistoryEventKind.PersonRose, appointee, "appointee", $"office:{office.Name}");
            }
            for (int i = 0; i < holders.Count; i++)
                for (int j = i + 1; j < holders.Count; j++)
                {
                    var a = holders[i];
                    var b = holders[j];
                    if (!Clashes(a.Traits) || !Clashes(b.Traits)) continue;
                    bool already = false;
                    foreach (var r in _view.RelationsOf(a.Id))
                        if (r.Kind == "rival" && (r.A == b.Id || r.B == b.Id)) already = true;
                    if (!already) _sink.Emit(new Proposal.AddRelation { EntityId = polity.Id, A = a.Id, B = b.Id, RelationKind = "rival" });
                }
        }

        private bool Clashes(ActorTraits traits)
            => CourtProfile.Scale(traits.Extraversion) >= Data.RivalAboveExtraversion
            && CourtProfile.Scale(traits.Agreeableness) <= Data.RivalBelowAgreeableness;

        private static int SkillOf(Proposal.AddPerson p, int skill) => skill switch
        {
            0 => p.Martial,
            1 => p.Intrigue,
            2 => p.Stewardship,
            _ => p.Charisma,
        };
    }
}
