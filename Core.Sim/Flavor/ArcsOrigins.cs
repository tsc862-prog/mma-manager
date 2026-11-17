using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>
/// The sport's first stories, told in 1993 voice: no ranking, no stream, no weight class, no glove, no belt. The
/// cast is the day-one pioneers. The houses of 1993 pair their men on the night (a bracket is announced with an
/// empty card and the garage matches rival gyms on the day), so no story opens on "booked": it opens when the two
/// meet, or when the lead is winning. Same rules as today's table: the text says only what the condition proves
/// (no house, city or gym, no age), no digits, no first/only/never. Append-only.
/// </summary>
public static class ArcsOrigins
{
    private static Beat B(int slot, Cond when, string tag, string head, string body,
        bool swap = false, int n = 0, string? org = null, NewsCategory cat = NewsCategory.Milestone) =>
        new(slot, when, tag, head, body, cat, swap, n, org);

    private static Arc A(string id, string title, string lead, string foil, params Beat[] beats) =>
        new(id, title, WorldMode.Origins, lead, foil, beats);

    public static IReadOnlyList<Arc> All { get; } = new Arc[]
    {
        A("quintela-paranhos", "The Bakery and the Tin Roof", "Odair Quintela", "Heitor Paranhos",
            B(1, Cond.Won, "the bakery wins",
                "Quintela beats Paranhos: the flour settles",
                "The jiu-jitsu man from above the bakery has put the luta livre man from the tin roof on the floor, and the north zone is loud. The tin roof will have a view."),
            B(1, Cond.Won, "the tin roof holds", swap: true,
                head: "Paranhos beats Quintela: the roof stays up",
                body: "The luta livre man has answered a decade of bakery talk in the one way the garage respects. Quintela shook his hand and said the flour on the mats was not an excuse."),
            B(1, Cond.WonStreak, "the bakery is busy", n: 2,
                head: "Quintela keeps winning: the tin roof is paying attention",
                body: "Odair Quintela keeps winning, and the tin roof has noticed."),
            B(2, Cond.Rematch, "the other night",
                "Quintela and Paranhos go again",
                "Two gyms, two traditions, one argument that will not stay settled. No one asks who is right any more; they ask when."),
            B(3, Cond.Retired, "the last argument", swap: true,
                head: "Paranhos retires: the tin roof goes quiet",
                body: "Heitor Paranhos has stopped fighting, and the north zone is arguing about whether the argument can go on without him.",
                cat: NewsCategory.Retirement)),

        A("hagiwara-ishikari", "Four Courteous Sentences", "Tetsuya Hagiwara", "Ryuhei Ishikari",
            B(1, Cond.Won, "the open hand",
                "Hagiwara beats Ishikari: the hall was not theatre",
                "The man from the wrestling halls has answered the man from the karate floor in the ring, and the ring did not need four sentences. Ishikari bowed lower than the custom required."),
            B(1, Cond.Won, "the iron shin", swap: true,
                head: "Ishikari beats Hagiwara: the karate man has his answer",
                body: "Ryuhei Ishikari has proved his point on the best of the wrestling halls, and the sport, which values courtesy over noise, has chosen its words with care."),
            B(1, Cond.WonStreak, "the test continues", n: 2,
                head: "Hagiwara keeps winning: he would still like to be tested",
                body: "Tetsuya Hagiwara answered Ryuhei Ishikari in the newspaper in four courteous sentences, and has been answering everybody else in the ring since."),
            B(2, Cond.Rematch, "the second test",
                "Hagiwara and Ishikari go again",
                "One result has not settled what the newspapers began. The two men, courteous to the last, have agreed to find out."),
            B(3, Cond.Retired, "the long answer", swap: true,
                head: "Ishikari retires: the test goes unfinished",
                body: "Ryuhei Ishikari has hung up the iron shin. Tetsuya Hagiwara sent a note, in four courteous sentences, that said he would have liked one more.",
                cat: NewsCategory.Retirement)),

        A("zandvliet-delacroix", "The Letter Left for Last", "Joost Zandvliet", "Ray Delacroix",
            B(1, Cond.SignedWith, "a letter is opened",
                "Zandvliet picks a house",
                "The Dike has stopped deciding and started signing, with {AOrg}. Three houses wrote to him; the letter he left until last is still on the table in Rotterdam."),
            B(2, Cond.Won, "the Dike holds",
                "Zandvliet beats Delacroix: nothing got through",
                "The man from the Rotterdam docks has given his answer, which was a result and not a word. Delacroix, blunt and funny to the end, said the Dutch had earned one."),
            B(2, Cond.Won, "Bayou answers", swap: true,
                head: "Delacroix beats Zandvliet: the Dutch have been wrong",
                body: "Ray Delacroix has made his case and, after ten years of saying it, will be insufferable about it. Zandvliet took the result in silence, which is how he takes everything."),
            B(3, Cond.Rematch, "the second letter",
                "Zandvliet and Delacroix meet again",
                "The argument about who is wrong about kickboxing has been settled once and will not stay settled. The Dike, for once, has replied to a letter.")),

        A("mgeladze-orekhov", "A Judging Decision in a Country That No Longer Exists", "Zurab Mgeladze", "Vadim Orekhov",
            B(1, Cond.Won, "the judoka's answer",
                "Mgeladze beats Orekhov: the old decision is overturned in the new language",
                "Zurab Mgeladze has the result he has wanted since the army championships. Vadim Orekhov, a sambo man of few words, asked for the judges' names so that he could thank them."),
            B(1, Cond.Won, "the sambo man's answer", swap: true,
                head: "Orekhov beats Mgeladze: the old decision stands, now with a result behind it",
                body: "Vadim Orekhov has the result he did not have to ask for. Zurab Mgeladze, who has the longer memory, said the longer memory would outlast the longer result."),
            B(1, Cond.WonStreak, "the old score", n: 2,
                head: "Orekhov keeps winning: the army championships are a long way back",
                body: "Vadim Orekhov keeps winning. Zurab Mgeladze has a long memory for a judging decision from a country that no longer exists."),
            B(2, Cond.Rematch, "the second hearing",
                "Mgeladze and Orekhov go again",
                "Two men from a country that no longer exists, and a quarrel that has outlived it. The new flags in the building have been asked to be neutral."),
            B(3, Cond.Retired, "the longer memory",
                "Mgeladze retires",
                "Zurab Mgeladze has hung up the judo jacket. The longer memory will have to outlast the career, and Vadim Orekhov, a sambo man of few words, has said he will miss the argument.",
                cat: NewsCategory.Retirement)),

        A("neves-locksmith", "A Long Afternoon of Chess", "Aurelio Neves", "Aurelio Neves",
            B(1, Cond.WonStreak, "somebody notices", n: 2,
                head: "Neves wins again: the strip-mall teacher has a following",
                body: "He teaches jiu-jitsu to police officers and insomniacs in a unit between a dry cleaner and a tax office, and the early classes are getting crowded. He treats a fight as a long afternoon of chess, and the afternoons are getting shorter."),
            B(2, Cond.WonStreak, "the locksmith opens another door", n: 4,
                head: "Neves keeps winning: the Locksmith has run out of doors that stay shut",
                body: "The quiet Brazilian is no longer a secret. Somebody has mentioned a bakery academy and an armbar, and Neves has not confirmed that he learned it there."),
            B(3, Cond.Retired, "the last class",
                "Neves retires",
                "The Locksmith has closed the strip-mall unit's front door for the last time, and the six a.m. class is now taught by one of his students.",
                cat: NewsCategory.Retirement)),

        A("skaggs-quirke", "One Bracket for Two Unwritten Records", "Dewey Skaggs", "Dermot Quirke",
            B(1, Cond.Won, "the mechanic's answer",
                "Skaggs beats Quirke: one of the two records is now written",
                "Dewey Skaggs swung with both hands from the opening second, as promised, and the fairground man was where the punch was. Quirke took the result with a handshake and the quiet of a man who has just learnt what exaggeration costs."),
            B(1, Cond.Won, "the fairground's answer", swap: true,
                head: "Quirke beats Skaggs: the awkward man was somewhere else",
                body: "Dermot Quirke spent the night not being where the punch was, and the Kentucky mechanic's record now has a footnote. Skaggs, who cannot be talked out of anything, asked for the rematch."),
            B(1, Cond.WonStreak, "the record grows", n: 2,
                head: "Skaggs keeps winning: the unwritten record is being written",
                body: "Dewey Skaggs keeps collecting results for a record no one wrote down. Dermot Quirke, who says the same of his own, may have noticed."),
            B(2, Cond.Rematch, "the footnote disputed",
                "Skaggs and Quirke go again",
                "One result did not settle which of the two had been exaggerating. The bar rooms of two countries have taken sides."),
            B(3, Cond.Retired, "the record is closed", swap: true,
                head: "Quirke retires: one unwritten record is closed",
                body: "Dermot Quirke has stopped fighting at the fairs and in the ring. Whatever he says about his record now, no one will be able to disprove it, which is how he likes it.",
                cat: NewsCategory.Retirement)),

        A("thanasarn-yoon", "Whoever Drew the Bigger Crowd", "Pongsak Thanasarn", "Tae-gyu Yoon",
            B(1, Cond.Won, "the elbow answers the boards",
                "Thanasarn beats Yoon: the boards drew the bigger crowd, but not this time",
                "The man from the temple fairs has beaten the fastest kicker in the field, and the demonstration team's board-breaking reels are a footnote now. Yoon said the forms were real and the elbow was too."),
            B(1, Cond.Won, "the crane lands", swap: true,
                head: "Yoon beats Thanasarn: the forms were real",
                body: "Tae-gyu Yoon says the sport is a test of whether the forms were real, and now he has an answer. Thanasarn took it quietly, and glanced at the crowd, which was a good one."),
            B(1, Cond.WonStreak, "the elbow keeps working", n: 2,
                head: "Thanasarn keeps winning: the small man with the elbow is no secret",
                body: "Pongsak Thanasarn has not forgotten that the boards drew the bigger crowd, and he is working on that too."),
            B(2, Cond.Rematch, "the second night",
                "Thanasarn and Yoon go again",
                "Two men who remember the same temple fair differently have been asked to settle it twice."),
            B(3, Cond.Retired, "the last elbow",
                "Thanasarn retires",
                "Pongsak Thanasarn has stopped fighting for meal money, and the temple fairs have noticed. The boards, a Korean team would like it noted, still draw a crowd.",
                cat: NewsCategory.Retirement)),
    };
}
