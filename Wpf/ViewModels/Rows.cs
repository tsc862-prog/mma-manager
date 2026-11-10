using System.Text;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Core.Sim.Scouting;
using MmaManager.Wpf.Art;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// A single rating shown as a labelled bar on the profile page. Under Fog of War a
/// bar can be known exactly (the number), roughly (a band word like "Strong"), or
/// not at all ("?") — see <see cref="MmaManager.Core.Sim.Scouting"/>.
/// </summary>
public sealed class RatingBar
{
    private readonly int _value;
    private readonly StatReveal _reveal;
    private readonly StatBand _band;

    /// <summary>Always-known bar (public stats, regional fame): shows the exact value.</summary>
    public RatingBar(string name, int value) : this(name, value, StatReveal.Exact, ScoutReport.Band(value)) { }

    public RatingBar(string name, int value, StatReveal reveal, StatBand band)
    {
        Name = name; _value = value; _reveal = reveal; _band = band;
    }

    public string Name { get; }

    /// <summary>The exact value (kept for callers/tests that want the raw number).</summary>
    public int Value => _value;

    /// <summary>How full to draw the bar: the real value when known, the band's midpoint when rough, empty when hidden.</summary>
    public int BarValue => _reveal switch
    {
        StatReveal.Exact => _value,
        StatReveal.Approximate => BandMid(_band),
        _ => 0,
    };

    /// <summary>The right-hand readout: the number, a band word, or "?".</summary>
    public string ValueText => _reveal switch
    {
        StatReveal.Exact => _value.ToString(),
        StatReveal.Approximate => BandWord(_band),
        _ => "?",
    };

    /// <summary>Exact bars use the theme accent; rough bars go amber; hidden bars are dim grey.</summary>
    public Brush BarBrush => _reveal switch
    {
        StatReveal.Exact => Known,
        StatReveal.Approximate => Rough,
        _ => Hidden,
    };

    public double BarOpacity => _reveal == StatReveal.Hidden ? 0.35 : 1.0;

    /// <summary>
    /// Who has the edge on what the player can SEE: -1 = <paramref name="a"/>,
    /// 1 = <paramref name="b"/>, 0 = even or unknowable. Two exact numbers compare
    /// as numbers; otherwise the shown bands compare (an exact number reads as its
    /// band), and a hidden bar never declares an edge — so the bolding on a
    /// side-by-side never tells the player more than the two readouts do.
    /// </summary>
    public static int ShownEdge(RatingBar a, RatingBar b)
    {
        if (a._reveal == StatReveal.Hidden || b._reveal == StatReveal.Hidden) return 0;
        if (a._reveal == StatReveal.Exact && b._reveal == StatReveal.Exact)
            return a._value == b._value ? 0 : a._value > b._value ? -1 : 1;
        return a._band == b._band ? 0 : a._band > b._band ? -1 : 1;
    }

    private static int BandMid(StatBand b) => b switch
    {
        StatBand.Elite => 88, StatBand.Strong => 72, StatBand.Average => 55, _ => 35,
    };
    private static string BandWord(StatBand b) => b switch
    {
        StatBand.Elite => "Elite", StatBand.Strong => "Strong", StatBand.Average => "Avg", _ => "Weak",
    };

    private static readonly Brush Known = Frozen(Color.FromRgb(0x4F, 0x9D, 0xD6));
    private static readonly Brush Rough = Frozen(Color.FromRgb(0xC9, 0x93, 0x3E));
    private static readonly Brush Hidden = Frozen(Color.FromRgb(0x55, 0x5D, 0x66));
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

/// <summary>A named group of ratings (Striking, Grappling, …).</summary>
public sealed class RatingGroup
{
    public RatingGroup(string title, params RatingBar[] ratings) { Title = title; Ratings = ratings; }
    public string Title { get; }
    public IReadOnlyList<RatingBar> Ratings { get; }
}

/// <summary>One line of a fighter's fight history.</summary>
public sealed class FightHistoryRow
{
    /// <summary>Raw date behind <see cref="When"/> so the column sorts chronologically.</summary>
    public required DateOnly Date { get; init; }
    public required string When { get; init; }
    public required string Stars { get; init; }
    public required string Result { get; init; }
    public required string Opponent { get; init; }
    public required string Method { get; init; }
    public required string Round { get; init; }
    public required string Event { get; init; }
    /// <summary>Who staged it, and the click-through targets.</summary>
    public required string Org { get; init; }
    public required int EventId { get; init; }
    public required int OpponentId { get; init; }
}

/// <summary>One line in a fighter's trophy case: a reign, a medal, or a year-end award.</summary>
public sealed class AchievementRow
{
    public required string Glyph { get; init; }
    public required string Text { get; init; }
    public required string Sub { get; init; }
    /// <summary>A belt they hold right now (drawn gold, listed first).</summary>
    public bool IsCurrent { get; init; }
}

/// <summary>A fighter as a flat, bindable row for the roster grid, plus a profile and fight history.</summary>
public sealed class FighterRow
{
    private readonly Fighter _f;
    private readonly World _w;
    private readonly IReadOnlyDictionary<FightId, (Fight Fight, Event Event)> _fights;

    public FighterRow(Fighter f, World w, IReadOnlyDictionary<FightId, (Fight, Event)> fightIndex)
    {
        _f = f;
        _w = w;
        _fights = fightIndex;
    }

    /// <summary>
    /// The player's SCOUTED read of a fighter's overall — what every screen shows
    /// instead of the truth (fog of war). Your own roster reads near-exact; an
    /// unproven outsider can be misjudged by up to ~10 points, consistently (the
    /// same deterministic error the AI orgs live with — see ScoutingService).
    /// </summary>
    public static int Scouted(World w, Fighter f) =>
        w.Get(w.PlayerPromotion) is { } mine
            ? MmaManager.Core.Sim.Scouting.ScoutingService.PerceivedAbility(w, mine, f)
            : FighterAbility.CurrentAbility(f);

    /// <summary>The fighter's portrait (see <see cref="Portraits"/> for the style choice).</summary>
    public ImageSource Portrait => Portraits.For(_f, _w);

    /// <summary>The matching full-body shot (profile hero image).</summary>
    public ImageSource BodyShot => Portraits.Body(_f, _w);

    /// <summary>Logo of the fighter's current promotion (null for free agents).</summary>
    public ImageSource? OrgLogo => _f.Contract is { } c && _w.Get(c.Promotion) is { } p ? LogoFactory.ForPromotion(p) : null;

    public string Name => _f.FullName;
    public int Age => _f.AgeOn(_w.CurrentDate);
    public string Division => DivisionLabel(_f.WeightClass, _f.Sex);
    public string Style => Pretty(_f.PrimaryStyle.ToString());
    public int Ovr => Scouted(_w, _f);
    public string Record => _f.Record.ToString();
    public int RecordSort => RecordKey(_f.Record);
    public string Status => _f.Status.ToString();
    public int Momentum => _f.Momentum;
    public string Form => _f.Momentum >= 20 ? $"▲ {_f.Momentum}" : _f.Momentum <= -20 ? $"▼ {_f.Momentum}" : _f.Momentum.ToString();
    public bool Hot => _f.Momentum >= 20;
    public bool Cold => _f.Momentum <= -20;
    public string Org => _f.Contract is { } c ? _w.Get(c.Promotion)?.Abbreviation ?? "FA" : "FA";

    // --- profile-page fields ------------------------------------------------
    public int Id => _f.Id.Value;
    /// <summary>The underlying model, for front-office actions (retention, marketing).</summary>
    public Fighter Model => _f;
    public string Nickname => _f.Nickname is null ? "" : $"“{_f.Nickname}”";
    public string Bio => $"{Division}   ·   {Style}   ·   {Hometown}   ·   {_f.Stance}{CampLine}";

    /// <summary>Where they train — camps drive development, so the room is part of the bio.</summary>
    private string CampLine => _f.Camp is { } c && _w.Get(c) is { } camp
        ? $"   ·   {camp.Name}{(_f.CampStipend ? " (stipend)" : "")}" : "";

    /// <summary>
    /// The people in their life — family, friends, mentors, and live bad blood.
    /// This is the relationship graph made visible: who they came up with, who
    /// they answer to, and who they can't stand.
    /// </summary>
    public string BondsText
    {
        get
        {
            var lines = new List<string>();
            foreach (var r in _w.Relationships.Where(r => r.Involves(_f.Id)).Take(5))
            {
                if (_w.Get(r.Other(_f.Id)) is not { } other) continue;
                lines.Add(r.Kind switch
                {
                    RelationshipKind.Family => r.Note is not null && r.B == _f.Id
                        ? $"👪  {r.Note}"
                        : $"👪  family — {other.FullName}",
                    RelationshipKind.Mentor => r.A == _f.Id
                        ? $"🧭  mentor to {other.FullName}"
                        : $"🧭  {r.Note ?? $"trains under {other.FullName}"}",
                    _ => $"🤝  friends with {other.FullName}" + (r.Note is not null ? $" ({r.Note})" : ""),
                });
            }
            foreach (var rv in _w.Rivalries.Where(rv => rv.Involves(_f.Id))
                         .OrderByDescending(rv => rv.Intensity).Take(3))
                if (_w.Get(rv.Other(_f.Id)) is { } enemy)
                    lines.Add($"⚔  bad blood with {enemy.FullName} ({HeatWord(rv.Intensity)})");
            return string.Join("\n", lines);
        }
    }
    public bool HasBonds => BondsText.Length > 0;

    private static string HeatWord(int intensity) => intensity switch
    {
        >= 80 => "a war",
        >= 60 => "personal",
        >= 40 => "heated",
        _ => "simmering",
    };

    /// <summary>A generated scouting-report paragraph.</summary>
    public string Story => Biographer.Fighter(_w, _f);
    public string Physicals => $"Age {Age}    ·    {_f.HeightCm} cm    ·    {_f.ReachCm} cm reach";
    public int Morale => _f.Morale.Value;
    public string Finishes => $"{_f.Record.WinsByKo} KO   ·   {_f.Record.WinsBySubmission} SUB   ·   {_f.Record.WinsByDecision} DEC";
    /// <summary>
    /// Honour icons: 👑 reigning champion, 🏅 Fighter of the Year, 💥 Fight/KO/Sub
    /// of the Year, 🌟 Rookie of the Year, 🥇 special-event medal.
    /// </summary>
    public string Badges
    {
        get
        {
            var b = new StringBuilder();
            if (_w.Titles.Values.Any(t => t.CurrentChampion == _f.Id)) b.Append("👑");
            if (MmaManager.Core.Sim.Org.Franchise.IsFace(_w, _f)) b.Append("📣");   // the face of their company
            if (_f.StarPower) b.Append("✨");                                       // the rare it-factor
            if (_f.Accolades.Any(a => a.Title.Contains("Fighter of the Year"))) b.Append("🏅");
            if (_f.Accolades.Any(a => a.Title.Contains("Fight of the Year")
                                      || a.Title.Contains("Knockout of the Year")
                                      || a.Title.Contains("Submission of the Year"))) b.Append("💥");
            if (_f.Accolades.Any(a => a.Title.Contains("Rookie of the Year"))) b.Append("🌟");
            if (_f.Accolades.Any(a => a.Title.EndsWith("gold") || a.Title.EndsWith("silver") || a.Title.EndsWith("bronze")))
                b.Append("🥇");
            return b.ToString();
        }
    }

    /// <summary>Career honours, newest first, for the profile page ("" = none).</summary>
    public string AccoladesText => string.Join("\n",
        _f.Accolades.OrderByDescending(a => a.Date).Select(a => $"🏆 {a.Title}"));

    public string StatusLine => $"{Status}   ·   Morale {Morale}   ·   Momentum {Momentum}"
        + $"   ·   {MmaManager.Core.Sim.Generation.Personality.Label(_f.Disposition)}"
        + (_f.PersonaTag is { } tag ? $"   ·   {PersonaLabel(tag)}" : "")
        + (_f.StarPower ? "   ·   ✨ Star power" : "")
        + (MmaManager.Core.Sim.Org.Franchise.IsFace(_w, _f) ? "   ·   📣 Face of the company" : "");

    // The marketable look baked into the portrait, as a profile-page chip.
    private static string PersonaLabel(string tag) => tag switch
    {
        "glam" => "★ Cover Star",
        "adonis" => "★ Cover Athlete",
        "menace" => "★ The Menace",
        "inked" => "★ Inked Showman",
        "babyface" => "★ Babyface",
        _ => $"★ {tag}",
    };
    public string ContractText => _f.Contract is { } k
        ? $"{k.PursePerFight} / fight (+{k.WinBonus} win)  ·  {k.FightsRemaining} bouts left"
        : "Free agent";

    /// <summary>Who holds their paper ("Apex Fighting Championship (AFC) — International promotion").</summary>
    public string CompanyLine => _f.Contract is { } c && _w.Get(c.Promotion) is { } p
        ? $"{p.Name} ({p.Abbreviation})  ·  {MmaManager.Core.Sim.Org.OrgSizing.Label(MmaManager.Core.Sim.Org.OrgSizing.SizeOf(p))} promotion"
        : _f.Status == FighterStatus.Retired ? "Retired from competition" : "Unsigned — on the open market";

    /// <summary>The rest of the deal's terms (clauses, bonuses, when it was inked).</summary>
    public string ContractDetail => _f.Contract is { } k
        ? string.Join("  ·  ", new[]
          {
              $"signed {k.SignedOn:MMM yyyy}",
              k.IsExclusive ? "exclusive" : "non-exclusive",
              k.HasChampionsClause ? "champion's clause" : null,
              k.Guaranteed ? "guaranteed money" : null,
              k.SigningBonus.Amount > 0 ? $"{k.SigningBonus} signing bonus" : null,
              k.PerformanceBonus.Amount > 0 ? $"{k.PerformanceBonus} performance bonus" : null,
              k.PpvPoints > 0 ? $"{k.PpvPoints} PPV point{(k.PpvPoints == 1 ? "" : "s")}" : null,
          }.Where(t => t is not null))
        : "";

    /// <summary>The current promotion's id for the click-through (0 = none).</summary>
    public int CompanyId => _f.Contract is { } cc ? cc.Promotion.Value : 0;
    public bool HasCompany => CompanyId != 0;

    // The promotions they actually fought under, read off the taped record.
    private string? _careerOrgs;
    /// <summary>Career stints ("Fought under:  SCL '19-'22  →  CK '22-'24  →  AFC '24-") — "" when there's nothing beyond the current org.</summary>
    public string CareerOrgs => _careerOrgs ??= BuildCareerOrgs();

    private string BuildCareerOrgs()
    {
        var stints = new List<(int Promo, string Abbr, int First, int Last)>();
        foreach (var id in _f.BoutHistory)
        {
            if (!_fights.TryGetValue(id, out var entry)) continue;
            var ev = entry.Event;
            string abbr = _w.Get(ev.Promotion)?.Abbreviation ?? "?";
            if (stints.Count > 0 && stints[^1].Promo == ev.Promotion.Value)
                stints[^1] = stints[^1] with { Last = ev.Date.Year };
            else
                stints.Add((ev.Promotion.Value, abbr, ev.Date.Year, ev.Date.Year));
        }
        if (stints.Count == 0) return "";
        // One org for the whole career and still signed there: the company line says it all.
        if (stints.Count == 1 && _f.Contract?.Promotion.Value == stints[0].Promo) return "";
        return "Fought under:  " + string.Join("  →  ", stints.Select(st =>
            st.First == st.Last ? $"{st.Abbr} '{st.First % 100:00}" : $"{st.Abbr} '{st.First % 100:00}-'{st.Last % 100:00}"));
    }

    // --- The trophy case: title reigns (from every belt's lineage) + honours --
    private IReadOnlyList<AchievementRow>? _achievements;
    public IReadOnlyList<AchievementRow> CareerAchievements => _achievements ??= BuildAchievements();
    public bool HasAchievements => CareerAchievements.Count > 0;

    private IReadOnlyList<AchievementRow> BuildAchievements()
    {
        var rows = new List<(DateOnly When, bool Current, AchievementRow Row)>();

        foreach (var t in _w.Titles.Values)
            foreach (var reign in t.Lineage.Where(r => r.Champion == _f.Id))
            {
                string org = _w.Get(t.Promotion)?.Abbreviation ?? "?";
                if (reign.LostOn is null)
                    rows.Add((reign.WonOn, true, new AchievementRow
                    {
                        Glyph = "👑", Text = t.Name,
                        Sub = $"reigning champion since {reign.WonOn:MMM yyyy}  ·  {reign.Defenses} defense{(reign.Defenses == 1 ? "" : "s")}  ·  {org}",
                        IsCurrent = true,
                    }));
                else
                    rows.Add((reign.WonOn, false, new AchievementRow
                    {
                        Glyph = "🏆", Text = t.Name,
                        Sub = $"{reign.WonOn:MMM yyyy} – {reign.LostOn:MMM yyyy}  ·  {reign.Defenses} defense{(reign.Defenses == 1 ? "" : "s")}  ·  {org}",
                    }));
            }

        foreach (var a in _f.Accolades)
            rows.Add((a.Date, false, new AchievementRow
            {
                Glyph = a.Title.Contains("Fighter of the Year") ? "🏅"
                      : a.Title.Contains("Rookie of the Year") ? "🌟"
                      : a.Title.Contains("of the Year") ? "💥"
                      : a.Title.EndsWith("gold") ? "🥇"
                      : a.Title.EndsWith("silver") ? "🥈"
                      : a.Title.EndsWith("bronze") ? "🥉"
                      : "🎖",
                Text = a.Title,
                Sub = a.Date.ToString("MMM yyyy"),
            }));

        return rows.OrderByDescending(r => r.Current).ThenByDescending(r => r.When)
                   .Select(r => r.Row).ToList();
    }

    // Fog of War: what the player has earned the right to see, computed from this
    // fighter's tape (and cached — the profile reads it several times).
    private ScoutReport? _scout;
    private ScoutReport Scout => _scout ??= BuildScoutReport();

    private ScoutReport BuildScoutReport()
    {
        var bouts = new List<BoutObservation>();
        foreach (var id in _f.BoutHistory)
            if (_fights.TryGetValue(id, out var pair) && pair.Fight.Outcome is { } o
                && pair.Event.Date >= _w.StartDate)   // pre-save bouts are record, not tape — the fog holds on day one
                bouts.Add(BoutObservation.FromOutcome(_f.Id, o, pair.Fight.ScheduledRounds));
        bool mine = _f.Contract is { } c && c.Promotion == _w.PlayerPromotion;
        // Your scouts + the camp grapevine: your fighters' campmates get seen daily
        // by people you talk to, and a sponsored camp's room is yours to watch.
        double scouting = MmaManager.Core.Sim.Org.StaffService.ScoutingBonus(_w.Get(_w.PlayerPromotion))
                        + MmaManager.Core.Sim.Living.Camps.FamiliarityBonus(_w, _f);
        return ScoutingService.Assess(_f, mine, bouts, scouting);
    }

    // A fogged bar: the value is real, but how much of it shows depends on the channel.
    private RatingBar Bar(string name, Rating rating, StatChannel channel)
    {
        int v = rating.Value;
        return new RatingBar(name, v, Scout.Precision(channel), ScoutReport.Band(v));
    }

    public IReadOnlyList<RatingGroup> RatingGroups => new[]
    {
        new RatingGroup("Striking",
            Bar("Punch Pow", _f.Striking.PunchPower, StatChannel.Power), Bar("Punch Tech", _f.Striking.PunchTechnique, StatChannel.Striking),
            Bar("Kick Pow", _f.Striking.KickPower, StatChannel.Power), Bar("Kick Tech", _f.Striking.KickTechnique, StatChannel.Striking),
            Bar("Clinch Str", _f.Striking.ClinchStriking, StatChannel.Clinch), Bar("Str Defense", _f.Striking.StrikingDefense, StatChannel.StrikingDef),
            Bar("Footwork", _f.Striking.Footwork, StatChannel.Striking)),
        new RatingGroup("Grappling",
            Bar("TD Offense", _f.Grappling.TakedownOffense, StatChannel.Wrestling), Bar("TD Defense", _f.Grappling.TakedownDefense, StatChannel.WrestlingDef),
            Bar("Top Control", _f.Grappling.TopControl, StatChannel.Wrestling), Bar("Bottom", _f.Grappling.BottomGame, StatChannel.Ground),
            Bar("Sub Offense", _f.Grappling.SubmissionOffense, StatChannel.Submission), Bar("Sub Defense", _f.Grappling.SubmissionDefense, StatChannel.SubmissionDef),
            Bar("Scrambling", _f.Grappling.Scrambling, StatChannel.Ground), Bar("Clinch Ctrl", _f.Grappling.ClinchControl, StatChannel.Clinch)),
        new RatingGroup("Physical",
            Bar("Strength", _f.Physical.Strength, StatChannel.Athletic), Bar("Cardio", _f.Physical.Cardio, StatChannel.Cardio), Bar("Speed", _f.Physical.Speed, StatChannel.Athletic),
            Bar("Chin", _f.Physical.Chin, StatChannel.Chin), Bar("Recovery", _f.Physical.Recovery, StatChannel.Cardio), Bar("Flexibility", _f.Physical.Flexibility, StatChannel.Ground)),
        new RatingGroup("Mental",
            Bar("Fight IQ", _f.Mental.FightIQ, StatChannel.Mind), Bar("Composure", _f.Mental.Composure, StatChannel.Chin),
            Bar("Heart", _f.Mental.Heart, StatChannel.Chin), Bar("Discipline", _f.Mental.Discipline, StatChannel.Mind)),
        // Intangibles + tendencies are public reputation, never fogged.
        new RatingGroup("Intangibles",
            new("Popularity", _f.Intangibles.Popularity), new("Charisma", _f.Intangibles.Charisma)),
        new RatingGroup("Tendencies",
            new("Strike Pref", _f.Tendencies.StrikingPreference), new("Aggression", _f.Tendencies.Aggression),
            new("Pace", _f.Tendencies.Pace), new("Counter", _f.Tendencies.Counterfighting),
            new("Finish", _f.Tendencies.FinishInstinct)),
    };

    /// <summary>
    /// One fogged rating by its profile label ("Punch Pow", "Chin") — exactly the
    /// bar the profile draws, so other screens (the booking tape) show the same
    /// scout's read instead of the true number.
    /// </summary>
    public RatingBar ScoutedRating(string name) =>
        RatingGroups.SelectMany(g => g.Ratings).First(r => r.Name == name);

    /// <summary>A one-line summary of how well this fighter is scouted, for the profile header.</summary>
    public string ScoutingLine
    {
        get
        {
            if (Scout.CareerBouts == 0) return "Unproven — no pro record to go on yet";
            var channels = new[]
            {
                StatChannel.Power, StatChannel.Striking, StatChannel.StrikingDef, StatChannel.Clinch,
                StatChannel.Wrestling, StatChannel.WrestlingDef, StatChannel.Ground, StatChannel.Submission,
                StatChannel.SubmissionDef, StatChannel.Cardio, StatChannel.Chin, StatChannel.Athletic, StatChannel.Mind,
            };
            int exact = channels.Count(c => Scout.Precision(c) == StatReveal.Exact);
            int pct = (int)System.Math.Round(100.0 * exact / channels.Length);
            string tag = pct >= 100 ? "Fully scouted" : pct >= 60 ? "Well scouted" : pct >= 25 ? "Partially scouted" : "Lightly scouted";
            string tape = Scout.ObservedFights == 0 ? "record only" : $"{Scout.ObservedFights} bout{(Scout.ObservedFights == 1 ? "" : "s")} on tape";
            return $"{tag} · {pct}% exact · {tape}";
        }
    }

    /// <summary>How well-known the fighter is in each region (0–100, derived from cities), shown as bars.</summary>
    public IReadOnlyList<RatingBar> RegionalFame => Enum.GetValues<Region>()
        .Select(r => new RatingBar(Pretty(r.ToString()), MmaManager.Core.Sim.Geography.Reputation.FighterInRegion(_w, _f, r)))
        .ToList();

    /// <summary>The fighter's real nationality ("Georgia").</summary>
    public string Nation => _w.Get(_f.Country)?.Name ?? Pretty(_f.Nationality.ToString());

    /// <summary>
    /// Hometown line for the profile ("Tbilisi, Georgia"). A fighter whose gym
    /// city is abroad shows both ("Moscow · Georgia"); no hometown shows just
    /// the nationality.
    /// </summary>
    public string Hometown => _f.HomeCity is { } hc && _w.Get(hc) is { } city
        ? city.Country == _f.Country ? $"{city.Name}, {Nation}" : $"{city.Name} · {Nation}"
        : Nation;

    /// <summary>The fighter's simulated bouts, most recent first.</summary>
    public IReadOnlyList<FightHistoryRow> History => BuildHistory();

    /// <summary>
    /// The record's off-the-books remainder, spelled out. A pro's listed fights
    /// can trail the raw tally — club-circuit moonlighting between contracts and
    /// a discovered local's pre-discovery club record are real wins that no
    /// commission taped. One line reconciles the number so nothing on the
    /// profile is a mystery ("" when every bout is listed).
    /// </summary>
    public string UnlistedBouts
    {
        get
        {
            int w = 0, l = 0, other = 0;
            foreach (var id in _f.BoutHistory)
            {
                if (!_fights.TryGetValue(id, out var entry) || entry.Fight.Outcome is not { } o) continue;
                if (o.Winner == _f.Id) w++;
                else if (o.Winner is null) other++;
                else l++;
            }
            int uw = _f.Record.Wins - w;
            int ul = _f.Record.Losses - l;
            int uo = _f.Record.Draws + _f.Record.NoContests - other;
            int total = uw + ul + uo;
            if (total <= 0 || uw < 0 || ul < 0 || uo < 0) return "";
            return $"…plus {uw}-{ul}{(uo > 0 ? $"-{uo}" : "")} across {total} club-circuit bout{(total == 1 ? "" : "s")} no commission taped.";
        }
    }

    private IReadOnlyList<FightHistoryRow> BuildHistory()
    {
        var rows = new List<FightHistoryRow>();
        // BoutHistory is chronological; show the most recent fight first.
        for (int i = _f.BoutHistory.Count - 1; i >= 0; i--)
        {
            if (!_fights.TryGetValue(_f.BoutHistory[i], out var entry)) continue;
            var (fight, ev) = entry;
            if (fight.Outcome is not { } o) continue;

            var oppId = fight.RedCorner == _f.Id ? fight.BlueCorner : fight.RedCorner;
            string result = fight.OverturnedNc ? "NC"
                : o.Winner == _f.Id ? "Win"
                : o.Winner is null ? (o.Method == FightMethod.NoContest ? "NC" : "Draw")
                : "Loss";

            rows.Add(new FightHistoryRow
            {
                Date = ev.Date,
                When = ev.Date.ToString("d MMM yyyy"),
                Stars = MmaManager.Core.Sim.Flavor.MatchRating.StarsFor(fight),
                Result = result + (fight.IsTitleFight ? " ★" : ""),
                Opponent = _w.Get(oppId)?.FullName ?? "?",
                Method = fight.OverturnedNc ? $"{Pretty(o.Method.ToString())} — overturned" : Pretty(o.Method.ToString()),
                Round = $"R{o.EndRound} {o.EndTimeSeconds / 60}:{o.EndTimeSeconds % 60:00}",
                Event = ev.Name,
                Org = _w.Get(ev.Promotion)?.Abbreviation ?? "?",
                EventId = ev.Id.Value,
                OpponentId = oppId.Value,
            });
        }
        return rows;
    }

    public static string Pretty(string enumName)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < enumName.Length; i++)
        {
            if (i > 0 && char.IsUpper(enumName[i]) && !char.IsUpper(enumName[i - 1])) sb.Append(' ');
            sb.Append(enumName[i]);
        }
        return sb.ToString();
    }

    /// <summary>A weight class labelled with sex — men's is the bare name, women's is prefixed.</summary>
    public static string DivisionLabel(WeightClass wc, Sex sex) =>
        sex == Sex.Female ? $"Women's {Pretty(wc.ToString())}" : Pretty(wc.ToString());

    /// <summary>
    /// Sort key for a "12-3" record column (grids sort the display string
    /// lexically otherwise, putting 9-0 above 10-2): wins first, losses break ties.
    /// </summary>
    public static int RecordKey(FightRecord r) => r.Wins * 1000 - r.Losses;
}

/// <summary>A division choice (weight + sex) for the booking and rankings pickers.</summary>
public sealed record DivisionPick(WeightClass WeightClass, Sex Sex)
{
    public string Label => FighterRow.DivisionLabel(WeightClass, Sex);
    public override string ToString() => Label;
}

/// <summary>A promotion summary row for the dashboard.</summary>
public sealed class PromotionRow
{
    public PromotionRow(Promotion p, World w)
    {
        Abbreviation = p.Abbreviation;
        Name = p.Name;
        Prestige = p.Prestige.Value;
        Roster = w.RosterOf(p.Id).Count(f => f.Status != FighterStatus.Retired);
        Balance = p.Balance.ToString();
        IsPlayer = p.IsPlayerControlled;
        Logo = LogoFactory.ForPromotion(p);
    }

    public ImageSource Logo { get; }
    public string Abbreviation { get; }
    public string Name { get; }
    public int Prestige { get; }
    public int Roster { get; }
    public string Balance { get; }
    public bool IsPlayer { get; }
}

/// <summary>
/// Billing-order naming for a fight card. The sim runs <c>Event.Card</c> front
/// to back (prelims first, <c>Card[^1]</c> last = the main event); every
/// player-facing list shows the card top-billed instead — main event first.
/// </summary>
public static class CardBilling
{
    /// <summary>Slot name for the bout at <paramref name="index"/> in a card of <paramref name="total"/>.</summary>
    public static string Slot(int index, int total) =>
        index == total - 1 ? "Main event"
        : index == total - 2 && total >= 2 ? "Co-main"
        : $"Fight {total - index}";

    /// <summary>Billing position (1 = main event).</summary>
    public static int Position(int index, int total) => total - index;
}

/// <summary>A ranking row (champion or contender) for the rankings screen.</summary>
public sealed class RankRow
{
    public RankRow(string rank, Fighter f, World w)
    {
        Rank = rank;
        // "C" (champion) tops the list, then #1, #2, …; anything else sinks.
        RankSort = rank == "C" ? 0
            : int.TryParse(rank.TrimStart('#'), out int n) ? n
            : int.MaxValue;
        FighterId = f.Id.Value;
        Name = f.FullName;
        Record = f.Record.ToString();
        RecordSort = FighterRow.RecordKey(f.Record);
        Ovr = FighterRow.Scouted(w, f);
        Age = f.AgeOn(w.CurrentDate);
    }

    public int FighterId { get; }
    public string Rank { get; }
    public int RankSort { get; }
    public string Name { get; }
    public string Record { get; }
    public int RecordSort { get; }
    public int Ovr { get; }
    public int Age { get; }
}
