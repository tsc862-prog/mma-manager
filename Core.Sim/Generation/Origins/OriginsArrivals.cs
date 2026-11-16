using MmaManager.Core.Model;
using MmaManager.Core.Sim.Geography;

namespace MmaManager.Core.Sim.Generation.Origins;

/// <summary>What a scheduled arrival is.</summary>
public enum OriginsArrivalKind { Promotion, Camp, Fighter }

/// <summary>
/// Someone or something that joins the Origins world later than 1993, tied to the chapter that makes it
/// believable. <see cref="Pioneer"/> carries the same row shape the day-one pioneers use (fighters only);
/// <see cref="Lead"/> is the founder (promotion), head coach (camp) or null (fighter).
/// <see cref="EarliestMonth"/> sits inside the linked chapter's window. Not wired anywhere yet.
/// </summary>
public sealed record OriginsArrival(
    string Key, string Name, OriginsArrivalKind Kind, int EarliestMonth, int LatestMonth,
    string Country, string City, string Chapter, string? Lead, string? Abbreviation, string? Niche,
    string Lore, OriginsPioneer? Pioneer = null)
{
    public int EarliestYear => 1993 + EarliestMonth / 12;
}

/// <summary>
/// The scheduled arrivals of the Origins timeline: the promotion that becomes the giant of the Boom, the
/// big-arena promotion of the second centre, the first all-women's promotion, the first famous camp and
/// six fighters who debut in later eras. All invented. Append-only (keys are saved).
/// </summary>
public static class OriginsArrivals
{
    public static IReadOnlyList<OriginsArrival> All { get; } = Finish(new OriginsArrival[]
    {
        new("arrival-seiryu-grand-combat", "Seiryu Grand Combat", OriginsArrivalKind.Promotion, 66, 120,
            "JPN", "Tokyo", "second-centre-arenas", "Ryoji Fukuhara", "SGC",
            "The dome promotion: entrances, orchestras and a single belt per night, in arenas of fifteen thousand and more.",
            "Ryoji Fukuhara ran a stadium concession for eleven years and watched a dozen sports fill and empty the same seats. He decides in 1998 that a fight is just a very expensive piece of theatre and builds a company around the thought. Seiryu Grand Combat opens in a sports arena with a lighting rig borrowed from a pop concert and a ring-walk that lasts four minutes. Its first card sells out; its second needs a bigger building. Within two years it has a national television contract, a house orchestra and a fan base that bows at the final bell. Its champions are famous at home and almost unknown abroad, which Fukuhara intends to change."),

        new("arrival-fenwick-street-combat-club", "Fenwick Street Combat Club", OriginsArrivalKind.Camp, 66, 114,
            "USA", "Chicago", "first-cross-training-camp", "Dennis Raczkowski", null,
            "The first famous cross-training camp: boxing, wrestling and the ground taught in one room.",
            "A converted tyre warehouse on Fenwick Street, a tin roof and a mat nobody has ever washed. Dennis Raczkowski, a one-time Golden Gloves boxer and high-school wrestling coach, starts it in 1998 with six students and a theory that nobody has ever won a fight with only one skill. By the end of the year he has thirty students; by the end of the next, a waiting list. His timetable is the first to teach striking, takedowns and submissions as parts of a single sport. Its graduates win fights in every promotion in the world, and every ambitious gym in three countries copies the schedule."),

        new("arrival-bellwether-fighting-championship", "Bellwether Fighting Championship", OriginsArrivalKind.Promotion, 84, 132,
            "USA", "Las Vegas", "first-sanctioned-state", "Dale Kessler", "BFC",
            "The patient giant: sanctioned from the start, built on strict rules and television, the promotion everyone ends up measured against.",
            "Dale Kessler and his brother Vern ran a casino's entertainment floor for fifteen years and learned two things: that a licence is a promoter's best friend, and that a crowd will pay for any fight that looks properly run. Bellwether Fighting Championship is their bet on the licence. Its cards are sanctioned from the first night, its rules are the strictest in the sport and its broadcasts are polished to a degree that the older promotions find faintly insulting. It loses money for four years. When the sport finally finds its television moment, Bellwether is the only promotion with the paperwork, the production crew and the contracts to be ready. It becomes the giant of the Boom."),

        new("arrival-bellona-fight-league", "Bellona Fight League", OriginsArrivalKind.Promotion, 192, 240,
            "CAN", "Toronto", "womens-first-division", "Marguerite Beaulieu", "BLF",
            "The first all-women's promotion: every card, every belt, every fighter.",
            "Marguerite Beaulieu coached a women's boxing club for twelve years and has never been able to find a ring that was not booked on her fighters' big nights. She founds the Bellona Fight League in a Toronto arena with a borrowed ring, a small television deal and a promise that no man will fight on a Bellona card. Its first show crowns a strawweight champion and sells out an arena that nobody had thought could be filled. Within two seasons it has divisions at three weights, a roster of two hundred and a waiting list of promotions wanting to buy it. It is the first promotion in the sport to be run by, and for, women."),

        new("arrival-declan-voss", "Declan Voss", OriginsArrivalKind.Fighter, 72, 120,
            "USA", "Chicago", "first-cross-training-camp", null, null, null,
            "The first fighter who is good at everything: a college wrestler who learned to punch and submit in the same room.",
            Row("Declan", "Voss", "The Polymath", "USA", OriginsDiscipline.FreestyleWrestling, 183, 83, 24, 62, 3, 0, null, 28,
                "Voss wrestled for a mid-sized university and was an All-American in his senior year. He then spent two winters at a warehouse gym on Fenwick Street in Chicago, where a one-time boxing coach taught him to punch properly and a visiting black belt from a Los Angeles strip mall taught him to choke. By 1998 he is the first fighter in the world who can do all three, and the first one the older specialists cannot answer. His record is short; his career is a tour of the discovery that the one-art champions of the early days were simply out of date.",
                new[] { (SkillId.PunchTechnique, 24), (SkillId.StrikingDefense, 22), (SkillId.Footwork, 22), (SkillId.SubmissionOffense, 24), (SkillId.SubmissionDefense, 24), (SkillId.BottomGame, 24), (SkillId.KickTechnique, 14) },
                PrimaryStyle.WellRounded)),

        new("arrival-tiago-moreira", "Tiago Moreira", OriginsArrivalKind.Fighter, 120, 168,
            "BRA", "Rio de Janeiro", "weight-ladder-lower", null, null, null,
            "A Rio lightweight raised on the garage nights: the first Brazilian who is a star on television, not only in the gym.",
            Row("Tiago", "Moreira", "Relampago", "BRA", OriginsDiscipline.JiuJitsu, 175, 70, 22, 66, 4, 0, "DLB", 30,
                "Moreira grew up two streets from the Tijuca garage where Duelo Livre held its first Saturday nights, and was carried on his father's shoulders to the fifth. He began jiu-jitsu at six on the thin mats of Odair Quintela's academy, paying in the bakery's leftover bread, boxing at fourteen and, at twenty-one, a career in the sport that had been waiting for him. He is quick, smiling and entirely unafraid, and the Saturday-night television audience, who have watched his sport grow up in front of them, think of him as one of the family.",
                new[] { (SkillId.PunchTechnique, 18), (SkillId.KickTechnique, 8), (SkillId.Footwork, 14), (SkillId.StrikingDefense, 12) },
                PrimaryStyle.BrazilianJiuJitsu)),

        new("arrival-arkady-slobodin", "Arkady Slobodin", OriginsArrivalKind.Fighter, 144, 192,
            "RUS", "Moscow", "boom-new-promotions", null, null, null,
            "A heavyweight from the sambo halls: patient, unfashionable and almost impossible to take down.",
            Row("Arkady", "Slobodin", "The Iron Clerk", "RUS", OriginsDiscipline.Sambo, 190, 108, 27, 68, 6, 0, null, 18,
                "A bank clerk by day, Slobodin trained in the Zarya sambo hall, where Vadim Orekhov and Boris Khlebnikov had been feeding the stove for a decade and which had produced the old wrestling champions a generation earlier. He is slow to anger, slower to tire and, once he has a grip, very hard to remove. He is a heavyweight in the old sense: thick, patient and uninterested in the kind of showmanship that sells tickets. He signs his first contract with a handshake and a request that the promoter pay the hall's heating bill.",
                new[] { (SkillId.TakedownDefense, 10), (SkillId.Strength, 8) },
                PrimaryStyle.Grappler)),

        new("arrival-joelle-aubuchon", "Joelle Aubuchon", OriginsArrivalKind.Fighter, 204, 252,
            "CAN", "Montreal", "womens-first-star", null, "BLF", null,
            "The first women's star: a judoka from Quebec with a calm manner and a long streak of finishes.",
            Row("Joelle", "Aubuchon", "La Tempete", "CAN", OriginsDiscipline.Judo, 168, 61, 25, 70, 3, 0, "BLF", 40,
                "Aubuchon won national judo titles for six years, retired with a back injury she was told was permanent and went into a boxing club to stay fit. Two years later she is a professional in the Bellona Fight League's first season, and a year after that the best-known woman in the sport. She wins quietly and finishes decisively, and gives long, careful answers to questions about what it means. Magazines put her on their covers. She keeps her gym in Montreal and still teaches the children's class on Tuesdays.",
                new[] { (SkillId.PunchTechnique, 14), (SkillId.StrikingDefense, 12), (SkillId.SubmissionOffense, 14), (SkillId.Footwork, 10) },
                PrimaryStyle.Grappler, Sex.Female)),

        new("arrival-mei-nakagawa", "Mei Nakagawa", OriginsArrivalKind.Fighter, 228, 260,
            "JPN", "Tokyo", "womens-headline-card", null, "BLF", null,
            "A strawweight from Tokyo, quick enough to be called untouchable, who headlines the first women's card.",
            Row("Mei", "Nakagawa", "Kingfisher", "JPN", OriginsDiscipline.Karate, 157, 52, 21, 66, 4, 0, "BLF", 32,
                "Nakagawa began karate at five, trained with the Kawabata Dojo's children's class and was national junior champion at fifteen. She weighs fifty-two kilos and moves like something thrown. She is the second woman in the world to be famous in the sport, and the first to be famous in Japan: her shows fill a hall that holds eleven thousand. She is shy in interviews, relentless in the ring and fond of saying that speed is only patience in a hurry.",
                new[] { (SkillId.PunchTechnique, 8), (SkillId.Footwork, 14), (SkillId.TakedownDefense, 10) },
                PrimaryStyle.Striker, Sex.Female)),

        new("arrival-kwabena-asante", "Kwabena Asante", OriginsArrivalKind.Fighter, 300, 336,
            "GHA", "Accra", "scene-west-africa-maghreb-lima", null, null, null,
            "A lightweight from Accra's boxing gyms: the first world-level star from the continent's newest scene.",
            Row("Kwabena", "Asante", "Ananse", "GHA", OriginsDiscipline.Boxing, 175, 70, 23, 72, 5, 0, null, 26,
                "Asante boxed for a gym on the Jamestown waterfront from the age of ten, and learned wrestling from a coach who watched the first Accra card on a borrowed television. He was named after the spider of local folklore for his habit of being where you do not expect him. He is smooth, funny and impossible to bore; the first African fighter to be asked, at twenty-three, to appear on a poster in Las Vegas. His fans think he is the first of a hundred. The gyms in Accra are already full.",
                new[] { (SkillId.TakedownOffense, 18), (SkillId.TakedownDefense, 18), (SkillId.Scrambling, 18), (SkillId.TopControl, 14) },
                PrimaryStyle.WellRounded)),
    });

    /// <summary>For fighters: the one-line description becomes the pioneer's hook and the full bio becomes the lore.</summary>
    private static OriginsArrival[] Finish(OriginsArrival[] rows) => rows.Select(a =>
        a.Pioneer is null ? a : a with { Lore = a.Pioneer.Fighter.Bio!, Pioneer = a.Pioneer with { Hook = a.Lore } }).ToArray();

    // ------------------------------------------------------------------ row builder
    /// <summary>The same shape <see cref="OriginsPioneers"/> uses: a <see cref="DesignedFighter"/> plus the pioneer extras.</summary>
    private static OriginsPioneer Row(
        string first, string last, string nick, string country, OriginsDiscipline d, int cm, int kg, int age, int overall,
        int wins, int losses, string? org, int popularity, string bio, (SkillId, int)[] tweaks, PrimaryStyle style,
        Sex sex = Sex.Male)
    {
        var region = WorldAtlas.ByCode(country)?.Continent ?? throw new ArgumentException($"{first} {last}: unknown country {country}");
        var fighter = new DesignedFighter(first, last, nick, region, OriginsPioneers.ClassFor(kg), style, age, overall, wins, losses,
            org, Champion: false, Popularity: popularity, Country: country, Sex: sex, RivalOf: null, Bio: bio);
        return new OriginsPioneer(fighter, d, cm, kg, "", tweaks.Select(t => (t.Item1, t.Item2)).ToArray());
    }
}
