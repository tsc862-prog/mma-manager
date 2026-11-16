using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>
/// Lines that know who the speaker is talking about: a student who has just beaten his teacher,
/// two rivals, a rematch. The line is always about the OPPONENT of the moment (the person the
/// speaker just beat, lost to, or took a belt from), so each pool is written for "I won" or
/// "I lost" against somebody I am tied to.
///
/// Every line is era-safe (no rankings, weight classes, gloves, pay-per-view or screens: it must
/// read in a 1993 hall as well as today) and claim-safe (no counts, no places, nothing the world could
/// overturn: only the tie itself, which <see cref="Ties"/> has already proved). Two registers per
/// pool: the hot ones (loudmouths, hotheads, showmen) and everybody else. A table, appended to,
/// never reordered: a row's position feeds the pick.
/// </summary>
public static class TieLines
{
    /// <summary>The share of calls (out of 100) that use a tie line when there is one. Stable per speaker and day.</summary>
    public static int ShareOf(TieKind kind) => kind switch
    {
        TieKind.Family => 75,
        TieKind.Mentor => 75,
        TieKind.Student => 75,
        TieKind.Rival => 70,
        TieKind.Rematch => 55,
        TieKind.Friend => 60,
        TieKind.Teammate => 40,
        TieKind.Countryman => 30,
        _ => 0,
    };

    /// <summary>Is this speaker's register the hot one?</summary>
    public static bool IsHot(Disposition d) => d is Disposition.Loudmouth or Disposition.Hothead or Disposition.Showman;

    /// <summary>Did the speaker win? Null when the moment is not a result a tie line is written for.</summary>
    public static bool? WonOf(Moment m) => m switch
    {
        Moment.WinFinish or Moment.WinDecision or Moment.TitleWin => true,
        Moment.Loss or Moment.TitleLoss => false,
        _ => null,
    };

    /// <summary>The pool of lines for this tie, register and result; empty when there is none.</summary>
    public static IReadOnlyList<string> Pool(Tie tie, bool hot, bool won)
    {
        var series = tie.Kind == TieKind.Rematch ? tie.Series : SeriesLean.Level;
        return Table.TryGetValue((tie.Kind, series, hot, won), out var pool) ? pool : Array.Empty<string>();
    }

    private static readonly Dictionary<(TieKind, SeriesLean, bool, bool), string[]> Table = Build();

    private static Dictionary<(TieKind, SeriesLean, bool, bool), string[]> Build()
    {
        var t = new Dictionary<(TieKind, SeriesLean, bool, bool), string[]>();
        void Add(TieKind k, SeriesLean s, bool hot, bool won, params string[] lines) => t[(k, s, hot, won)] = lines;
        const SeriesLean L = SeriesLean.Level;

        // --- Blood ---------------------------------------------------------------
        Add(TieKind.Family, L, true, true,
            "Blood doesn't buy a discount in there. Dinner at the family table is going to be quiet.",
            "I love him and I'd do it again. Both of those are true.");
        Add(TieKind.Family, L, false, true,
            "Nobody in the building wanted that fight less than the two of us. It had to be done properly, so I did it properly.",
            "Hardest night's work I've done. Call him tonight, I'm going to.");
        Add(TieKind.Family, L, true, false,
            "Family or not, I want it back. The dinner table can wait.",
            "He beat me and the whole family's pretending it's fine. It isn't fine.");
        Add(TieKind.Family, L, false, false,
            "Lose to a stranger and it's a result. Lose to family and it's a story. I'll live with the story.",
            "If it had to be anybody, I'm glad it was him. I'll say that once.");

        // --- The teacher, beaten by or beating the student -----------------------------
        Add(TieKind.Mentor, L, true, true,
            "I taught him everything he knows and I kept a few things back. Tonight was one of them.",
            "Respect the student, sure. But the teacher is still in the room.");
        Add(TieKind.Mentor, L, false, true,
            "I've held pads for him. Tonight I had to forget I ever did.",
            "I taught him half of it. The other half is all him, and that's the half that hurt.");
        Add(TieKind.Mentor, L, true, false,
            "I built that man. Fine. Now he's built a problem for me.",
            "He learned it from me. Put that in the paper next to his name.");
        Add(TieKind.Mentor, L, false, false,
            "That's the whole job: teaching somebody to beat you. I'm proud and I'm not happy.",
            "He did exactly what I spent years telling him to do. Hard to complain about that.");

        // --- The student, beating or losing to the teacher ------------------------------
        Add(TieKind.Student, L, true, true,
            "Everybody said I couldn't. He said I could, and then I went and did it to him.",
            "He taught me to do that and he never told me what it'd feel like to do it to him.");
        Add(TieKind.Student, L, false, true,
            "I owe him everything I've got in there. I'll thank him properly when my hands stop shaking.",
            "I didn't want to win it like that. I'm glad I did, and I'm sorry to him for it.");
        Add(TieKind.Student, L, true, false,
            "He knows every move I own because he gave me them. Next time I'll bring one he didn't.",
            "He's still the best teacher I've had. And I'm still not done with him.");
        Add(TieKind.Student, L, false, false,
            "He's still the best teacher I've had. Tonight he taught me one more thing.",
            "That's what I came for, honestly: to find out how far I'd got. Not far enough yet.");

        // --- Bad blood -------------------------------------------------------------
        Add(TieKind.Rival, L, true, true,
            "Told you. Told everybody. It's on the record now.",
            "Say it to my face now. Go on. I'll wait.");
        Add(TieKind.Rival, L, false, true,
            "This was always going to be settled in there, and now it has been. I'd rather leave it at that.",
            "I didn't enjoy it as much as I thought I would, and I thought I would a lot.");
        Add(TieKind.Rival, L, true, false,
            "This isn't over. It isn't anywhere near over.",
            "He got one. I'll get the rest.");
        Add(TieKind.Rival, L, false, false,
            "He had the night. The rivalry is longer than one night.",
            "I'll shake his hand and I'll mean about half of it. Then I'll go and get better.");

        // --- A rematch: they have shared a cage before. Even series, then leading, then trailing. ----
        Add(TieKind.Rematch, L, true, true,
            "I'd been here with him before and I wasn't afraid of a repeat.",
            "Somebody booked me against this man again. I'll win again, it's their money.", "I know him and he knows me. Tonight I knew a little more.");
        Add(TieKind.Rematch, L, false, true,
            "We know each other's game by now. He still made me earn it.",
            "When you've shared a cage before, there's nowhere to hide. That's the best kind of fight.", "Second look at the same problem, and I had a better answer ready.", "Nothing in there surprised either of us. That made it a proper fight.");
        Add(TieKind.Rematch, L, true, false,
            "I've seen him before and I'll see him again. Write that down.",
            "Don't read anything into tonight. We're not finished, he knows it.", "Nothing's settled until I say it is. Remember that.");
        Add(TieKind.Rematch, L, false, false,
            "We know each other's game by now, and tonight he had the better read. Back to the gym.", "Nothing I showed him was new, and that's on me.",
            "He was a step ahead of me tonight. I know where that step is. I'll be there next time.", "No secrets left between us, so the next one comes down to who wants it more.");

        Add(TieKind.Rematch, SeriesLean.Leads, true, true,
            "Same man, same result. Somebody should stop booking this.",
            "I beat him before and I'd do it again before breakfast.", "He's a good fighter. I just happen to be better against him.");
        Add(TieKind.Rematch, SeriesLean.Leads, false, true,
            "I beat him before and I wanted to prove it wasn't a one-off. It wasn't.",
            "He had a plan for me this time. The plan wasn't enough.", "He came with something new and I'd done my homework on it.", "Same man across from me and a different night. I'll take it.");
        Add(TieKind.Rematch, SeriesLean.Leads, true, false,
            "Don't read anything into this. I've beaten him before and I'll do it again.",
            "He got lucky tonight. Let him enjoy it, he's not getting another.");
        Add(TieKind.Rematch, SeriesLean.Leads, false, false,
            "I beat him before, and he went away and learned from it. That's a smart man, and I should've known.",
            "He earned that one. I'll go back and work on the part he found.", "I had him figured and he had me better. That happens.");

        Add(TieKind.Rematch, SeriesLean.Trails, true, true,
            "He got his chance last time. This time I took mine.",
            "He beat me before. He can read about this one.");
        Add(TieKind.Rematch, SeriesLean.Trails, false, true,
            "He beat me before and I owed myself this one. I owed it for a long time.",
            "I've thought about that other night for a long while. It's lighter now.", "That one's been waiting a long time. It was worth the wait.");
        Add(TieKind.Rematch, SeriesLean.Trails, true, false,
            "He's beaten me before and I haven't had enough of him yet. Book it again.",
            "He's gone home happy over me before. I won't let that become a habit.", "He's had my number and I'm taking it back, one night at a time.");
        Add(TieKind.Rematch, SeriesLean.Trails, false, false,
            "He's still got my number. I'd better find a new one.",
            "He has the better of me so far. That tells me what to work on.", "He's solved me before and tonight he did it again. Time to become a different problem.");

        // --- Friends --------------------------------------------------------------------
        Add(TieKind.Friend, L, true, true,
            "Friends is friends and business is business. He'd have done the same. We'll talk after.",
            "I'll buy him a drink for being good enough to make that hard.");
        Add(TieKind.Friend, L, false, true,
            "Hardest night of my career to win. Tonight we weren't friends. Tomorrow we are.",
            "It's a strange thing, hurting somebody you'd lend your car to. I'll be calling him.");
        Add(TieKind.Friend, L, true, false,
            "Friend or not, I'm not clapping. Ask me again next week.",
            "I'll shake his hand, and I'll remember tonight every time we're in the same room.");
        Add(TieKind.Friend, L, false, false,
            "If I had to lose to anybody, I'm glad it was him. I won't say that again.",
            "He's a friend. That's why I'll be fine tomorrow.");

        // --- The same room ---------------------------------------------------------------
        Add(TieKind.Teammate, L, true, true,
            "Mat partner or not, the cage doesn't know your name.",
            "We share a room, a coach and a locker. Tonight he didn't get the part about the cage.");
        Add(TieKind.Teammate, L, false, true,
            "We share a room and a coach. Tonight we shared a cage. I'll see him on the mats on Monday.",
            "He's seen every drill I've done because he did them beside me. I had to do something new.");
        Add(TieKind.Teammate, L, true, false,
            "He knows my game because we train it together. That's the last time I show him anything.",
            "That's the last time I hold pads for that man.");
        Add(TieKind.Teammate, L, false, false,
            "He's seen every drill I own, because he did them next to me. Fair enough.",
            "We'll be back in the same room on Monday, and we'll both have learned something.");

        // --- The same country -------------------------------------------------------------
        Add(TieKind.Countryman, L, true, true,
            "Only one of us gets to carry this home. Home will have to choose.");
        Add(TieKind.Countryman, L, false, true,
            "Two from one country in one cage. Whoever lost, home still wins. I hope I made them proud.");
        Add(TieKind.Countryman, L, true, false,
            "My own people watch this on tape for years. I want it back.");
        Add(TieKind.Countryman, L, false, false,
            "Lost to a countryman, so the flag stays in the right hands. I'll be back.");

        return t;
    }
}
