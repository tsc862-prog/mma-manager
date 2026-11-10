using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>The moments a fighter gets to speak.</summary>
public enum Moment
{
    /// <summary>They just finished somebody.</summary>
    WinFinish,
    /// <summary>They just won a decision.</summary>
    WinDecision,
    /// <summary>They just lost.</summary>
    Loss,
    /// <summary>They just won a belt.</summary>
    TitleWin,
    /// <summary>They just lost a belt.</summary>
    TitleLoss,
    /// <summary>They've been released.</summary>
    Cut,
    /// <summary>Somebody else got the title shot.</summary>
    PassedOver,
    /// <summary>The offer on the table insults them.</summary>
    Lowball,
    /// <summary>They just signed.</summary>
    Signed,
    /// <summary>They're done.</summary>
    Retiring,
    /// <summary>They missed weight.</summary>
    MissedWeight,
}

/// <summary>
/// The sport's voice. Until this existed, every one of the ~50 mail messages and
/// the entire news feed was third-person narration — nobody in the world ever
/// said anything, so a roster of two thousand people read like a spreadsheet.
///
/// A line is chosen by WHO the fighter is (<see cref="Disposition"/>) and WHAT
/// just happened (<see cref="Moment"/>), and picked deterministically off the
/// fighter's id and the date, so the same moment always reads the same way and
/// a save reloads word for word.
/// </summary>
public static class Quotes
{
    /// <summary>What this fighter says about this moment, already in quote marks.</summary>
    public static string Say(Fighter f, Moment moment, DateOnly on)
    {
        var pool = Lines(f.Disposition, moment);
        if (pool.Count == 0) pool = Lines(Disposition.QuietProfessional, moment);
        uint h = Hash(f.Id.Value, (uint)moment * 31u + (uint)on.DayNumber);
        return $"“{pool[(int)(h % (uint)pool.Count)]}”";
    }

    /// <summary>The same line with the speaker attributed — for a news body.</summary>
    public static string Attributed(Fighter f, Moment moment, DateOnly on) =>
        $"{Say(f, moment, on)} — {f.FullName}";

    private static uint Hash(int id, uint salt)
    {
        unchecked
        {
            uint h = (uint)id * 2246822519u ^ (salt * 0x85EBCA6Bu);
            h ^= h >> 13; h *= 0x27D4EB2Fu;
            h ^= h >> 16;
            return h;
        }
    }

    // Every disposition answers every moment differently. Where an archetype has
    // nothing distinctive to add it falls back to the quiet professional, who is
    // the sport's default register.
    private static IReadOnlyList<string> Lines(Disposition d, Moment m) => (d, m) switch
    {
        // --- Winning by finish ---------------------------------------------
        (Disposition.Loudmouth, Moment.WinFinish) => new[]
        {
            "I told you exactly how it would go and it went exactly like that. Ask me again about the next one.",
            "Nobody wanted to hear it. Round one. Whose name do I have to say twice?",
            "That's not a fluke, that's a forecast.",
        },
        (Disposition.Showman, Moment.WinFinish) => new[]
        {
            "You didn't pay to watch me point-fight. You paid for that.",
            "Everybody go home happy. That's the job.",
            "I hear that noise and I forget I'm tired.",
        },
        (Disposition.Hothead, Moment.WinFinish) => new[]
        {
            "He had a lot to say all week. He's got nothing to say now.",
            "They keep sending me people who don't respect me. That's what happens.",
            "I'm not here to make friends and I never said I was.",
        },
        (Disposition.QuietProfessional, Moment.WinFinish) => new[]
        {
            "We drilled that exact sequence for six weeks. It's not magic, it's Tuesdays.",
            "Credit to him, he was tough. I just had a better camp.",
            "Job's done. I'll enjoy it tonight and be in the gym Monday.",
        },
        (Disposition.Student, Moment.WinFinish) => new[]
        {
            "He telegraphed the level change all round. Once I saw it twice, it was over.",
            "The tape said the opening was there in the second. It was there in the second.",
            "That's the fifth time that setup has worked. It'll work again.",
        },
        (Disposition.Mercenary, Moment.WinFinish) => new[]
        {
            "Short night, full purse. That's the business working properly.",
            "Whoever pays next gets the same. That's all I've got to say.",
        },
        (Disposition.CompanyMan, Moment.WinFinish) => new[]
        {
            "That's for everyone in this building who's backed me since the small halls.",
            "This is my house. People should stop booking fights in it.",
        },
        (Disposition.Warhorse, Moment.WinFinish) => new[]
        {
            "I'd have gone another three. Give me the next one.",
            "He came to fight. I respect that. Doesn't change the result.",
        },

        // --- Winning a decision --------------------------------------------
        (Disposition.Loudmouth, Moment.WinDecision) => new[]
        {
            "Three rounds, thirty minutes, doesn't matter. Nobody beats me.",
            "He survived. That's the whole story. He survived.",
        },
        (Disposition.Hothead, Moment.WinDecision) => new[]
        {
            "He held on for fifteen minutes and they're clapping him. Unbelievable.",
            "Run. That's all that was. Run for fifteen minutes.",
        },
        (Disposition.Showman, Moment.WinDecision) => new[]
        {
            "Not my favourite kind of night, and I owe you all a better one.",
            "He made it ugly on purpose. I'll take the win and buy you a proper fight next time.",
        },
        (Disposition.QuietProfessional, Moment.WinDecision) => new[]
        {
            "Not pretty, but it's a win. There are things to fix and I know what they are.",
            "He was awkward. I adjusted. That's the game.",
            "I'll take the fifteen minutes of work. You don't learn anything in ninety seconds.",
        },
        (Disposition.Student, Moment.WinDecision) => new[]
        {
            "I lost the second by getting greedy. That's on me and it's fixable.",
            "The plan was to make him lead. He led for fifteen minutes.",
        },
        (Disposition.Warhorse, Moment.WinDecision) => new[]
        {
            "That's the kind of fight I like. Nobody's face was the same at the end.",
            "Twenty-five hard minutes. I'd do it again next week if they asked.",
        },

        // --- Losing ----------------------------------------------------------
        (Disposition.Loudmouth, Moment.Loss) => new[]
        {
            "One fight. One. Watch what I do to him in the rematch.",
            "I'd like to see him do that when I'm not carrying an injury nobody knew about.",
        },
        (Disposition.Hothead, Moment.Loss) => new[]
        {
            "The referee decided that fight and everybody in the building knows it.",
            "I want it back. Tomorrow, next week, in the car park, I don't care.",
        },
        (Disposition.QuietProfessional, Moment.Loss) => new[]
        {
            "He was better tonight. No excuses. I'll go and be better.",
            "That one's mine to own. I'll take a couple of weeks and get back to work.",
        },
        (Disposition.Student, Moment.Loss) => new[]
        {
            "I know exactly which round I lost that in, and I know why. That's useful.",
            "There's a hole in my game. I'd rather find it here than in a title fight.",
        },
        (Disposition.Showman, Moment.Loss) => new[]
        {
            "You got your money's worth. I just didn't get mine.",
            "I'd rather lose like that than win boring. I mean it, mostly.",
        },
        (Disposition.Warhorse, Moment.Loss) => new[]
        {
            "Good fight. Hard fight. That's why I do it.",
            "He earned it. Shake his hand, go home, start again.",
        },
        (Disposition.Mercenary, Moment.Loss) => new[]
        {
            "I got paid either way. Doesn't mean I liked it.",
            "Bad night at work. The next contract still gets signed.",
        },
        (Disposition.CompanyMan, Moment.Loss) => new[]
        {
            "I let a lot of people down tonight. I'll make it right here.",
            "They stuck with me before. I'm not going anywhere.",
        },

        // --- Belts -----------------------------------------------------------
        (Disposition.Loudmouth, Moment.TitleWin) => new[]
        {
            "I said it when nobody was listening and I'll say it with this on my shoulder. Line them up.",
            "Everyone who picked against me — I have a list and I have a long memory.",
        },
        (Disposition.Showman, Moment.TitleWin) => new[]
        {
            "This thing is going to travel. Wherever you are, I'll bring it to you.",
            "I've been imagining this since I was a kid and it's better than I imagined.",
        },
        (Disposition.QuietProfessional, Moment.TitleWin) => new[]
        {
            "My coaches did this. My family did this. I just carried it in.",
            "It hasn't landed yet. Ask me in a week.",
        },
        (Disposition.CompanyMan, Moment.TitleWin) => new[]
        {
            "This belt stays here. I don't care what anyone else offers.",
            "The people who signed me when I was nobody get to hold this first.",
        },
        (Disposition.Student, Moment.TitleWin) => new[]
        {
            "Eleven years of small rooms and film. It works. The work works.",
            "Now the interesting part starts — everyone gets to study me.",
        },
        (Disposition.Mercenary, Moment.TitleWin) => new[]
        {
            "The belt's nice. The champion's purse is nicer. Let's talk.",
            "This changes my number. Considerably.",
        },
        (Disposition.Warhorse, Moment.TitleWin) => new[]
        {
            "Whoever's next, I'll fight them anywhere. That's what this is for.",
            "I didn't take a single easy fight to get here and I'm not starting now.",
        },
        (Disposition.Hothead, Moment.TitleWin) => new[]
        {
            "Everybody who laughed can watch me wear this for years.",
            "Now they have to come through me. Good luck to them.",
        },

        (Disposition.Loudmouth, Moment.TitleLoss) => new[]
        {
            "It's a loan, not a loss. I want it back immediately.",
            "He's keeping it warm. That's all that is.",
        },
        (Disposition.QuietProfessional, Moment.TitleLoss) => new[]
        {
            "It was never mine, I just held it a while. He's got it now — he earned it.",
            "I'll climb back. It's the only thing I know how to do.",
        },
        (Disposition.Hothead, Moment.TitleLoss) => new[]
        {
            "They took that off me in there and somebody's going to answer for it.",
            "Rematch clause. Read it. Use it.",
        },

        // --- The business ---------------------------------------------------
        (Disposition.Mercenary, Moment.Lowball) => new[]
        {
            "That's not an offer, that's a rounding error. Somebody else will pay it.",
            "I'm not sentimental and I'm not cheap. Come back with a real number.",
        },
        (Disposition.Loudmouth, Moment.Lowball) => new[]
        {
            "They're selling tickets with my face and offering me bus fare.",
            "I put people in seats. Pay me like it or I'll do it somewhere else.",
        },
        (Disposition.CompanyMan, Moment.Lowball) => new[]
        {
            "I'd like to stay. I'd also like to feed my family. Meet me somewhere.",
            "I'm not going to fight them in the press. But that number has to move.",
        },
        (Disposition.QuietProfessional, Moment.Lowball) => new[]
        {
            "My manager handles that. I just train.",
            "It's below the market and they know it. We'll keep talking.",
        },
        (Disposition.Warhorse, Moment.Lowball) => new[]
        {
            "Pay me what's fair and I'll fight whoever you want, whenever you want.",
            "I've never held a promotion up over money. I'm also not a charity.",
        },

        (Disposition.Mercenary, Moment.Cut) => new[]
        {
            "Business is business. My phone's already going.",
            "No hard feelings. I'll see them again when their champion needs an opponent.",
        },
        (Disposition.Hothead, Moment.Cut) => new[]
        {
            "They'll regret that inside a year. Put it in writing.",
            "Released. After what I gave that company. Fine.",
        },
        (Disposition.CompanyMan, Moment.Cut) => new[]
        {
            "That one hurts. I thought I'd finish my career there.",
            "I've got no bad word to say. I just wish they'd waited one more fight.",
        },
        (Disposition.QuietProfessional, Moment.Cut) => new[]
        {
            "It's a results business and I didn't get results. I'll go and get some.",
            "I'm grateful for the run. Somebody else gets the next version of me.",
        },

        (Disposition.Loudmouth, Moment.PassedOver) => new[]
        {
            "They gave my shot to a man I've already beaten. Explain that one.",
            "I'll keep saying it until somebody with a pen hears me: I'm next.",
        },
        (Disposition.Hothead, Moment.PassedOver) => new[]
        {
            "I'm done being polite about this. Book the fight or let me go.",
            "Every week they skip me is a week I remember.",
        },
        (Disposition.QuietProfessional, Moment.PassedOver) => new[]
        {
            "I'll keep winning until it's impossible to ignore. That's the only answer I've got.",
            "Disappointed. Not surprised. Back to work.",
        },
        (Disposition.Mercenary, Moment.PassedOver) => new[]
        {
            "If the shot isn't here, the shot is somewhere. I'll find it.",
            "I'm not waiting in a queue for a belt. My contract has an end date.",
        },

        (Disposition.CompanyMan, Moment.Signed) => new[]
        {
            "I'd have signed this on a napkin. This is where I want to be.",
            "They believed in me first. That's worth more than a bidding war.",
        },
        (Disposition.Mercenary, Moment.Signed) => new[]
        {
            "Best offer on the table. That's the whole reason, and I won't pretend otherwise.",
            "Good deal, good terms. Now let's go to work.",
        },
        (Disposition.Loudmouth, Moment.Signed) => new[]
        {
            "You've just signed the best thing that's ever walked through that door.",
            "Whoever's holding that belt — start watching tape. I'm here now.",
        },
        (Disposition.QuietProfessional, Moment.Signed) => new[]
        {
            "Happy to be here. I'll let the fighting do the talking.",
            "New room, same job. I'm ready to go whenever they call.",
        },

        (Disposition.QuietProfessional, Moment.Retiring) => new[]
        {
            "The body's been sending letters for a while. I finally opened one.",
            "I got more out of this sport than I ever put in. That's the truth.",
        },
        (Disposition.Warhorse, Moment.Retiring) => new[]
        {
            "I never ducked anybody. Whatever else they say, they can't say that.",
            "Thirty-one fights and I'd take every one of them again.",
        },
        (Disposition.Loudmouth, Moment.Retiring) => new[]
        {
            "I said I'd change this sport and I'd say I got most of the way there.",
            "Don't call it retirement. Call it a very long camp.",
        },

        (Disposition.QuietProfessional, Moment.MissedWeight) => new[]
        {
            "No excuse. The weight is my responsibility and I failed at it.",
            "I let my opponent and my team down before the fight even started.",
        },
        (Disposition.Hothead, Moment.MissedWeight) => new[]
        {
            "Two pounds. Two. And everyone's acting like I robbed a bank.",
            "The cut was wrong and the camp was wrong. It won't happen again.",
        },
        (Disposition.Mercenary, Moment.MissedWeight) => new[]
        {
            "They can keep their twenty percent. I'm still getting paid to fight.",
            "It's a catchweight. Same night, same job, smaller cheque.",
        },

        _ => Array.Empty<string>(),
    };
}
