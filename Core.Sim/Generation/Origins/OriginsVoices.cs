using MmaManager.Core.Model;
using MmaManager.Core.Sim.Flavor;

namespace MmaManager.Core.Sim.Generation.Origins;

/// <summary>What kind of period line this is.</summary>
public enum OriginsVoiceKind
{
    /// <summary>One fighter on another's art: style against style.</summary>
    StyleTrashTalk,
    /// <summary>The promoter selling the night.</summary>
    PromoterBallyhoo,
    /// <summary>A newspaper columnist who thinks it is a disgrace, or a joke.</summary>
    ColumnistSceptic,
    /// <summary>A politician, a clergyman or a regulator calling for a ban.</summary>
    BanCall,
    /// <summary>A news headline template for a night's results or a story.</summary>
    Headline,
    /// <summary>A commentator or magazine voice, for the tape and the poster.</summary>
    Broadcast,
}

/// <summary>
/// Period voices for the 1993 start: the lines the world says when it has no internet, no social
/// media and no streaming; only newspapers, late-night cable, pay-per-view, VHS tapes and
/// martial-arts magazines. Written to sit beside <see cref="Quotes"/> without editing it: fighters'
/// reactions are keyed by the same <see cref="Disposition"/> and <see cref="Moment"/>, and the rest
/// are a plain keyed table with {A}, {B}, {City}, {Org}, {Style} slots.
///
/// Picks are deterministic off an integer key (a fighter id plus a day number, for instance), exactly
/// as <see cref="Quotes.Say"/> does, so a save reloads word for word. No randomness here.
/// </summary>
public static class OriginsVoices
{
    /// <summary>A line for a kind, picked by key; slots left unfilled when the caller passes null.</summary>
    public static string Pick(OriginsVoiceKind kind, int key, string? a = null, string? b = null, string? city = null, string? org = null, string? style = null)
    {
        var pool = Pool[kind];
        string line = pool[(int)(Mix((uint)key, (uint)kind) % (uint)pool.Count)];
        return Fill(line, a, b, city, org, style);
    }

    /// <summary>Fill the {A} {B} {City} {Org} {Style} slots of a template.</summary>
    public static string Fill(string template, string? a = null, string? b = null, string? city = null, string? org = null, string? style = null) =>
        template.Replace("{A}", a ?? "the challenger").Replace("{B}", b ?? "his opponent")
                .Replace("{City}", city ?? "town").Replace("{Org}", org ?? "the promotion").Replace("{Style}", style ?? "his style");

    /// <summary>A fighter's own line for a moment, in the era's register. Falls back to the quiet professional.</summary>
    public static string Say(Disposition d, Moment m, int key)
    {
        if (!Reactions.TryGetValue((d, m), out var pool) && !Reactions.TryGetValue((Disposition.QuietProfessional, m), out pool))
            return string.Empty;
        return $"“{pool[(int)(Mix((uint)key, (uint)m * 31u) % (uint)pool.Count)]}”";
    }

    private static uint Mix(uint id, uint salt)
    {
        unchecked
        {
            uint h = id * 2246822519u ^ (salt * 0x85EBCA6Bu);
            h ^= h >> 13; h *= 0x27D4EB2Fu;
            h ^= h >> 16;
            return h;
        }
    }

    // ------------------------------------------------------------------ keyed tables
    public static IReadOnlyDictionary<OriginsVoiceKind, IReadOnlyList<string>> Pool { get; } =
        new Dictionary<OriginsVoiceKind, IReadOnlyList<string>>
        {
            [OriginsVoiceKind.StyleTrashTalk] = new[]
            {
                "“Boxing is the only honest fighting there is. Put a wrestler in front of me and see how long he lasts standing up.” — {A}",
                "“{B} has never been hit by a man who meant it. I mean it. That is the whole difference between us.” — {A}",
                "“A man who spends his life on the floor is a man who has never been punished for it. Come stand with me.” — {A}",
                "“Thirty years of judo and nobody has ever asked me to try it on a person. Now they are asking. {B} will be the first to find out.” — {A}",
                "“I have broken cinder blocks, boards and a world champion's jaw. {B} is not any of those.” — {A}",
                "“Wrestlers are very strong and very slow. I will be neither.” — {A}",
                "“They say the kick is a show. I have a hundred and twenty fights that say otherwise.” — {A}",
                "“My teacher told me that a man who cannot be thrown cannot be beaten. Let us find out what he forgot to mention.” — {A}",
                "“{B} calls my art theatre. I will be happy to bow to him before I prove otherwise.” — {A}",
                "“No gloves, no rounds, no excuses. The best style will go home with the money, and it will not be {B}'s.” — {A}",
                "“I do not hate {B}. I hate the idea that what he does is better than what my teacher gave his life to. That I will fix on Saturday.” — {A}",
                "“A sumo man does not fall down. Whatever else happens in {City}, nobody will push me over.” — {A}",
                "“In my gym we did the arm bar eleven thousand times before anyone showed us a punch. {B} will not enjoy it.” — {A}",
                "“{B} says there are no rules. There are always rules. The first one is that the man who is hit loses.” — {A}",
            },
            [OriginsVoiceKind.PromoterBallyhoo] = new[]
            {
                "“For the first time in history, eight men from eight disciplines, one night, no weight classes, no judges, and no excuses. Order it on your cable box tonight.” — {Org}",
                "“Who wins? Nobody knows. We are about to find out, live, on pay-per-view, in {City}.” — {Org}",
                "“This is not wrestling and it is not boxing. It is the answer to an argument that is older than the telephone.” — {Org}",
                "“The tape of this night will be in every martial-arts shop in America by Christmas. You will want to say you were there.” — {Org}",
                "“Karate against judo. Boxing against the mat. Tonight the arguments end, and they end in {City}.” — {Org}",
                "“Doors at six, first bell at seven, and the last man standing by midnight. Tickets from the box office and your local cable operator.” — {Org}",
                "“Every man on this card has a teacher who says his art is the best. Tonight they find out, and so do you.” — {Org}",
                "“No ropes to hold, no round bells to save you. Only the man in front of you.” — {Org}",
                "“Sold out in {City} in nine days. The line at the box office began on a Tuesday.” — {Org}",
                "“We are not asking you to like it. We are asking you to watch it.” — {Org}",
                "“Call the number on your screen. Lines are open now; the first bout begins at seven sharp.” — {Org}",
                "“Bring a friend, bring a doubter. We guarantee that nobody leaves in the first ten minutes.” — {Org}",
            },
            [OriginsVoiceKind.ColumnistSceptic] = new[]
            {
                "“It is not a sport. A sport has a referee who may stop it, a doctor who may refuse it and a rulebook that runs to more than one page. This has none of the three.” — columnist, {City} Courier",
                "“I watched the tape twice. I am told it is called a pay-per-view. I paid, and I am still waiting for the sport.” — the Evening Banner's sporting desk",
                "“If this is the future of combat sports, the past will do very well for me.” — {City} Gazette",
                "“Eight men and one night: the sole rule is that the winner should be able to stand up afterwards, and even that is optional.” — columnist, the Morning Dispatch",
                "“The promoters say it settles which fighting style is best. A cheaper way to settle that would be to ask the fighters, who would each answer: mine.” — columnist, {City} Courier",
                "“Somebody ought to explain to the sponsors that blood is not a marketing opportunity.” — {City} Gazette leader column",
                "“My nephew rented the video. I had him return it by Tuesday.” — letters page, the Daily Bulletin",
                "“Disgraceful, ridiculous, and, I regret to report, quite gripping for the first quarter of an hour.” — television critic, the Sunday Ledger",
                "“One wonders what would happen if the Marquess of Queensberry saw this, and then one stops wondering.” — columnist, the Morning Dispatch",
                "“The winner of last night's contest was the man who put his opponent on the floor and could not be got off it. If that is a sport, so is moving house.” — {City} Gazette",
                "“A circus without a clown, unless one counts the organisers.” — arts and leisure page, the Evening Banner",
                "“I should like to see it banned, and I should like to see it again, and I am embarrassed by both.” — columnist, the Sunday Ledger",
            },
            [OriginsVoiceKind.BanCall] = new[]
            {
                "“This is human cockfighting, and I will not have it in my state.” — Senator, in a letter to the cable operators",
                "“I have asked the Attorney General to determine whether these events are consistent with the laws governing public entertainment.” — Governor's office",
                "“Any broadcaster carrying this will hear from me, in writing, by Friday.” — member of the city council, {City}",
                "“If a man were to do this to a dog he would go to prison. We should find out why it is any different when it is done to a man.” — pastor, {City}",
                "“I call on the carriers to remove this programme from their pay-per-view services and on parents to remove it from their homes.” — Senator",
                "“The medical association has written to the Minister of Sport asking for an inquiry. The Minister has not yet replied.” — {City} Gazette",
                "“These shows are not licensed, are not supervised and are not welcome in this city.” — {City} licensing board",
                "“We will not issue a permit for anything in which one man may continue to strike another who is on the ground.” — mayor's office, {City}",
                "“A ban is not censorship. It is a refusal to be entertained by injury.” — Member of Parliament, in the House",
                "“The cable operators have been given until the end of the month to explain why this is on their menu.” — consumer affairs committee",
                "“A sport that does not wish to be examined should not be sold in the shops.” — school governors' association, in an open letter",
                "“I am told these men are professionals. I should like to see their professional bodies, their insurance and their referee's qualifications.” — Member of Parliament",
            },
            [OriginsVoiceKind.Headline] = new[]
            {
                "{A} submits {B} in {City}",
                "{A} stops {B}; crowd on its feet in {City}",
                "{Org} night ends in a draw: both men advance to the doctor",
                "{A} wins the {Org} bracket on a night of surprises",
                "{A} beats {B} and leaves {City} with the purse",
                "{B} taps to {A} in the second round of the evening",
                "{Org} sells out {City} for a second night",
                "Three thousand watch {A} and {B} settle it in {City}",
                "{A} says the sport has found its first champion; {B} disagrees",
                "{Org} announces its next night for {City}; no date for a rematch",
                "{A} calls out every man on the card",
                "A draw at the limit: {A} and {B} shake hands in {City}",
                "{City} council to review licence for {Org} shows",
                "{Org} night banned in {City}; promoters seek another venue",
                "Cable carriers weigh whether to drop {Org}",
                "{A} abandons boxing for the {Org} ring",
                "{B} withdraws from {Org} bill on doctor's advice; replacement named",
                "Pay-per-view buys for {Org} night top expectations, say organisers",
                "{Org} tape on sale in martial-arts shops from Friday",
                "{A}: 'I came for the money, I stayed for the fight'",
                "{Org} founder defends the sport before the committee: 'We have never hidden anything'",
                "{A}, former champion of another sport, makes {Org} debut in {City}",
            },
            [OriginsVoiceKind.Broadcast] = new[]
            {
                "And there is the bell, and I do not think I have ever seen two men who are less alike. {A} from one end of the world, {B} from the other.",
                "{A} is giving away eighty pounds and, he assures us, he does not mind.",
                "He has him by the wrist, he has him by the collar, and the crowd in {City} has gone quite silent.",
                "That is a kick that you could hear in the car park.",
                "No, no, no, he has been told the rope is not a hold, and the referee is telling him again.",
                "Nobody has seen this before, ladies and gentlemen. Nobody at all.",
                "{A} is turning his body to the left, which is exactly what {B} wanted.",
                "There is no clock on this fight but there is one on that man's lungs.",
                "A tap! A tap! The doctor is in, and I believe the bout is over.",
                "Every man in this room is in the middle of an argument, and the argument is called {Style}.",
            },
        };

    /// <summary>Reactions keyed like <see cref="Quotes"/>: who they are and what just happened. Era-true: nothing about ratings, tweets, streams or belts.</summary>
    public static IReadOnlyDictionary<(Disposition, Moment), IReadOnlyList<string>> Reactions { get; } =
        new Dictionary<(Disposition, Moment), IReadOnlyList<string>>
        {
            [(Disposition.Loudmouth, Moment.WinFinish)] = new[]
            {
                "I told every newspaper in the country how it would go. Check the clippings.",
                "Now somebody ring my teacher and tell him he was right.",
            },
            [(Disposition.Showman, Moment.WinFinish)] = new[]
            {
                "They will be showing that on the tape for years. I hope they have the good angle.",
                "A man pays for a seat in the house and he wants his money's worth. There it is.",
            },
            [(Disposition.Hothead, Moment.WinFinish)] = new[]
            {
                "He laughed at my gym. I will see who is laughing in the morning papers.",
                "I did not come to be polite.",
            },
            [(Disposition.QuietProfessional, Moment.WinFinish)] = new[]
            {
                "I trained for the man I thought he was. It turned out he was easier.",
                "It was a good fight. I would like to go home now.",
            },
            [(Disposition.Student, Moment.WinFinish)] = new[]
            {
                "My teacher said the first ten seconds decide it. He was off by about nine.",
                "I had never tried that on a person. It works the way the book says.",
            },
            [(Disposition.Warhorse, Moment.WinFinish)] = new[]
            {
                "Twelve years of waiting and one afternoon. I would do it again tomorrow.",
                "Give me the next man. I do not mind who he is.",
            },
            [(Disposition.CompanyMan, Moment.WinFinish)] = new[]
            {
                "This win belongs to the hall. They gave me the ring, I gave them the night.",
                "The people in this building trusted me. I only did the work.",
            },
            [(Disposition.Mercenary, Moment.WinFinish)] = new[]
            {
                "It was a short night and a long cheque. That is how it should be.",
                "I fought for the purse and the purse was good. Tell your friends.",
            },
            [(Disposition.Loudmouth, Moment.Loss)] = new[]
            {
                "He got lucky. If you want it again, I will bring the cameras this time.",
                "I will be back, and the next time he will be the one apologising.",
            },
            [(Disposition.Showman, Moment.Loss)] = new[]
            {
                "That is the sport. One night you are on the poster, the next you are on the floor.",
                "They will remember it, and so will I. Let me know when the rematch is on pay-per-view.",
            },
            [(Disposition.Hothead, Moment.Loss)] = new[]
            {
                "I want him again. I do not want to wait for the paperwork.",
                "Do not tell me about respect. I lost, and that is all there is to it.",
            },
            [(Disposition.QuietProfessional, Moment.Loss)] = new[]
            {
                "He was better tonight. I shook his hand and I will go back to work.",
                "I learned something I could not have learned in the gym.",
            },
            [(Disposition.Student, Moment.Loss)] = new[]
            {
                "I will watch the tape. There was a mistake in the third minute and I want to find it.",
                "It was something I had never seen before. I will not forget it.",
            },
            [(Disposition.Warhorse, Moment.Loss)] = new[]
            {
                "I have been beaten before. I came back each time.",
                "That is why they call it a fight and not a rehearsal.",
            },
            [(Disposition.CompanyMan, Moment.Loss)] = new[]
            {
                "I let down the hall tonight. I will make it up to them.",
                "Whatever the papers say, I fought for the people who bought a ticket.",
            },
            [(Disposition.Mercenary, Moment.Loss)] = new[]
            {
                "I will take the purse and the lesson, in that order.",
                "A loss pays the same as a win, in this sport. That is something nobody has fixed yet.",
            },
            [(Disposition.Loudmouth, Moment.Signed)] = new[]
            {
                "I have signed. Tell every other art in the world to book a seat.",
            },
            [(Disposition.QuietProfessional, Moment.Signed)] = new[]
            {
                "I signed it. I start training Monday.",
            },
            [(Disposition.Mercenary, Moment.Signed)] = new[]
            {
                "The figure was right. I do not need the speech.",
            },
            [(Disposition.Warhorse, Moment.Signed)] = new[]
            {
                "Give me the hardest man you have, and put him on first.",
            },
            [(Disposition.Student, Moment.Signed)] = new[]
            {
                "It is the only place in the world where I can test what my teacher taught.",
            },
        };

    /// <summary>The era's own headline kinds, for a quick count in tests and in the showcase.</summary>
    public static int LineCount =>
        Pool.Values.Sum(p => p.Count) + Reactions.Values.Sum(p => p.Count);
}
