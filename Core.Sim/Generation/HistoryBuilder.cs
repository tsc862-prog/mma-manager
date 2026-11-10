using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// Turns every fighter's generated win/loss tally into a REAL career: back-dated
/// completed events with opponents, methods, dates and statlines, all living in
/// <see cref="World.Events"/> like any card the sim runs. The record on the tin
/// and the history behind it finally agree — so the profile's fight-history
/// table is full on day one, résumé rankings judge real results, rematch/trilogy
/// hype can fire, title lineages predate the save, and event numbering continues
/// a promotion's actual past instead of starting at 1.
///
/// Opponents come from three pools, most credible first: roster peers in the
/// same division (mutually consistent — the loss appears on the other career
/// too), retired journeyman pros minted into the world with real names, and the
/// unnamed local scene ("Local Striker") for lesser fighters' early bouts — the
/// same filler a small org books during gameplay. Designed lore bouts (the
/// Ozols–Urbonas wars, Mercer's decision over Seo, Tanner losing the AFC belt)
/// are pinned by hand so the authored bios match the data.
///
/// Deterministic from the world seed. Runs once at world build, after rosters,
/// contracts and belts exist.
/// </summary>
public static class HistoryBuilder
{
    private const ulong SaltCareer = 0xCA5E_ED15UL;
    private const ulong SaltPair = 0x9A1E_DB00UL;
    private const ulong SaltMint = 0x0FF5_C00FUL;
    private const ulong SaltEvent = 0xE7E1_7DA7UL;

    // How far into a career the current org plausibly reaches (the last stretch
    // was fought on the current contract; before that, the regional ladder).
    private const double CurrentOrgFrac = 0.60;
    private const double MidOrgFrac = 0.30;
    // Roster-vs-roster pairing only makes sense for the recent, shared stretch.
    private const double PairableFrac = 0.55;

    /// <summary>One planned career bout on a fighter's timeline.</summary>
    private sealed class Slot
    {
        public required Career Career { get; init; }
        public required int Index { get; init; }
        public DateOnly Date;
        public char Result;                 // 'W','L','D','N'
        public FightMethod? WinMethod;      // concrete method for 'W' slots
        public bool Taken;                  // consumed by a planned bout
        public Promotion? PinnedHost;       // must be staged by this promotion
        public Title? Title;                // non-null => a title fight for this belt
        public Fighter F => Career.Fighter;
    }

    private sealed class Career
    {
        public required Fighter Fighter { get; init; }
        public List<Slot> Slots { get; } = new();
        public int Paired;
        public int Count => Slots.Count;
    }

    /// <summary>A fully resolved historical bout waiting to be staged on a card.</summary>
    private sealed class Planned
    {
        public required Slot Anchor { get; init; }   // the subject's slot ('W': anchor won)
        public Slot? Partner { get; init; }          // roster opponent's 'L' slot (shares the bout)
        public Fighter? Minted { get; init; }        // journeyman / local opponent
        public required Promotion Host { get; init; }
        public DateOnly Date;
        public required FightMethod Method { get; init; }
        public bool IsTitle;
        public Title? Title;
        public Fight? Fight;                          // set once staged

        public Fighter Opponent => Partner?.F ?? Minted!;
    }

    private sealed class ReignSpec
    {
        public required Title Title { get; init; }
        public required Slot WinSlot { get; init; }        // the current champ's belt-winning bout
        public int Defenses;
        public (Fighter Champ, Slot WinSlot)? Prior;       // dethroned/vacated predecessor
        public bool PriorVacated;                          // predecessor left without losing it in the cage
    }

    public static void Build(World world, DateOnly startDate, PromotionId? exclude = null)
    {
        var gen = FighterGenerator.ForWorld(world);

        // --- 1. every fighter's tally becomes a dated, ordered result sequence ---
        var careers = new Dictionary<FighterId, Career>();
        foreach (var f in world.Fighters.Values
                     .Where(f => !f.LocalTalent && f.Record.TotalBouts > 0)
                     .OrderBy(f => f.Id.Value))
            careers[f.Id] = BuildCareer(world, f, startDate);

        var planned = new List<Planned>();
        var meetings = new Dictionary<(int, int), int>();

        // --- 2. the authored lore bouts, pinned before anything else claims slots ---
        var (reigns, vacatedPriors) = PinStories(world, careers, planned, meetings, startDate);

        // --- 3. every current champion's reign becomes real title fights ---
        PinChampions(world, careers, reigns, vacatedPriors, exclude);

        // --- 4. roster peers share the recent bouts (both records stay true) ---
        PairRosters(world, careers, planned, meetings, exclude);

        // --- 5. everything left is fought against minted opposition ---
        FillRemainder(world, careers, planned, gen, startDate, exclude);

        // --- 6. bouts become completed events on the calendar ---
        StageEvents(world, planned);

        // --- 7. records, streaks, bout history and lineages derive from the bouts ---
        Finalize(world, careers, planned, reigns);
    }

    // =========================================================================
    // 1. Career timelines
    // =========================================================================

    private static Career BuildCareer(World world, Fighter f, DateOnly startDate)
    {
        var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)f.Id.Value ^ SaltCareer));
        var rec = f.Record;
        int n = rec.TotalBouts;
        var career = new Career { Fighter = f };

        // Dates: walk backwards from a recent last bout toward an 18-year-old debut.
        var last = startDate.AddDays(-rng.Range(38, 170));
        var earliest = f.BirthDate.AddYears(18).AddDays(45);
        int avail = Math.Max(80, last.DayNumber - earliest.DayNumber);
        double avgGap = Math.Clamp(avail / (double)n, 72, 290);
        var dates = new DateOnly[n];
        var cursor = last;
        for (int i = n - 1; i >= 0; i--)
        {
            dates[i] = cursor;
            int gap = Math.Max(63, (int)Math.Round(avgGap * (0.72 + rng.NextDouble() * 0.62)));
            cursor = cursor.AddDays(-gap);
        }
        if (dates[0] < earliest)
        {
            // Debut fell before 18: compress the early career into the legal span.
            double scale = (last.DayNumber - earliest.DayNumber) / (double)(last.DayNumber - dates[0].DayNumber);
            for (int i = 0; i < n; i++)
                dates[i] = last.AddDays(-(int)Math.Round((last.DayNumber - dates[i].DayNumber) * scale));
            for (int i = 1; i < n; i++)                      // keep months distinct after compression
                if (dates[i] < dates[i - 1].AddDays(63)) dates[i] = dates[i - 1].AddDays(63);
            if (dates[n - 1] > startDate.AddDays(-35))
            {
                int shift = dates[n - 1].DayNumber - (startDate.DayNumber - 35);
                for (int i = 0; i < n; i++) dates[i] = dates[i].AddDays(-shift);
            }
        }

        // Results: honour the streak on the tail, then shuffle the rest of the tally.
        int w = rec.Wins, l = rec.Losses, d = rec.Draws, nc = rec.NoContests;
        bool isChampion = world.Titles.Values.Any(t => t.CurrentChampion == f.Id);
        int s = rec.CurrentStreak;
        int tail = s > 0 ? Math.Min(s, w) : 0;
        char tailChar = 'W';
        if (s < 0) { tail = Math.Min(-s, l); tailChar = 'L'; }
        // A reigning champion's belt sits on their trailing wins; give a reign
        // some length unless the design pinned the streak explicitly.
        if (isChampion && tailChar == 'W' && !HasAuthoredStreak(f))
            tail = Math.Min(w, Math.Max(tail, rng.Range(3, 6)));
        if (tailChar == 'W') w -= tail; else l -= tail;

        var results = new List<char>();
        // The bout just before a win streak must be the one that broke it.
        char? breaker = null;
        if (tail > 0 && n > tail)
        {
            if (tailChar == 'W' && l + d + nc > 0)
            {
                Span<double> bw = stackalloc double[] { l, d, nc };
                breaker = "LDN"[rng.WeightedIndex(bw)];
                if (breaker == 'L') l--; else if (breaker == 'D') d--; else nc--;
            }
            else if (tailChar == 'L' && w > 0) { breaker = 'W'; w--; }
        }
        for (int i = 0; i < w; i++) results.Add('W');
        for (int i = 0; i < l; i++) results.Add('L');
        for (int i = 0; i < d; i++) results.Add('D');
        for (int i = 0; i < nc; i++) results.Add('N');
        for (int i = results.Count - 1; i > 0; i--)          // Fisher–Yates off the seeded stream
        {
            int j = rng.Next(0, i + 1);
            (results[i], results[j]) = (results[j], results[i]);
        }
        if (breaker is { } b) results.Add(b);
        for (int i = 0; i < tail; i++) results.Add(tailChar);

        // Win methods: spend the generator's KO/sub/dec split across the win slots.
        var methods = new List<FightMethod>();
        for (int i = 0; i < rec.WinsByKo; i++)
            methods.Add(rng.Chance(0.32) ? FightMethod.Knockout : FightMethod.TechnicalKnockout);
        for (int i = 0; i < rec.WinsBySubmission; i++)
            methods.Add(rng.Chance(0.85) ? FightMethod.SubmissionTap : FightMethod.TechnicalSubmission);
        for (int i = 0; i < rec.WinsByDecision; i++)
            methods.Add(RollDecision(rng));
        while (methods.Count < rec.Wins) methods.Add(RollDecision(rng));   // DQ wins etc. fall back to decisions
        for (int i = methods.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (methods[i], methods[j]) = (methods[j], methods[i]);
        }

        int mi = 0;
        for (int i = 0; i < n; i++)
        {
            career.Slots.Add(new Slot
            {
                Career = career,
                Index = i,
                Date = dates[i],
                Result = results[i],
                WinMethod = results[i] == 'W' && mi < methods.Count ? methods[mi++] : null,
            });
        }
        return career;
    }

    private static bool HasAuthoredStreak(Fighter f) =>
        f.Designed && DesignedRoster.All.FirstOrDefault(x => x.First == f.FirstName && x.Last == f.LastName)?.Streak is not null;

    private static FightMethod RollDecision(SimRandom rng)
    {
        Span<double> ww = stackalloc double[] { 0.62, 0.26, 0.12 };
        return rng.WeightedIndex(ww) switch
        {
            0 => FightMethod.UnanimousDecision,
            1 => FightMethod.SplitDecision,
            _ => FightMethod.MajorityDecision,
        };
    }

    // =========================================================================
    // 2. The authored lore bouts
    // =========================================================================

    private static (List<ReignSpec>, Dictionary<TitleId, (Fighter, Slot)>) PinStories(
        World world, Dictionary<FighterId, Career> careers,
        List<Planned> planned, Dictionary<(int, int), int> meetings, DateOnly startDate)
    {
        var reigns = new List<ReignSpec>();
        var vacatedPriors = new Dictionary<TitleId, (Fighter, Slot)>();

        Fighter? ByLast(string last) => world.Fighters.Values
            .Where(x => x.Designed && x.LastName == last)
            .OrderBy(x => x.Id.Value).FirstOrDefault();
        Promotion? ByAbbr(string abbr) => world.Promotions.Values
            .FirstOrDefault(p => p.Abbreviation == abbr);
        Career? Of(Fighter? f) => f is not null && careers.TryGetValue(f.Id, out var c) ? c : null;

        // A pinned bout between two designed fighters: the winner's chosen win slot
        // sets the date, the loser's nearest free loss slot joins it.
        Planned? Pin(string winnerLast, string loserLast, string hostAbbr, double yearsAgo,
            FightMethod method, bool fromTail = false, Title? title = null)
        {
            var wf = ByLast(winnerLast); var lf = ByLast(loserLast);
            var host = ByAbbr(hostAbbr);
            var wc = Of(wf); var lc = Of(lf);
            if (wf is null || lf is null || host is null || wc is null || lc is null) return null;

            var target = startDate.AddDays(-(int)(yearsAgo * 365));
            var tailStart = TailStart(wc);
            IEnumerable<Slot> WinCandidates() => wc.Slots
                .Where(x => x.Result == 'W' && !x.Taken && x.Title is null
                            && (fromTail ? x.Index >= tailStart : x.Index < tailStart));

            // Search winner-slot candidates from closest-to-target outward, and for
            // each take the loser's nearest loss that can legally share the date —
            // the story lands as close to "two years ago" as both careers allow.
            Slot? ws = null, ls = null;
            foreach (var w in WinCandidates().OrderBy(x => Math.Abs(x.Date.DayNumber - target.DayNumber)))
            {
                ls = lc.Slots
                    .Where(x => x.Result == 'L' && !x.Taken && Feasible(lc, x, w.Date))
                    .OrderBy(x => Math.Abs(x.Date.DayNumber - w.Date.DayNumber))
                    .FirstOrDefault();
                if (ls is not null) { ws = w; break; }
            }
            // No loss could move to any win date: try the other direction — move a
            // win to a loss date instead. A sparse loss column (a 16-2 contender)
            // can't always meet the winner's calendar halfway.
            if (ws is null)
            {
                foreach (var l in lc.Slots
                             .Where(x => x.Result == 'L' && !x.Taken)
                             .OrderBy(x => Math.Abs(x.Date.DayNumber - target.DayNumber)))
                {
                    ws = WinCandidates()
                        .Where(x => Feasible(wc, x, l.Date))
                        .OrderBy(x => Math.Abs(x.Date.DayNumber - l.Date.DayNumber))
                        .FirstOrDefault();
                    if (ws is not null) { ws.Date = l.Date; ls = l; break; }
                }
            }
            if (ws is null || ls is null) return null;

            ls.Date = ws.Date;
            ws.Taken = ls.Taken = true;
            if (title is not null) { ws.Title = title; ws.PinnedHost = host; }
            var p = new Planned
            {
                Anchor = ws, Partner = ls, Host = host, Date = ws.Date,
                Method = method, IsTitle = title is not null, Title = title,
            };
            planned.Add(p);
            Met(meetings, wf.Id, lf.Id);
            // The feud predates the save — its clock starts at the first meeting.
            foreach (var r in world.Rivalries.Where(r => r.IsBetween(wf.Id, lf.Id)))
                if (ws.Date < r.Since) r.Since = ws.Date;
            return p;
        }

        Title? BeltOf(string abbr, WeightClass wcl, Sex sex = Sex.Male)
        {
            var div = ByAbbr(abbr)?.Divisions.FirstOrDefault(d => d.WeightClass == wcl && d.Sex == sex);
            return div?.Title is { } tid ? world.Get(tid) : null;
        }

        // Mercer took a razor-thin decision off Seo two years ago — a title defense
        // mid-reign, so the lightweight title picture opens as the grudge rematch.
        if (BeltOf("AFC", WeightClass.Lightweight) is { } afcLw)
            Pin("Mercer", "Seo", "AFC", 2.0, FightMethod.SplitDecision, fromTail: true, title: afcLw);

        // The Baltic heavyweight wars: Urbonas KO'd Ozols; Ozols answered by taking
        // the BLB belt off him — one apiece, the rubber match unbooked.
        Pin("Urbonas", "Ozols", "BLB", 3.2, FightMethod.TechnicalKnockout);
        if (BeltOf("BLB", WeightClass.Heavyweight) is { } blbBelt
            && Pin("Ozols", "Urbonas", "BLB", 1.6, FightMethod.UnanimousDecision, fromTail: true, title: blbBelt) is { } war2)
        {
            var spec = new ReignSpec { Title = blbBelt, WinSlot = war2.Anchor };
            // Urbonas' own reign: flag an earlier win of his as the belt-winning night.
            if (Of(ByLast("Urbonas")) is { } uc && PriorWin(uc, war2.Anchor.Date.AddDays(-420), war2.Anchor.Date, blbBelt, ByAbbr("BLB")!) is { } uw)
                spec.Prior = (uc.Fighter, uw);
            reigns.Add(spec);
        }

        // Hale dethroned Tanner for the AFC light-heavyweight belt three years ago.
        if (BeltOf("AFC", WeightClass.LightHeavyweight) is { } afcLhw
            && Pin("Hale", "Tanner", "AFC", 3.0, FightMethod.UnanimousDecision, fromTail: true, title: afcLhw) is { } dethrone)
        {
            var spec = new ReignSpec { Title = afcLhw, WinSlot = dethrone.Anchor };
            if (Of(ByLast("Tanner")) is { } tc && PriorWin(tc, dethrone.Anchor.Date.AddDays(-900), dethrone.Anchor.Date, afcLhw, ByAbbr("AFC")!) is { } tw)
                spec.Prior = (tc.Fighter, tw);
            reigns.Add(spec);
        }

        // Failed title challenges the bios remember: Corrigan at AFC welterweight,
        // Hattori at ICF bantamweight. Pinned to the champion's reign (tail) wins.
        if (BeltOf("AFC", WeightClass.Welterweight) is { } afcWw)
            Pin("Okonjo", "Corrigan", "AFC", 2.5, FightMethod.UnanimousDecision, fromTail: true, title: afcWw);
        if (BeltOf("ICF", WeightClass.Bantamweight) is { } icfBw)
            Pin("Batyr", "Hattori", "ICF", 2.4, FightMethod.UnanimousDecision, fromTail: true, title: icfBw);

        // Rivas: the former Cage Kings bantamweight champion who walked out — his
        // belt-winning night is real, the reign ends vacated (no losing bout).
        // Attached to the CURRENT champion's reign spec when it's built below.
        if (ByLast("Rivas") is { } rivas && Of(rivas) is { } rc
            && BeltOf("CK", WeightClass.Bantamweight) is { } ckBw && ByAbbr("CK") is { } ck)
        {
            var target = startDate.AddDays(-(int)(4.2 * 365));
            if (PriorWin(rc, target, startDate.AddDays(-800), ckBw, ck) is { } rw)
                vacatedPriors[ckBw.Id] = (rivas, rw);
        }

        // Farouk: cut by the GFC one loss after they stopped needing him — that
        // loss happened on a GFC card.
        if (ByLast("Farouk") is { } farouk && Of(farouk) is { } fc && ByAbbr("GFC") is { } gfc)
        {
            var target = startDate.AddDays(-(int)(1.5 * 365));
            var slot = fc.Slots.Where(x => x.Result == 'L' && !x.Taken)
                .OrderBy(x => Math.Abs(x.Date.DayNumber - target.DayNumber)).FirstOrDefault();
            if (slot is not null) slot.PinnedHost = gfc;
        }

        return (reigns, vacatedPriors);
    }

    /// <summary>Flag a past win before <paramref name="before"/> as a title-winning bout (a predecessor's reign start).</summary>
    private static Slot? PriorWin(Career c, DateOnly target, DateOnly before, Title title, Promotion host)
    {
        var slot = c.Slots
            .Where(x => x.Result == 'W' && !x.Taken && x.Title is null && x.Date < before.AddDays(-90))
            .OrderBy(x => Math.Abs(x.Date.DayNumber - target.DayNumber))
            .FirstOrDefault();
        if (slot is null) return null;
        slot.Title = title;
        slot.PinnedHost = host;
        return slot;
    }

    // =========================================================================
    // 3. Champions' reigns
    // =========================================================================

    private static void PinChampions(World world, Dictionary<FighterId, Career> careers,
        List<ReignSpec> reigns, Dictionary<TitleId, (Fighter, Slot)> vacatedPriors, PromotionId? exclude)
    {
        var storyTitles = reigns.Select(r => r.Title.Id).ToHashSet();
        foreach (var title in world.Titles.Values.OrderBy(t => t.Id.Value))
        {
            // Title-change stories already anchored this reign; just flag its defenses.
            if (storyTitles.Contains(title.Id))
            {
                FlagDefenses(world, reigns.First(r => r.Title.Id == title.Id));
                continue;
            }
            if (title.Promotion == exclude) continue;
            if (title.CurrentChampion is not { } cid || !careers.TryGetValue(cid, out var c)) continue;
            var host = world.Get(title.Promotion);
            if (host is null) continue;

            // The reign starts on the earliest win of the champion's current run —
            // which may already be a pinned story bout carrying this belt. But a
            // belt is not won on debut: an UNBEATEN champion's "current run" is
            // their whole career, which used to hand them the title in their
            // first professional fight and a decade of defenses to go with it.
            // Cap the reign so a champion always fought their way up to it.
            int tailStart = Math.Max(TailStart(c), Math.Min(MinBoutsBeforeABelt, c.Count - 1));
            tailStart = Math.Max(tailStart, Math.Min(c.Count - MaxReignBouts, c.Count - 1));
            var winSlot = c.Slots.FirstOrDefault(x => x.Index >= tailStart && x.Result == 'W');
            if (winSlot is null) continue;   // a champion with no past win keeps the day-one reign
            if (winSlot.Title is null)
            {
                winSlot.Title = title;
                winSlot.PinnedHost = host;
            }
            var spec = new ReignSpec { Title = title, WinSlot = winSlot };
            if (vacatedPriors.TryGetValue(title.Id, out var prior) && prior.Item2.Date < winSlot.Date)
            {
                spec.Prior = prior;
                spec.PriorVacated = true;
            }
            reigns.Add(spec);
            FlagDefenses(world, spec);
        }
    }

    /// <summary>Every win after the belt-winning night is a defense of it.</summary>
    private static void FlagDefenses(World world, ReignSpec spec)
    {
        var c = spec.WinSlot.Career;
        var host = world.Get(spec.Title.Promotion);
        if (host is null) return;
        foreach (var s in c.Slots.Where(x => x.Index > spec.WinSlot.Index && x.Result == 'W'))
        {
            if (s.Title is null && !s.Taken)
            {
                s.Title = spec.Title;
                s.PinnedHost = host;
            }
            if (s.Title == spec.Title) spec.Defenses++;
        }
    }

    /// <summary>Nobody wins a world title in their first few professional fights.</summary>
    private const int MinBoutsBeforeABelt = 3;

    /// <summary>
    /// The longest reign the back-dating will invent — the belt-winning night plus
    /// up to six defenses. Real long reigns get built by PLAYING; day one shouldn't
    /// open with a champion who has held the belt since their debut.
    /// </summary>
    private const int MaxReignBouts = 7;

    /// <summary>First index of the trailing win (or loss) run — where a champion's reign can live.</summary>
    private static int TailStart(Career c)
    {
        int i = c.Count - 1;
        if (i < 0) return 0;
        char last = c.Slots[i].Result;
        while (i > 0 && c.Slots[i - 1].Result == last) i--;
        return i;
    }

    // =========================================================================
    // 4. Roster pairing
    // =========================================================================

    private static void PairRosters(World world, Dictionary<FighterId, Career> careers,
        List<Planned> planned, Dictionary<(int, int), int> meetings, PromotionId? exclude)
    {
        foreach (var promo in world.Promotions.Values.OrderBy(p => p.Id.Value))
        {
            if (promo.Id == exclude || promo.Defunct) continue;
            var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)promo.Id.Value ^ SaltPair));

            var roster = world.RosterOf(promo.Id).OrderBy(f => f.Id.Value).ToList();
            foreach (var group in roster.GroupBy(f => (f.WeightClass, f.Sex)).OrderBy(g => g.Key))
            {
                var slots = group
                    .Where(f => careers.ContainsKey(f.Id))
                    .SelectMany(f => careers[f.Id].Slots)
                    .Where(s => !s.Taken
                                && (s.Index + 1) / (double)s.Career.Count >= PairableFrac
                                && (s.PinnedHost is null || s.PinnedHost == promo))
                    .ToList();
                var losses = slots.Where(s => s.Result == 'L').OrderBy(s => s.Date).ToList();

                foreach (var ws in slots.Where(s => s.Result == 'W').OrderBy(s => s.Date))
                {
                    if (ws.Taken || ws.Career.Paired > ws.Career.Count * 0.45) continue;
                    if (!rng.Chance(0.85)) continue;
                    var ls = losses
                        .Where(x => !x.Taken && x.F.Id != ws.F.Id
                                    && x.Career.Paired <= x.Career.Count * 0.45
                                    && Math.Abs(x.Date.DayNumber - ws.Date.DayNumber) <= 130
                                    && MeetingCount(meetings, ws.F.Id, x.F.Id) < 2
                                    // Designed feuds are authored stories: their shared
                                    // past (or pointed lack of one) is pinned by hand,
                                    // never invented here.
                                    && !world.Rivalries.Any(r => r.IsBetween(ws.F.Id, x.F.Id))
                                    && Feasible(x.Career, x, ws.Date))
                        .OrderBy(x => Math.Abs(x.Date.DayNumber - ws.Date.DayNumber))
                        .FirstOrDefault();
                    if (ls is null) continue;

                    ls.Date = ws.Date;
                    ws.Taken = ls.Taken = true;
                    ws.Career.Paired++; ls.Career.Paired++;
                    Met(meetings, ws.F.Id, ls.F.Id);
                    planned.Add(new Planned
                    {
                        Anchor = ws, Partner = ls, Host = promo, Date = ws.Date,
                        Method = ws.WinMethod ?? FightMethod.UnanimousDecision,
                        IsTitle = ws.Title is not null, Title = ws.Title,
                    });
                }
            }
        }
    }

    /// <summary>The slot can move to <paramref name="date"/> without crowding its neighbours or sharing a month.</summary>
    private static bool Feasible(Career c, Slot slot, DateOnly date)
    {
        foreach (var other in c.Slots)
        {
            if (other == slot) continue;
            if (Math.Abs(other.Date.DayNumber - date.DayNumber) < 45) return false;
            if (other.Date.Year == date.Year && other.Date.Month == date.Month) return false;
            // Chronological order must survive the move.
            if (other.Index < slot.Index && other.Date >= date) return false;
            if (other.Index > slot.Index && other.Date <= date) return false;
        }
        return true;
    }

    private static void Met(Dictionary<(int, int), int> meetings, FighterId a, FighterId b)
    {
        var key = a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);
        meetings[key] = meetings.GetValueOrDefault(key) + 1;
    }

    private static int MeetingCount(Dictionary<(int, int), int> meetings, FighterId a, FighterId b)
    {
        var key = a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);
        return meetings.GetValueOrDefault(key);
    }

    // =========================================================================
    // 5. Minted opposition for the rest of the career
    // =========================================================================

    private sealed class MintPools
    {
        public readonly Dictionary<(WeightClass, Sex, int), List<Fighter>> Journeymen = new();
        public readonly Dictionary<(Region, WeightClass, Sex), List<Fighter>> Locals = new();
        public readonly Dictionary<FighterId, int> Uses = new();
        public readonly Dictionary<FighterId, HashSet<FighterId>> Faced = new();
        // Months (y*12+m) each minted opponent already fights in — one bout per
        // card, and their own histories stay chronologically clean.
        public readonly Dictionary<FighterId, HashSet<int>> Months = new();

        public bool BookedIn(Fighter f, DateOnly date) =>
            Months.TryGetValue(f.Id, out var set) && set.Contains(date.Year * 12 + date.Month);

        public void Book(Fighter f, DateOnly date) =>
            (Months.TryGetValue(f.Id, out var set) ? set : Months[f.Id] = new()).Add(date.Year * 12 + date.Month);
    }

    private static void FillRemainder(World world, Dictionary<FighterId, Career> careers,
        List<Planned> planned, FighterGenerator gen, DateOnly startDate, PromotionId? exclude)
    {
        var pools = new MintPools();
        var hosts = new HostPicker(world, exclude);

        foreach (var career in careers.Values.OrderBy(c => c.Fighter.Id.Value))
        {
            var f = career.Fighter;
            var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)f.Id.Value ^ SaltMint));
            int ability = FighterAbility.CurrentAbility(f);

            foreach (var slot in career.Slots.Where(s => !s.Taken))
            {
                slot.Taken = true;
                double frac = (slot.Index + 1) / (double)career.Count;
                var host = slot.PinnedHost ?? hosts.For(f, frac, slot.Index);
                if (host is null) continue;   // a world with no promotions at all

                // Lesser-known fighters came up through the local scene — same
                // "Local Striker" filler a small org books during gameplay.
                bool local = !f.Designed && slot.Title is null && ability < 62
                             && frac < (ability < 45 ? 0.70 : 0.35);
                var opp = local
                    ? LocalOpponent(world, gen, pools, rng, f, slot.Date, startDate)
                    : Journeyman(world, gen, pools, rng, f, ability, slot.Result == 'L', slot.Date, startDate);
                (pools.Faced.TryGetValue(f.Id, out var set) ? set : pools.Faced[f.Id] = new()).Add(opp.Id);
                pools.Book(opp, slot.Date);

                var method = slot.Result switch
                {
                    'W' => slot.WinMethod ?? FightMethod.UnanimousDecision,
                    'L' => RollLossMethod(rng),
                    'D' => FightMethod.Draw,
                    _ => FightMethod.NoContest,
                };
                planned.Add(new Planned
                {
                    Anchor = slot, Minted = opp, Host = host, Date = slot.Date,
                    Method = method, IsTitle = slot.Title is not null, Title = slot.Title,
                });
            }
        }
    }

    private static FightMethod RollLossMethod(SimRandom rng)
    {
        Span<double> w = stackalloc double[] { 0.45, 0.32, 0.20, 0.03 };
        return rng.WeightedIndex(w) switch
        {
            0 => RollDecision(rng),
            1 => rng.Chance(0.35) ? FightMethod.Knockout : FightMethod.TechnicalKnockout,
            2 => rng.Chance(0.85) ? FightMethod.SubmissionTap : FightMethod.TechnicalSubmission,
            _ => FightMethod.Disqualification,
        };
    }

    private static Fighter LocalOpponent(World world, FighterGenerator gen, MintPools pools,
        SimRandom rng, Fighter subject, DateOnly on, DateOnly startDate)
    {
        var key = (subject.Nationality, subject.WeightClass, subject.Sex);
        if (pools.Locals.TryGetValue(key, out var pool))
            foreach (var cand in pool)
                if (pools.Uses.GetValueOrDefault(cand.Id) < 10 && !HasFaced(pools, subject, cand)
                    && !pools.BookedIn(cand, on))
                    return Use(pools, cand);

        var style = Living.LocalScene.OfferedStyles[rng.Range(0, Living.LocalScene.OfferedStyles.Length - 1)];
        var id = world.NewFighterId();
        var f = gen.Generate(id, startDate, new GenOptions
        {
            WeightClass = subject.WeightClass,
            Style = style,
            Nationality = subject.Nationality,
            Sex = subject.Sex,
            Age = rng.Range(20, 31),
            Overall = rng.GaussianInt(24, 3, 18, 30),
        });
        f.FirstName = "Local";
        f.LastName = Living.LocalScene.Label(style);
        f.Nickname = null;
        f.LocalTalent = true;
        f.Status = FighterStatus.Retired;
        f.Intangibles.Popularity = rng.Range(3, 10);
        f.Momentum = 0;
        gen.AssignHometown(world, f);
        world.Fighters[id] = f;
        (pools.Locals.TryGetValue(key, out var list) ? list : pools.Locals[key] = new()).Add(f);
        return Use(pools, f);
    }

    private static Fighter Journeyman(World world, FighterGenerator gen, MintPools pools,
        SimRandom rng, Fighter subject, int ability, bool subjectLost, DateOnly on, DateOnly startDate)
    {
        // A loss went to somebody credible; a win came over a step-down opponent.
        int target = Math.Clamp(ability + (subjectLost ? rng.Range(-6, 4) : rng.Range(-22, -8)), 20, 84);
        var key = (subject.WeightClass, subject.Sex, target / 12);
        if (pools.Journeymen.TryGetValue(key, out var pool))
            foreach (var cand in pool)
                if (pools.Uses.GetValueOrDefault(cand.Id) < 8 && !HasFaced(pools, subject, cand)
                    && !pools.BookedIn(cand, on))
                    return Use(pools, cand);

        var id = world.NewFighterId();
        var f = gen.Generate(id, startDate, new GenOptions
        {
            WeightClass = subject.WeightClass,
            Sex = subject.Sex,
            Age = rng.Range(24, 38),
            Overall = Math.Clamp(target / 12 * 12 + 6, 20, 84),
        });
        f.Status = FighterStatus.Retired;    // fought their careers out; not on today's market
        f.Momentum = 0;
        gen.AssignHometown(world, f);
        gen.EnsureUniqueName(world, f);
        world.Fighters[id] = f;
        (pools.Journeymen.TryGetValue(key, out var list) ? list : pools.Journeymen[key] = new()).Add(f);
        return Use(pools, f);
    }

    private static bool HasFaced(MintPools pools, Fighter subject, Fighter cand) =>
        pools.Faced.TryGetValue(subject.Id, out var set) && set.Contains(cand.Id);

    private static Fighter Use(MintPools pools, Fighter f)
    {
        pools.Uses[f.Id] = pools.Uses.GetValueOrDefault(f.Id) + 1;
        return f;
    }

    /// <summary>Which promotion staged a given stretch of a career: the current org
    /// recently, the regional mid-tier before that, the small halls at the start.</summary>
    private sealed class HostPicker
    {
        private readonly World _world;
        private readonly Dictionary<(Region, Sex, char), List<Promotion>> _cache = new();
        private readonly List<Promotion> _all;
        private readonly PromotionId? _exclude;

        public HostPicker(World world, PromotionId? exclude)
        {
            _world = world;
            _exclude = exclude;
            _all = world.Promotions.Values
                .Where(p => p.Id != exclude && !p.Defunct)
                .OrderBy(p => p.Id.Value).ToList();
        }

        public Promotion? For(Fighter f, double frac, int index)
        {
            if (_all.Count == 0) return null;
            var current = f.Contract is { } c && c.Promotion != _exclude ? _world.Get(c.Promotion) : null;
            if (current is not null && frac > CurrentOrgFrac) return current;
            char tier = frac > MidOrgFrac ? 'M' : 'S';
            var pool = Pool(f.Nationality, f.Sex, tier);
            ulong h = SimRandom.Mix(_world.RngSeed, (ulong)(f.Id.Value * 8087 + index) ^ SaltEvent);
            return pool[(int)(h % (ulong)pool.Count)];
        }

        private List<Promotion> Pool(Region region, Sex sex, char tier)
        {
            var key = (region, sex, tier);
            if (_cache.TryGetValue(key, out var cached)) return cached;

            IEnumerable<Promotion> q = _all;
            if (sex == Sex.Female)
                q = q.Where(p => p.Divisions.Any(d => d.Sex == Sex.Female));
            var bySex = q.ToList();
            if (bySex.Count == 0) bySex = _all;

            var regional = bySex.Where(p => p.BaseRegion == region).ToList();
            if (regional.Count == 0) regional = bySex;
            var tiered = tier == 'M'
                ? regional.Where(p => p.Prestige.Value is >= 18 and <= 52).ToList()
                : regional.Where(p => p.Prestige.Value < 32).ToList();
            if (tiered.Count == 0) tiered = regional;
            return _cache[key] = tiered;
        }
    }

    // =========================================================================
    // 6. Staging: bouts become completed events
    // =========================================================================

    private static void StageEvents(World world, List<Planned> planned)
    {
        var venueCache = new Dictionary<PromotionId, List<Venue>>();
        var byHost = planned
            .GroupBy(p => p.Host.Id.Value)
            .OrderBy(g => g.Key);

        foreach (var hostGroup in byHost)
        {
            var host = world.Get(new PromotionId(hostGroup.Key))!;
            var buckets = hostGroup
                .GroupBy(p => (p.Date.Year, p.Date.Month))
                .OrderBy(g => g.Key)
                .ToList();

            int number = 0;
            foreach (var bucket in buckets)
            {
                number++;
                ulong h = SimRandom.Mix(world.RngSeed,
                    (ulong)(hostGroup.Key * 100_000 + bucket.Key.Year * 100 + bucket.Key.Month) ^ SaltEvent);
                var date = new DateOnly(bucket.Key.Year, bucket.Key.Month, 2 + (int)(h % 26));
                bool hasTitle = bucket.Any(p => p.IsTitle);
                bool ppv = hasTitle && host.Prestige.Value >= 38;

                var venue = PickHistoricVenue(world, host, h, venueCache);
                var ev = new Event
                {
                    Id = world.NewEventId(),
                    Promotion = host.Id,
                    Name = ppv ? $"{host.Abbreviation} {number}" : $"{host.Abbreviation} Fight Night {number}",
                    Date = date,
                    Venue = venue?.Name,
                    VenueId = venue?.Id,
                    Region = venue is { } v && world.Get(v.City) is { } city ? city.Region : host.BaseRegion,
                    Tier = ppv ? EventTier.Ppv : EventTier.FightNight,
                    Status = EventStatus.Completed,
                };

                // Card in rising order of importance — the belt (or the biggest
                // names) top the bill, like any real card.
                var ordered = bucket
                    .OrderBy(p => p.IsTitle ? 1 : 0)
                    .ThenBy(p => (p.Anchor.F.Intangibles.Popularity.Value + p.Opponent.Intangibles.Popularity.Value))
                    .ThenBy(p => p.Anchor.F.Id.Value)
                    .ToList();
                foreach (var p in ordered)
                {
                    p.Date = date;
                    p.Anchor.Date = date;
                    if (p.Partner is { } partner) partner.Date = date;

                    var fight = new Fight
                    {
                        Id = world.NewFightId(),
                        RedCorner = p.Anchor.F.Id,
                        BlueCorner = p.Opponent.Id,
                        WeightClass = p.Anchor.F.WeightClass,
                        ScheduledRounds = p.IsTitle ? 5 : 3,
                        IsTitleFight = p.IsTitle,
                        Title = p.Title?.Id,
                    };
                    fight.Outcome = SynthOutcome(world, fight, p);
                    p.Fight = fight;
                    ev.Card.Add(fight);
                }
                world.Events[ev.Id] = ev;
            }
        }
    }

    private static Venue? PickHistoricVenue(World world, Promotion host, ulong hash,
        Dictionary<PromotionId, List<Venue>> cache)
    {
        // Historical cards obey the same reach ladder live booking does — a
        // national outfit's past lives in its own country, not all over the
        // continent (falls back to the home region if reach resolves nowhere).
        if (!cache.TryGetValue(host.Id, out var options))
        {
            options = world.Venues.Values
                .Where(v => Org.OrgReach.CanBookIn(world, host, v.City))
                .OrderBy(v => v.Id.Value)
                .ToList();
            if (options.Count == 0)
                options = world.Venues.Values
                    .Where(v => world.Get(v.City)?.Region == host.BaseRegion)
                    .OrderBy(v => v.Id.Value)
                    .ToList();
            cache[host.Id] = options;
        }
        return options.Count == 0 ? null : options[(int)(hash / 7 % (ulong)options.Count)];
    }

    /// <summary>A plausible result sheet for a bout nobody simulated: round, time,
    /// and statlines shaped by the method — enough for star ratings and scouting
    /// tape to read the bout the way they read a live one.</summary>
    private static FightOutcome SynthOutcome(World world, Fight fight, Planned p)
    {
        var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)fight.Id.Value ^ 0x51A7_11E5UL));
        bool finish = p.Method is FightMethod.Knockout or FightMethod.TechnicalKnockout
            or FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;
        bool distance = p.Method is FightMethod.UnanimousDecision or FightMethod.SplitDecision
            or FightMethod.MajorityDecision or FightMethod.Draw;

        int scheduled = fight.ScheduledRounds;
        int endRound;
        if (distance) endRound = scheduled;
        else if (p.Method == FightMethod.NoContest) endRound = rng.Range(1, 2);
        else
        {
            Span<double> w3 = stackalloc double[] { 0.45, 0.33, 0.22 };
            Span<double> w5 = stackalloc double[] { 0.30, 0.22, 0.18, 0.16, 0.14 };
            endRound = 1 + (scheduled >= 5 ? rng.WeightedIndex(w5) : rng.WeightedIndex(w3));
        }
        int endTime = distance ? 300 : rng.Range(35, 295);

        Fighter? winner = p.Method is FightMethod.Draw or FightMethod.NoContest
            ? null
            : p.Anchor.Result == 'W' ? p.Anchor.F : p.Opponent;
        Fighter? loser = winner is null ? null : winner == p.Anchor.F ? p.Opponent : p.Anchor.F;

        var stats = new Dictionary<FighterId, FightStatline>
        {
            [p.Anchor.F.Id] = Statline(rng, p.Anchor.F, winner == p.Anchor.F, endRound, p.Method),
            [p.Opponent.Id] = Statline(rng, p.Opponent, winner == p.Opponent, endRound, p.Method),
        };

        return new FightOutcome
        {
            Winner = winner?.Id,
            Loser = loser?.Id,
            Method = p.Method,
            EndRound = endRound,
            EndTimeSeconds = endTime,
            Stats = stats,
        };
    }

    private static FightStatline Statline(SimRandom rng, Fighter f, bool won, int rounds, FightMethod method)
    {
        bool grappler = f.PrimaryStyle is PrimaryStyle.Wrestler or PrimaryStyle.Grappler
            or PrimaryStyle.BrazilianJiuJitsu or PrimaryStyle.ClinchFighter;
        int landed = (int)(rounds * rng.Range(7, 26) * (won ? 1.15 : 0.85));
        int attempted = (int)(landed * (1.5 + rng.NextDouble() * 0.8));
        int tdAtt = grappler ? rng.Range(0, rounds + 2) : rng.Range(0, 1);
        int td = Math.Min(tdAtt, rng.Range(0, Math.Max(1, tdAtt)));
        int subs = grappler ? rng.Range(0, 2) : 0;
        if (won && method is FightMethod.SubmissionTap or FightMethod.TechnicalSubmission) subs += 1;
        int kd = 0;
        if (won && method == FightMethod.Knockout) kd = 1 + (rng.Chance(0.2) ? 1 : 0);
        else if (won && method == FightMethod.TechnicalKnockout) kd = rng.Chance(0.7) ? 1 : 0;
        else if (rng.Chance(0.06)) kd = 1;

        return new FightStatline
        {
            SignificantStrikesLanded = landed,
            SignificantStrikesAttempted = attempted,
            TakedownsLanded = td,
            TakedownsAttempted = tdAtt,
            SubmissionAttempts = subs,
            KnockdownsScored = kd,
            ControlTimeSeconds = td * rng.Range(35, 110) + (grappler ? rng.Range(0, 90) : 0),
        };
    }

    // =========================================================================
    // 7. Finalize: records, streaks, bout history, lineages
    // =========================================================================

    private static void Finalize(World world, Dictionary<FighterId, Career> careers,
        List<Planned> planned, List<ReignSpec> reigns)
    {
        // Every participant's bouts, in date order.
        var byFighter = new Dictionary<FighterId, List<Planned>>();
        void Add(FighterId id, Planned p)
        {
            if (!byFighter.TryGetValue(id, out var list)) byFighter[id] = list = new List<Planned>();
            list.Add(p);
        }
        foreach (var p in planned.Where(x => x.Fight is not null))
        {
            Add(p.Anchor.F.Id, p);
            Add(p.Opponent.Id, p);
        }

        foreach (var (id, bouts) in byFighter.OrderBy(kv => kv.Key.Value))
        {
            var f = world.Get(id)!;
            bouts.Sort((a, b) => a.Date != b.Date ? a.Date.CompareTo(b.Date) : a.Fight!.Id.Value.CompareTo(b.Fight!.Id.Value));

            f.BoutHistory.Clear();
            var rec = f.Record;
            rec.Wins = rec.Losses = rec.Draws = rec.NoContests = 0;
            rec.WinsByKo = rec.WinsBySubmission = rec.WinsByDecision = 0;
            rec.CurrentStreak = 0;

            foreach (var p in bouts)
            {
                f.BoutHistory.Add(p.Fight!.Id);
                var o = p.Fight.Outcome!;
                if (o.Winner is null)
                {
                    if (o.Method == FightMethod.NoContest) rec.NoContests++; else rec.Draws++;
                    rec.CurrentStreak = 0;
                }
                else if (o.Winner == id)
                {
                    rec.Wins++;
                    switch (o.Method)
                    {
                        case FightMethod.Knockout or FightMethod.TechnicalKnockout: rec.WinsByKo++; break;
                        case FightMethod.SubmissionTap or FightMethod.TechnicalSubmission: rec.WinsBySubmission++; break;
                        case FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision: rec.WinsByDecision++; break;
                    }
                    rec.CurrentStreak = rec.CurrentStreak >= 0 ? rec.CurrentStreak + 1 : 1;
                }
                else
                {
                    rec.Losses++;
                    rec.CurrentStreak = rec.CurrentStreak <= 0 ? rec.CurrentStreak - 1 : -1;
                }
            }

            // Minted opposition carries a fuller career than the bouts we staged —
            // pad the tally so a 34-year-old journeyman doesn't read 0-8 (records
            // and bout histories legitimately diverge; the local circuit does the
            // same off-screen during gameplay).
            if (!careers.ContainsKey(id))
            {
                var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)id.Value ^ 0xBADD_EC05UL));
                int extraW = f.LocalTalent ? rng.Range(1, 4) : rng.Range(4, 12);
                int extraL = f.LocalTalent ? rng.Range(1, 3) : rng.Range(3, 9);
                rec.Wins += extraW;
                rec.Losses += extraL;
                rec.WinsByKo += extraW / 3;
                rec.WinsByDecision += extraW - extraW / 3;
            }
        }

        // Title lineages: the reigns the bouts describe, in order. Each handled
        // title has exactly one spec (current champion + optional predecessor).
        foreach (var spec in reigns)
        {
            var title = spec.Title;
            var wonOn = spec.WinSlot.Date;
            title.Lineage.Clear();
            if (spec.Prior is { } prior && prior.WinSlot.Date < wonOn)
            {
                // Dethroned in the pinned bout, or (Rivas) vacated some months before
                // today's champion claimed the empty belt.
                var lostOn = spec.PriorVacated
                    ? wonOn.AddDays(-40 - (int)(SimRandom.Mix(world.RngSeed, (ulong)title.Id.Value) % 140))
                    : wonOn;
                if (lostOn <= prior.WinSlot.Date) lostOn = prior.WinSlot.Date.AddDays(60);
                title.Lineage.Add(new TitleReign
                {
                    Champion = prior.Champ.Id,
                    WonOn = prior.WinSlot.Date,
                    LostOn = lostOn,
                });
            }
            title.Lineage.Add(new TitleReign
            {
                Champion = spec.WinSlot.F.Id,
                WonOn = wonOn,
                Defenses = spec.Defenses,
            });
        }
    }
}
