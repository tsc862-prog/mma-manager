using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>
/// The sport's first stories, told in 1993 voice: no ranking, no stream, no weight class, no glove. The cast is the
/// day-one pioneers; the beats are the same observable conditions as today's. Append-only; no digits.
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
            B(1, Cond.Booked, "the garage is told",
                "Quintela and Paranhos are down for the same night",
                "One gym is above a bakery in the north zone and the other has a tin roof, and each has said for years that the other's students learnt their trade from the wrong teacher. Rio has been waiting for somebody to open a garage door on it."),
            B(2, Cond.Won, "the bakery wins",
                "Quintela beats Paranhos: the flour settles",
                "The jiu-jitsu man from above the bakery has put the luta livre man from the tin roof on the floor, and the north zone is loud. Paranhos has asked, in the plain way he asks things, for another night."),
            B(2, Cond.Won, "the tin roof holds", swap: true,
                head: "Paranhos beats Quintela: the roof stays up",
                body: "The luta livre man has answered a decade of bakery talk in the one way the garage respects. Quintela shook his hand and said the flour on the mats was not an excuse."),
            B(3, Cond.Rematch, "the other night",
                "Quintela and Paranhos go again",
                "Two gyms, two traditions, one argument that will not stay settled. Rio has stopped asking who is right and started asking when.")),

        A("hagiwara-ishikari", "Four Courteous Sentences", "Tetsuya Hagiwara", "Ryuhei Ishikari",
            B(1, Cond.Booked, "the test is arranged",
                "Hagiwara and Ishikari are on the same bill",
                "Ryuhei Ishikari said the wrestling halls were theatre. Tetsuya Hagiwara answered in the newspaper, in four courteous sentences, that he would like to be tested. The Association has now arranged the test."),
            B(2, Cond.Won, "the open hand",
                "Hagiwara beats Ishikari: the hall was not theatre",
                "The man from the wrestling halls has answered the man from the karate floor in the ring, and the ring did not need four sentences. Ishikari bowed lower than the custom required."),
            B(2, Cond.Won, "the iron shin", swap: true,
                head: "Ishikari beats Hagiwara: the karate man has his answer",
                body: "Ryuhei Ishikari has proved his point on the best of the wrestling halls, and the Association, which values courtesy over noise, has chosen its words with care."),
            B(3, Cond.Rematch, "the second test",
                "Hagiwara and Ishikari go again",
                "One result has not settled what the newspapers began. The two men, courteous to the last, have agreed to find out.")),

        A("zandvliet-delacroix", "The Letter Left for Last", "Joost Zandvliet", "Ray Delacroix",
            B(1, Cond.SignedWith, "a letter is opened",
                "Zandvliet picks a house",
                "The Dike has stopped deciding and started signing. Three houses wrote to him; one of the letters he left until last is still on the table in Rotterdam."),
            B(2, Cond.Booked, "the answer is posted",
                "Zandvliet and Delacroix are booked",
                "Ray Delacroix said the Dutch have been wrong about kickboxing for ten years and that he would fight Joost Zandvliet anywhere. Zandvliet has not replied. The promoters, who needed no reply, have made the match."),
            B(3, Cond.Won, "the Dike holds",
                "Zandvliet beats Delacroix: nothing got through",
                "The man from the Rotterdam docks has given his answer, which was a result and not a word. Delacroix, blunt and funny to the end, said the Dutch had earned one."),
            B(3, Cond.Won, "Bayou answers", swap: true,
                head: "Delacroix beats Zandvliet: the Dutch have been wrong",
                body: "Ray Delacroix has made his case and, after ten years of saying it, will be insufferable about it. Zandvliet took the result in silence, which is how he takes everything."),
            B(4, Cond.Rematch, "the second letter",
                "Zandvliet and Delacroix meet again",
                "The argument about who is wrong about kickboxing has been settled once and will not stay settled. The Dike, for once, has replied to a letter.")),

        A("mgeladze-orekhov", "A Judging Decision in a Country That No Longer Exists", "Zurab Mgeladze", "Vadim Orekhov",
            B(1, Cond.Booked, "the army championships revisited",
                "Mgeladze and Orekhov are on one card",
                "A judging decision at the army championships, in a country that is gone, and neither man has let the other forget it. The promoters have found a ring and a pair of judges the two of them have not met."),
            B(2, Cond.Won, "the judoka's answer",
                "Mgeladze beats Orekhov: the old decision is overturned in the new language",
                "Zurab Mgeladze has the result he has wanted since the army championships. Vadim Orekhov, a sambo man of few words, asked for the judges' names so that he could thank them."),
            B(2, Cond.Won, "the sambo man's answer", swap: true,
                head: "Orekhov beats Mgeladze: the old decision stands, now with a result behind it",
                body: "Vadim Orekhov has the result he did not have to ask for. Zurab Mgeladze, who has the longer memory, said the longer memory would outlast the longer result."),
            B(3, Cond.Rematch, "the second hearing",
                "Mgeladze and Orekhov go again",
                "Two men from a country that no longer exists, and a quarrel that has outlived it. The new flags in the building have been asked to be neutral.")),

        A("neves-locksmith", "A Long Afternoon of Chess", "Aurelio Neves", "Aurelio Neves",
            B(1, Cond.WonStreak, "somebody finally notices", n: 2,
                head: "Neves wins again: the strip-mall teacher has a following",
                body: "He teaches jiu-jitsu to police officers and insomniacs in a unit between a dry cleaner and a tax office, and the early classes are getting crowded. He treats a fight as a long afternoon of chess, and the afternoons are getting shorter."),
            B(2, Cond.WonStreak, "the locksmith opens another door", n: 4,
                head: "Neves keeps winning: the Locksmith has run out of doors that stay shut",
                body: "The quiet Brazilian is no longer a secret in Los Angeles. Somebody has mentioned a bakery academy in Rio and an armbar, and Neves has not confirmed that he learned it there."),
            B(3, Cond.Retired, "the last class",
                "Neves retires",
                "The Locksmith has closed the strip-mall unit's front door for the last time, and the six a.m. class is now taught by one of his students. The old Crucible field did not expect to miss him as much as they do.",
                cat: NewsCategory.Retirement)),

        A("skaggs-quirke", "One Bracket for Two Unwritten Records", "Dewey Skaggs", "Dermot Quirke",
            B(1, Cond.Booked, "the matchmakers relent",
                "Skaggs and Quirke are booked: two unwritten records, one ring",
                "A Kentucky mechanic and an Irish fairground fighter each say they have not lost, and no one has been able to disprove either. The matchmakers, who have been asked for months to put them in one bracket, have run out of excuses."),
            B(2, Cond.Won, "the mechanic's answer",
                "Skaggs beats Quirke: one of the two records is now written",
                "Dewey Skaggs swung with both hands from the opening second, as promised, and the fairground man was where the punch was. Quirke took the result with a handshake and the quiet of a man who has just learnt what exaggeration costs."),
            B(2, Cond.Won, "the fairground's answer", swap: true,
                head: "Quirke beats Skaggs: the awkward man was somewhere else",
                body: "Dermot Quirke spent the night not being where the punch was, and the Kentucky mechanic's record now has a footnote. Skaggs, who cannot be talked out of anything, asked for the rematch."),
            B(3, Cond.Rematch, "the footnote disputed",
                "Skaggs and Quirke go again",
                "One result did not settle which of the two had been exaggerating. The bar rooms of two continents have taken sides.")),

        A("thanasarn-yoon", "Whoever Drew the Bigger Crowd", "Pongsak Thanasarn", "Tae-gyu Yoon",
            B(1, Cond.Booked, "the temple-fair debt",
                "Thanasarn and Yoon are on the same bill",
                "A small muay thai man stared at the boards on a Bangkok temple-fair bill, and a taekwondo man who broke them remembers him doing it. Somebody has now put the two in a ring."),
            B(2, Cond.Won, "the elbow answers the boards",
                "Thanasarn beats Yoon: the boards drew the bigger crowd, but not tonight",
                "The man from the temple fairs has beaten the fastest kicker in the field, and the Korean team's demonstration reels are a footnote this evening. Yoon said the forms were real and the elbow was too."),
            B(2, Cond.Won, "the crane lands", swap: true,
                head: "Yoon beats Thanasarn: the forms were real",
                body: "Tae-gyu Yoon says the sport is a test of whether the forms were real, and tonight he has an answer. Thanasarn took it quietly, and glanced at the crowd, which was a good one."),
            B(3, Cond.Rematch, "the second night",
                "Thanasarn and Yoon go again",
                "Two men who remember the same temple fair differently have been asked to settle it twice.")),
    };
}
