using MmaManager.Core.Model;
using MmaManager.Core.Sim.Geography;

namespace MmaManager.Core.Sim.Generation.Origins;

/// <summary>The single art each pioneer comes from. In 1993 nobody has more than one.</summary>
public enum OriginsDiscipline
{
    Boxing, Kickboxing, MuayThai, Karate, Taekwondo, Capoeira,
    Judo, FreestyleWrestling, GrecoRomanWrestling, Sumo, Ssireum,
    JiuJitsu, Sambo, LutaLivre, ProWrestling, Brawler,
}

/// <summary>
/// A day-one pioneer: the same <see cref="DesignedFighter"/> row the designed roster uses (so the
/// later wiring is a plain map), plus what that row has no room for: the discipline, the natural
/// size (this is an open-weight era; the row's WeightClass is only the nearest class), a one-line hook
/// and optional skill tweaks for the few men whose résumé does not fit their discipline's template.
/// </summary>
public sealed record OriginsPioneer(
    DesignedFighter Fighter, OriginsDiscipline Discipline, int HeightCm, int WeightKg, string Hook,
    (SkillId Skill, int Offset)[]? Tweaks = null)
{
    public string FullName => $"{Fighter.First} {Fighter.Last}";
    public int WeightLbs => (int)Math.Round(WeightKg * 2.20462);
    public IReadOnlyDictionary<SkillId, int> Skills => OriginsPioneers.SkillsOf(this);
}

/// <summary>A training room, named in the bios and the camp-home table.</summary>
public sealed record OriginsGym(string Name, string City, string Lore);

/// <summary>
/// The day-one fighters of the Origins timeline (1 January 1993): forty-odd single-discipline
/// specialists, all men (women's MMA arrives decades later), all invented. Skills are lopsided on
/// purpose: <see cref="SkillsOf"/> turns a discipline profile and an overall level into a spread in
/// which the art's strengths stand far above everything else. No randomness; the generator is not
/// involved. Append-only.
/// </summary>
public static class OriginsPioneers
{
    /// <summary>The designed rows' reference date.</summary>
    public static readonly DateOnly StartDate = new(1993, 1, 1);

    // ------------------------------------------------------------------ the table
    public static IReadOnlyList<OriginsPioneer> All { get; } = new OriginsPioneer[]
    {
        // ============ CRU: the eight-man spectacle (Los Angeles) ============
        P("Walt", "Corbin", "The Alderman", "USA", OriginsDiscipline.Boxing, 188, 92, 34, 60, "CRU", 22, 0, 0,
            "A Brooklyn club heavyweight who spent twelve years one win from a title shot.",
            "Walt Corbin boxed for twelve years out of a Brooklyn gym and retired with forty-one wins, nine losses and no title fight. The papers called him the Alderman because he was always one vote short. He accepted the Crucible's invitation because the purse was larger than anything boxing ever paid him, and because he wants to know what happens when the other man is allowed to grab."),
        P("Cade", "Hollister", "Farmhand", "USA", OriginsDiscipline.FreestyleWrestling, 178, 83, 26, 68, "CRU", 14, 0, 0,
            "An Iowa state champion who has never thrown a punch at a person and does not plan to start.",
            "A two-time conference champion at an Iowa state university who did not lose a dual meet in his senior year. Hollister's plan is to put every opponent on his back and keep him there until somebody makes him stop. He drove to Los Angeles with his college roommate Mitch Eversole and a cooler of sandwiches."),
        P("Tae-gyu", "Yoon", "The Crane", "KOR", OriginsDiscipline.Taekwondo, 181, 73, 27, 50, "CRU", 10, 0, 0,
            "The fastest kicker in the field, who has never been hit without a mouthguard.",
            "Four-time national taekwondo champion of South Korea and a member of a touring demonstration team that kicked boards in twelve countries. Yoon is the fastest and most flexible man in the Crucible field, and perhaps the only one who has never been punched without a mouthguard. He says the sport is a test of whether the forms were real."),
        P("Lemalu", "Tofaeono", "The Island", "SAM", OriginsDiscipline.Sumo, 193, 168, 31, 52, "CRU", 30, 0, 0,
            "A 370-pound sumo washout whose knee ended a career in Tokyo's third division.",
            "Born in a village outside Apia, Tofaeono spent six years in a Tokyo sumo stable and reached the third-highest division before a knee gave out. He weighs 168 kilos, can move a refrigerator with one shove and cannot run a hundred metres. The Crucible's promoters booked him for his size before they checked whether he could fight."),
        P("Lyle", "Brannock", "Sideshow", "USA", OriginsDiscipline.ProWrestling, 185, 108, 36, 50, "CRU", 30, 0, 0,
            "A carnival-circuit pro wrestler who spent a decade taking on local challengers for real money.",
            "Brannock started in a travelling athletic show where local men were paid to try to last three minutes with him, and he learned the hard way what the difference is between a hold and a pose. Later, as a professional wrestler in the territories, he was the man the promoters sent to settle arguments in the back of the building. He is thirty-six and delighted that someone finally wants to see it.",
            new[] { (SkillId.Heart, 8) }),
        P("Dewey", "Skaggs", "The Windmill", "USA", OriginsDiscipline.Brawler, 180, 88, 29, 44, "CRU", 18, 0, 0,
            "Reputedly undefeated across several hundred parking-lot arguments in three Kentucky counties.",
            "The reputation, not a record, got Skaggs his invitation: a Kentucky mechanic who has been settling things outside bars since high school and who says he has never lost, though nobody wrote it down. He swings with both hands from the first second and cannot be talked out of it. The Crucible's matchmakers say he is the one man in the field who truly does not know what he does not know."),
        P("Terry", "Nakahara", "Stonehand", "USA", OriginsDiscipline.Karate, 176, 78, 30, 58, "CRU", 16, 0, 0,
            "A Hawaii-born full-contact karate champion who breaks cinder blocks for fun and tournaments for a living.",
            "Nakahara won three national full-contact karate titles out of a dojo in Honolulu, fighting bare-fisted to the body and with kicks to the head. He is calm, courteous and fast in a straight line. He has never fought a man who was allowed to take him to the floor."),
        P("Aurelio", "Neves", "The Locksmith", "BRA", OriginsDiscipline.JiuJitsu, 182, 83, 33, 74, "CRU", 8, 0, 0,
            "A quiet Brazilian black belt who teaches six a.m. classes in a California strip mall.",
            "Neves left Niteroi for Los Angeles at twenty-six and teaches jiu-jitsu to police officers, teenagers and the occasional insomniac in a strip-mall unit between a dry cleaner and a tax office. He treats a fight as a long afternoon of chess. Nobody in the Crucible field has heard of him, and he is not in a hurry to change that."),
        P("Gaston", "Thibodeau", "Le Marteau", "CAN", OriginsDiscipline.Judo, 188, 102, 31, 62, "CRU", 12, 0, 0,
            "A Quebec judoka who finished fifth at a world championship and has not been thrown by anyone under a hundred kilos.",
            "A heavyweight judoka from Trois-Rivieres who reached the world championship quarter-finals and fifth place, and who has been told for ten years that there is no money in it. Thibodeau throws large men and is, to his own surprise, a very good sport about being hit. He signed with the Crucible after a long phone call with his mother."),
        P("Rogelio", "Baeza", "Hijo del Trueno", "MEX", OriginsDiscipline.ProWrestling, 173, 80, 28, 48, "CRU", 20, 0, 0,
            "A masked Mexico City wrestler who has agreed to compete without the mask.",
            "Baeza is the son of a famous masked wrestler and has worn the family hood in the ring since he was eighteen. He agreed to fight in the Crucible with his face showing, which he says is the hardest part of the contract. He is quick, flexible and utterly at home in a crowd, if not yet in a fight where the other man will not cooperate."),

        // ============ SFA: the respectful ring (Tokyo) ============
        P("Tetsuya", "Hagiwara", "The Open Hand", "JPN", OriginsDiscipline.ProWrestling, 183, 92, 30, 72, "SFA", 38, 0, 0,
            "A wrestling-hall ace who insists the holds were never fake in his gym.",
            "Hagiwara debuted in a professional wrestling hall at nineteen and spent most of his career in the back rooms learning the holds that were never in the script. Taught by Goro Kawabata since the age of nineteen, he is quick, patient and unusually hard to hurt. Many of the Association's fans first saw him in the ring on television, and they came because they wanted to see him tested.",
            new[] { (SkillId.KickTechnique, 20), (SkillId.SubmissionOffense, 6) }),
        P("Ryuhei", "Ishikari", "Iron Shin", "JPN", OriginsDiscipline.Karate, 180, 80, 28, 62, "SFA", 20, 0, 0,
            "A full-contact karate champion who says the wrestling halls are theatre.",
            "Ishikari took two national full-contact karate titles in Hokkaido and arrived in Tokyo to prove that real striking beats choreography. He has said, politely and often, that the Association's wrestling stars are performers. Tetsuya Hagiwara was the first of them to answer him in writing.",
            null, "Hagiwara"),
        P("Masashi", "Tsukamoto", "The Gate", "JPN", OriginsDiscipline.Judo, 178, 95, 29, 68, "SFA", 24, 0, 0,
            "An Olympic-level judoka who walked off the national team over a judging decision.",
            "Tsukamoto lost an Olympic semifinal on a decision the judges later admitted was wrong, and walked off the national team the following season. He is a thick-necked, unhurried man with a throw that has put heavyweights through tatami mats. He is the Association's best answer to every critic who says the sport has no technique."),
        P("Kiyoshi", "Aizawa", "Yamakaze", "JPN", OriginsDiscipline.Sumo, 187, 155, 31, 54, "SFA", 34, 0, 0,
            "A retired sumo wrestler of the second division who gets cheered at every airport.",
            "Aizawa reached the second division of professional sumo, retired with an injured back and has been recognised on every Tokyo train platform since. He weighs 155 kilos and moves with the small, patient steps of a man who has spent fifteen years in a ring the size of a bedroom. He cannot box and will tell you so."),
        P("Shuhei", "Morikawa", null, "JPN", OriginsDiscipline.FreestyleWrestling, 172, 68, 26, 60, "SFA", 12, 0, 0,
            "An Olympic hopeful in freestyle wrestling with no interest in being hit.",
            "Morikawa was twice a national freestyle champion and the alternate on an Olympic squad. He wrestles with a shyness that looks like caution and is actually arithmetic. He joined the Association because it offered more money than the national federation and a ring in which the rope is a rule, not a rescue."),
        P("Naoto", "Segawa", "Needle", "JPN", OriginsDiscipline.Kickboxing, 175, 70, 25, 58, "SFA", 18, 0, 0,
            "A kickboxer whose jab has been compared to a sewing machine.",
            "Segawa moved to kickboxing from school karate and had won sixteen of nineteen amateur fights by the time he was twenty-three. He is tall for his weight, jabs from the end of his arm and exits before the answer arrives. He has been told that a good wrestler could end him in ten seconds and has said that is the reason he is coming."),
        P("Goro", "Kawabata", "The Old Rope", "JPN", OriginsDiscipline.ProWrestling, 180, 105, 38, 56, "SFA", 36, 0, 0,
            "A veteran wrestling-hall ace turned teacher, and the man who taught Hagiwara how to hurt a person.",
            "Kawabata spent seventeen years in the ring of the old wrestling halls and fifteen of them quietly teaching the holds that were never meant to be shown. Hagiwara is his best student and his argument with the old bosses. At thirty-eight he has agreed to the Association's first show in order to be in the corner, and to fight if somebody asks."),
        P("Eiji", "Furuya", "Quickstep", "JPN", OriginsDiscipline.Boxing, 170, 61, 28, 56, "SFA", 14, 0, 0,
            "A former Japanese national lightweight boxing champion with a very quick right foot.",
            "Furuya held a national boxing title for fourteen months and lost it to a man he had beaten twice before. He is tidy, light on his feet and has never been taken down by anyone who was not holding his shirt. The Association hired him as an ambassador for boxing, and he intends to be more than that."),
        P("Haruo", "Mizushima", "The Eel", "JPN", OriginsDiscipline.Judo, 168, 62, 24, 56, "SFA", 10, 0, 0,
            "A university judo specialist from the old ground-fighting school.",
            "Mizushima trained in the old ground-fighting judo of a Kyoto university club, where matches are won on the mat and the throw is an afterthought. He is a slim, relentless man who takes arms the way other men take hats. His teachers say he is the best natural ground-fighter in the country, and that he has never once been punched hard."),
        P("Seiji", "Okabe", null, "JPN", OriginsDiscipline.GrecoRomanWrestling, 176, 87, 27, 58, "SFA", 10, 0, 0,
            "A Greco-Roman wrestler from Hokkaido who throws with the arms and nothing below the waist.",
            "Okabe wrestled Greco-Roman for the national team out of a Hokkaido steel town and was a national finalist twice. His throws begin at the hips and end on the shoulders, and he has never in his life attacked a leg. He will ask politely before touching your ankle, and he will not have to."),

        // ============ DLB: the gym duel (Rio de Janeiro) ============
        P("Nelson", "Aguiar", "O Professor", "BRA", OriginsDiscipline.LutaLivre, 181, 86, 33, 70, "DLB", 24, 4, 0,
            "A physical-education teacher whose gym has not lost a challenge match in two years.",
            "Aguiar taught physical education in Tijuca for ten years and ran the best catch-as-catch-can gym in the north zone after hours. He is four and oh in challenge matches. His students call him the Professor because he stops in the middle of a fight to explain what just happened.",
            null),
        P("Cassio", "Vilela", "Cipo", "BRA", OriginsDiscipline.JiuJitsu, 175, 76, 31, 62, "DLB", 20, 3, 0,
            "The elder Vilela brother, a jiu-jitsu black belt who has never lost a challenge match.",
            "Cassio is the elder of the two Vilela brothers and the one with the belt. He has won three challenge matches in the garage, all of them on the ground and all within six minutes. He took up jiu-jitsu to look after his younger brother and has never stopped."),
        P("Marcelo", "Vilela", "Fogo", "BRA", OriginsDiscipline.Brawler, 178, 72, 24, 54, "DLB", 18, 1, 1,
            "The younger Vilela brother, who goes forward with both hands and has never been told to stop.",
            "Marcelo is the younger Vilela and the loudest man in the room. He is one and one in challenge matches, the loss to a larger man whom he knocked down twice. He does everything his brother tells him not to, and his brother comes to every fight anyway."),
        P("Odair", "Quintela", "O Padeiro", "BRA", OriginsDiscipline.JiuJitsu, 176, 80, 36, 64, "DLB", 22, 4, 1,
            "A baker whose gym is the oldest in the north zone and whose grudge against the Paranhos house is older.",
            "Quintela owns a bakery and the Quintela Academia above it, and has run both since his father's death. He has four wins and one loss in challenge matches, the loss to a Paranhos student. He is a patient man who has never forgiven the Paranhos house and is not going to start now.",
            null),
        P("Heitor", "Paranhos", "Tatu", "BRA", OriginsDiscipline.LutaLivre, 172, 76, 29, 62, "DLB", 20, 3, 2,
            "A compact, bad-tempered luta livre man who owns his gym and every grudge in it.",
            "Paranhos took over his father's gym at twenty-four and has three wins and two losses in the garage. He is called Tatu, the armadillo, because he curls up and cannot be turned. He thinks the Quintela house is a bakery that teaches an expensive hobby.",
            null, "Quintela"),
        P("Jurandir", "Salles", "Mestre Jura", "BRA", OriginsDiscipline.Capoeira, 170, 70, 40, 55, "DLB", 26, 0, 0,
            "A capoeira master of forty who has never fought for money and agreed to once, for his student.",
            "Mestre Jura has taught capoeira in a Lapa roda for twenty years and has never fought for money. He agreed to a single challenge match to prove to his students that the art works, and he will not be hurried into another. He is the oldest man on any card in the sport."),
        P("Wesley", "Amaral", "Bem-te-vi", "BRA", OriginsDiscipline.Capoeira, 172, 66, 21, 48, "DLB", 12, 0, 0,
            "Mestre Jura's best student, twenty-one, quick as a bird and not yet afraid of anything.",
            "Amaral has trained in Mestre Jura's roda since he was eleven and is called Bem-te-vi after the bird that shouts its own name. He is twenty-one, flexible as a rope and untested in anything but sparring. Jura says he is the future of the art, and that the future is not ready."),
        P("Luis", "Gaspar", "O Boi", "BRA", OriginsDiscipline.Judo, 183, 98, 27, 58, "DLB", 14, 0, 0,
            "A Brazilian judo heavyweight who learnt to fight at his father's carpentry shop.",
            "Gaspar was a national judo medallist and works at his father's carpentry shop between competitions. He is a strong, uncomplicated man who throws what he can reach and holds what he throws. He does not know what a challenge match is, but he knows what a throw is."),
        P("Tobias", "Cardoso", "Soco Seco", "BRA", OriginsDiscipline.Boxing, 177, 72, 26, 54, "DLB", 14, 1, 1,
            "A Rio amateur boxing champion who has learnt what it is to be taken down.",
            "Cardoso won a state amateur title and turned professional in a Rio boxing hall. He is one and one in the garage, the loss on the ground in under two minutes. He has since added a lesson to his training that he will not discuss."),
        P("Ademir", "Bastos", "O Armario", "BRA", OriginsDiscipline.Brawler, 198, 128, 30, 46, "DLB", 18, 2, 0,
            "A six-foot-six nightclub doorman who has won two challenge matches by standing still.",
            "Bastos is the Armario, the wardrobe, and has worked the door at a Copacabana club for eight years. He is two and oh in challenge matches, both of them decided by his refusing to fall over. His weakness is that nobody has yet asked him to run."),
        P("Zeca", "Marinho", "Chicote", "BRA", OriginsDiscipline.Kickboxing, 176, 68, 25, 56, "DLB", 14, 1, 0,
            "A Rio kickboxer whose left leg is the loudest sound in the building.",
            "Marinho took up kickboxing at a Copacabana gym and has one win in the garage, a body kick that ended the argument. He is called Chicote, the whip, and he has not yet been taken to the floor. His friends say that this is only because nobody has tried hard enough."),

        // ============ Unattached: free agents from elsewhere ============
        P("Joost", "Zandvliet", "The Dike", "NED", OriginsDiscipline.Kickboxing, 188, 92, 27, 76, null, 26, 0, 0,
            "A Rotterdam kickboxing champion who has held the line against every challenger for six years.",
            "Zandvliet comes from the Rotterdam docks and has fought on every continent in kickboxing rules. He is the Dike because nothing gets through him: six years, no stoppage losses, a national heavy title. The Crucible, the Association and the Garage have all written to him, and he has not yet decided which letter to open first.",
            null),
        P("Zurab", "Mgeladze", null, "GEO", OriginsDiscipline.Judo, 177, 82, 28, 66, null, 12, 0, 0,
            "A Georgian judoka and former Soviet team member, now fighting for no flag.",
            "Mgeladze was a member of the Soviet judo team and then, the following year, a citizen of Georgia. He is a short, silent man whose throw has not been countered in years. He has not forgiven Vadim Orekhov for a result in the army championships in 1989, and Orekhov has not offered to remember it.",
            null, "Orekhov"),
        P("Vadim", "Orekhov", null, "RUS", OriginsDiscipline.Sambo, 178, 90, 29, 72, null, 14, 0, 0,
            "A former Soviet army champion in combat sambo, now fighting under a passport of a country that did not exist two years ago.",
            "Orekhov won the Soviet army combat-sambo championship three times and is now a Russian citizen, with a gym in a Moscow suburb that heats itself with a stove. He takes legs, hands and wrists and ends fights in ways no one in the West has seen. His only complaint is that the Association will not let him kick someone who is already on the floor."),
        P("Boris", "Khlebnikov", "The Stove", "RUS", OriginsDiscipline.GrecoRomanWrestling, 190, 122, 34, 64, null, 12, 0, 0,
            "A Greco-Roman heavyweight from a Moscow suburb who is believed to have never been thrown.",
            "Khlebnikov wrestled Greco-Roman for the Soviet Union until the Soviet Union stopped, and then for himself. He has not been thrown since 1985 and is called the Stove because he gets hotter and heavier as the fight goes on. He is thirty-four, short on cardio and long on contempt for anyone who calls wrestling a boring sport."),
        P("Mitch", "Eversole", "Silo", "USA", OriginsDiscipline.FreestyleWrestling, 190, 112, 24, 57, null, 10, 0, 0,
            "A big Iowa heavyweight wrestler with the same roommate and the same sandwiches as Cade Hollister.",
            "Eversole was a heavyweight wrestler at the same Iowa state university as Cade Hollister and wrestles the way he lives: slowly, enormously and without fuss. He followed his friend west in the same car. He has never been hit hard enough to know whether he can take it."),
        P("Jun-seo", "Baek", "Bull of Daegu", "KOR", OriginsDiscipline.Ssireum, 188, 118, 26, 56, null, 10, 0, 0,
            "A Korean traditional wrestler, a champion of the belt-and-sash sport of ssireum.",
            "Baek won three Korean traditional-wrestling titles in ssireum, a sport fought from a kneeling grip on a thigh sash in a sandpit. He is an enormous, mild-mannered man from Daegu who has been thrown, in his words, only by his own father. He is an unknown quantity to everyone except the three men who have tried to throw him."),
        P("Pongsak", "Thanasarn", "Iron Elbow", "THA", OriginsDiscipline.MuayThai, 170, 64, 24, 63, null, 10, 0, 0,
            "A Bangkok stadium veteran with more than a hundred fights and the elbows to show for them.",
            "Thanasarn began fighting for meal money at a Bangkok temple fair at the age of eleven and has had well over a hundred professional fights. He is called Iron Elbow for reasons that his opponents have taken to the hospital. He is small and has never been asked to wrestle.",
            null),
        P("Parviz", "Zarrinkamar", "Zarrin", "IRI", OriginsDiscipline.FreestyleWrestling, 172, 74, 29, 64, null, 12, 0, 0,
            "An Iranian freestyle wrestler from a country that does not yet have a fight scene.",
            "Zarrinkamar was a world-championship finalist in freestyle wrestling and is a hero in Tehran's gymnasiums. He wrestles with a quiet, dangerous ease and comes to the sport because he was told it will pay what the national federation does not. He speaks very little English and has the best grip in the room."),
        P("Dermot", "Quirke", "The Fairground", "IRL", OriginsDiscipline.Brawler, 183, 92, 31, 52, null, 18, 0, 0,
            "An Irish bare-knuckle fairground fighter who has never fought under rules and does not mean to start.",
            "Quirke grew up in a travelling fairground family and has fought bare-knuckle at horse fairs since he was fifteen, for stakes and for pride. He says he has never lost, and nobody has been able to disprove it. He is strong, awkward and unusually good at not being where the punch is."),
        P("Sipho", "Dladla", "The Express", "RSA", OriginsDiscipline.Boxing, 178, 75, 28, 56, null, 14, 0, 0,
            "A Johannesburg township boxer who has never lost a round to a smaller man.",
            "Dladla learned to box in a Soweto church hall and has twenty-two amateur titles to his name, most of them won by walking forward and refusing to stop. He is called the Express for the way he goes straight at people. He has been told that the sport has no weight limits and has taken it as an invitation."),
        P("Reinaldo", "Pozo", "Lefty", "CUB", OriginsDiscipline.Boxing, 183, 80, 26, 68, null, 12, 0, 0,
            "A Cuban amateur boxing star, now living in Miami, who left the national team behind.",
            "Pozo was a Cuban amateur champion who left the national squad and now lives in Miami, where he works at a car wash and trains at a gym with no heating. He is a tall, relaxed southpaw who has fought eighty amateur bouts and lost four. He is looking for money that boxing, so far, has not offered him."),
        P("Ray", "Delacroix", "Bayou", "USA", OriginsDiscipline.Kickboxing, 180, 78, 30, 62, null, 16, 0, 0,
            "A Louisiana full-contact kickboxing champion who believes the Dutch have been wrong about everything.",
            "Delacroix is a three-time American full-contact kickboxing champion out of Lafayette and a man who thinks the Dutch have been wrong about kickboxing for ten years. He is blunt, funny and fast, and says he will fight Joost Zandvliet anywhere. Zandvliet has not yet answered the letter.",
            null, "Zandvliet"),
    };

    // ------------------------------------------------------------------ gyms
    /// <summary>The training rooms the bios name. The wiring phase must create these as camps; none exists in today's world.</summary>
    public static IReadOnlyList<OriginsGym> Gyms { get; } = new OriginsGym[]
    {
        new("Quintela Academia", "Rio de Janeiro", "A jiu-jitsu academy above a bakery in the north zone, founded by Odair Quintela's father. The mats are thin, the flour dust is permanent and the rivalry with the Paranhos house is older than most of its students."),
        new("Casa Paranhos", "Rio de Janeiro", "A concrete gym with a tin roof where luta livre has been taught since the early seventies. Its members say Quintela's students learn jiu-jitsu from a book; Quintela's students say the Paranhos men learn it from a grudge."),
        new("Roda de Mestre Jura", "Rio de Janeiro", "A weekly capoeira circle in a Lapa courtyard that has run for twenty years under a single mango tree. Mestre Jura teaches for nothing and has never charged a student for a lesson."),
        new("Kawabata Dojo", "Tokyo", "A second-floor room above a noodle shop in Tokyo where an old wrestling-hall ace teaches the holds that were never in the script. It has one ring, one mirror and a framed photograph of the old hall that closed."),
        new("Seishin Judo Club", "Tokyo", "A university-affiliated judo club that has produced two Olympians and a cultured contempt for strikers. Its ground-fighting school is the best in the country, and it will tell you so."),
        new("Heartland Mat Club", "Chicago", "A Midwest wrestling room with a cracked mat, a speaker that plays the same cassette every day and eleven national champions in a framed row. The boys who come out of it do not talk much."),
        new("Zuidwal Gym", "Amsterdam", "A kickboxing gym in a former Rotterdam dockside warehouse, moved to Amsterdam by a founder who disliked the cranes. It has a long reputation, a short ring and a coach who counts rounds in Dutch."),
        new("Zarya Sambo Hall", "Moscow", "A Moscow suburb sambo hall heated by a stove and a brazier. It trained half the army champions of the eighties and now trains whoever can pay in dollars or in vodka."),
        new("Unit 14 Jiu-Jitsu", "Los Angeles", "A strip-mall academy between a dry cleaner and a tax office, run by a man who prefers it that way. The mats are second-hand, the students are police officers and teenagers, and nobody has yet heard of it."),
    };

    /// <summary>Where the pioneers train, by last name (full "First Last" where a surname is shared).</summary>
    public static IReadOnlyDictionary<string, string> CampHomes { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Quintela"] = "Quintela Academia",
            ["Cassio Vilela"] = "Quintela Academia",     // the academy that raised the elder brother
            ["Paranhos"] = "Casa Paranhos",
            ["Aguiar"] = "Casa Paranhos",                // the north-zone luta livre school
            ["Salles"] = "Roda de Mestre Jura",
            ["Amaral"] = "Roda de Mestre Jura",
            ["Kawabata"] = "Kawabata Dojo",
            ["Hagiwara"] = "Kawabata Dojo",
            ["Okabe"] = "Kawabata Dojo",
            ["Tsukamoto"] = "Seishin Judo Club",
            ["Mizushima"] = "Seishin Judo Club",
            ["Hollister"] = "Heartland Mat Club",
            ["Eversole"] = "Heartland Mat Club",
            ["Zandvliet"] = "Zuidwal Gym",
            ["Orekhov"] = "Zarya Sambo Hall",
            ["Khlebnikov"] = "Zarya Sambo Hall",
            ["Neves"] = "Unit 14 Jiu-Jitsu",
        };

    // ------------------------------------------------------------------ side tables
    /// <summary>Who they are, by last name. Matches what each bio says out loud.</summary>
    public static IReadOnlyDictionary<string, Disposition> Dispositions { get; } =
        new Dictionary<string, Disposition>(StringComparer.Ordinal)
        {
            ["Corbin"] = Disposition.Warhorse,          // twelve years one win short, here for the harder fight
            ["Hollister"] = Disposition.QuietProfessional,
            ["Yoon"] = Disposition.Student,             // says the sport tests whether the forms were real
            ["Tofaeono"] = Disposition.Showman,         // booked for his size; will sell it
            ["Brannock"] = Disposition.Showman,         // carnival athletic show, a decade of challengers
            ["Skaggs"] = Disposition.Hothead,           // parking-lot arguments, three counties
            ["Nakahara"] = Disposition.QuietProfessional, // calm, courteous, fast in a straight line
            ["Neves"] = Disposition.QuietProfessional,  // treats a fight as a long afternoon of chess
            ["Thibodeau"] = Disposition.Student,
            ["Baeza"] = Disposition.Showman,            // at home in a crowd
            ["Hagiwara"] = Disposition.CompanyMan,      // the Association's ring is where he is tested
            ["Ishikari"] = Disposition.Loudmouth,       // says the wrestling halls are theatre, politely and often
            ["Tsukamoto"] = Disposition.Warhorse,       // walked off the national team on a point of honour
            ["Aizawa"] = Disposition.QuietProfessional, // cannot box and will tell you so
            ["Morikawa"] = Disposition.Student,         // shyness that looks like caution and is arithmetic
            ["Segawa"] = Disposition.Loudmouth,         // announces that a wrestler can end him in ten seconds
            ["Kawabata"] = Disposition.Student,         // fifteen years teaching the unshown holds
            ["Furuya"] = Disposition.QuietProfessional,
            ["Mizushima"] = Disposition.Student,
            ["Okabe"] = Disposition.QuietProfessional,
            ["Aguiar"] = Disposition.Student,           // stops mid-fight to explain what just happened
            ["Cassio Vilela"] = Disposition.QuietProfessional,
            ["Marcelo Vilela"] = Disposition.Hothead,   // does everything his brother tells him not to
            ["Quintela"] = Disposition.CompanyMan,      // the academy above the bakery
            ["Paranhos"] = Disposition.Hothead,         // bad-tempered, owns every grudge in the gym
            ["Salles"] = Disposition.QuietProfessional, // has never fought for money
            ["Amaral"] = Disposition.Showman,
            ["Gaspar"] = Disposition.QuietProfessional,
            ["Cardoso"] = Disposition.Student,          // added a lesson he will not discuss
            ["Bastos"] = Disposition.QuietProfessional, // wins by refusing to fall over
            ["Marinho"] = Disposition.Showman,          // the loudest sound in the building
            ["Zandvliet"] = Disposition.Mercenary,      // three letters on the desk, no loyalty to any
            ["Mgeladze"] = Disposition.Warhorse,        // carrying a 1989 result
            ["Orekhov"] = Disposition.QuietProfessional,
            ["Khlebnikov"] = Disposition.Hothead,       // contempt for anyone who calls wrestling boring
            ["Eversole"] = Disposition.QuietProfessional,
            ["Baek"] = Disposition.QuietProfessional,
            ["Thanasarn"] = Disposition.Warhorse,       // more than a hundred fights for meal money
            ["Zarrinkamar"] = Disposition.Mercenary,    // here because the federation pays nothing
            ["Quirke"] = Disposition.Hothead,           // has never fought under rules and does not mean to start
            ["Dladla"] = Disposition.Warhorse,
            ["Pozo"] = Disposition.Mercenary,           // looking for money boxing has not offered
            ["Delacroix"] = Disposition.Loudmouth,      // says the Dutch are wrong about everything
        };

    /// <summary>Heat a day-one feud starts at, by the two last names ("A|B", either order).</summary>
    public static IReadOnlyDictionary<string, int> RivalHeat { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Quintela|Paranhos"] = 80,   // the bakery against the armadillo, two gyms, one neighbourhood
            ["Hagiwara|Ishikari"] = 65,   // theatre versus the real thing, in the papers
            ["Zandvliet|Delacroix"] = 70, // Rotterdam against Lafayette, by letter
            ["Mgeladze|Orekhov"] = 60,    // the 1989 army championships
        };

    public static int RivalHeatOf(string lastA, string lastB) =>
        RivalHeat.TryGetValue($"{lastA}|{lastB}", out var h) || RivalHeat.TryGetValue($"{lastB}|{lastA}", out h) ? h : 60;

    /// <summary>Why two pioneers cannot stand each other, in one line of attitude, by last names.</summary>
    public static IReadOnlyDictionary<string, string> FeudReasons { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Quintela|Paranhos"] = "One gym is above a bakery and the other has a tin roof, and each is certain the other's students learnt their trade from the wrong teacher.",
            ["Hagiwara|Ishikari"] = "Ishikari says the wrestling halls are theatre; Hagiwara answered in the newspaper, in four courteous sentences, that he would like to be tested.",
            ["Zandvliet|Delacroix"] = "Delacroix says the Dutch have been wrong about kickboxing for ten years; Zandvliet has not replied, which Delacroix takes as an admission.",
            ["Mgeladze|Orekhov"] = "A judging decision at the 1989 army championships, in a country that no longer exists, and neither man will let the other forget it.",
        };

    public static string? FeudReason(string lastA, string lastB) =>
        FeudReasons.TryGetValue($"{lastA}|{lastB}", out var r) || FeudReasons.TryGetValue($"{lastB}|{lastA}", out r) ? r : null;

    /// <summary>
    /// Day-one bonds. A and B are last names, or "First Last" where a surname is shared (the Vilelas).
    /// A is the elder for a MENTOR.
    /// </summary>
    public static IReadOnlyList<DesignedRoster.DesignedBond> Bonds { get; } = new DesignedRoster.DesignedBond[]
    {
        new("Kawabata", "Hagiwara", RelationshipKind.Mentor, 85, "taught him the holds that were never in the script"),
        new("Salles", "Amaral", RelationshipKind.Mentor, 80, "the master and the boy who has trained in his courtyard since he was eleven"),
        new("Cassio Vilela", "Marcelo Vilela", RelationshipKind.Family, 90, "brothers; the elder took up jiu-jitsu to look after the younger"),
        new("Hollister", "Eversole", RelationshipKind.Friend, 70, "college roommates who drove west in the same car"),
        new("Brannock", "Baeza", RelationshipKind.Friend, 55, "worked the same carnival and lucha circuits and share a love of a crowd"),
        new("Tsukamoto", "Mizushima", RelationshipKind.Mentor, 60, "the older judoka who first showed the younger the ground work"),
    };

    // ------------------------------------------------------------------ skill profiles
    private static readonly SkillId[] Technical =
    {
        SkillId.PunchPower, SkillId.PunchTechnique, SkillId.KickPower, SkillId.KickTechnique, SkillId.ClinchStriking,
        SkillId.StrikingDefense, SkillId.Footwork, SkillId.TakedownOffense, SkillId.TakedownDefense, SkillId.TopControl,
        SkillId.BottomGame, SkillId.SubmissionOffense, SkillId.SubmissionDefense, SkillId.Scrambling, SkillId.ClinchControl,
    };

    /// <summary>What every technical skill sits at, relative to overall, when the discipline never teaches it.</summary>
    public const int UntaughtOffset = -24;

    /// <summary>
    /// Per discipline: offsets from the fighter's overall. Skills listed here are the art's craft (or its
    /// body); every OTHER technical skill sits at <see cref="UntaughtOffset"/>, and every other physical
    /// or mental skill at zero. That is what makes a boxer a boxer and a judoka a judoka.
    /// </summary>
    private static readonly IReadOnlyDictionary<OriginsDiscipline, (SkillId, int)[]> Profiles =
        new Dictionary<OriginsDiscipline, (SkillId, int)[]>
        {
            [OriginsDiscipline.Boxing] = new[] { (SkillId.PunchPower, 18), (SkillId.PunchTechnique, 26), (SkillId.StrikingDefense, 22), (SkillId.Footwork, 20), (SkillId.Chin, 8), (SkillId.Cardio, 6), (SkillId.TakedownDefense, -12) },
            [OriginsDiscipline.Kickboxing] = new[] { (SkillId.KickPower, 22), (SkillId.KickTechnique, 26), (SkillId.PunchTechnique, 14), (SkillId.PunchPower, 10), (SkillId.Footwork, 18), (SkillId.StrikingDefense, 16), (SkillId.ClinchStriking, 8), (SkillId.Cardio, 8), (SkillId.TakedownDefense, -12) },
            [OriginsDiscipline.MuayThai] = new[] { (SkillId.KickPower, 20), (SkillId.KickTechnique, 16), (SkillId.ClinchStriking, 28), (SkillId.ClinchControl, 10), (SkillId.PunchTechnique, 4), (SkillId.StrikingDefense, 12), (SkillId.Cardio, 12), (SkillId.Chin, 10), (SkillId.TakedownDefense, -8) },
            [OriginsDiscipline.Karate] = new[] { (SkillId.KickTechnique, 20), (SkillId.KickPower, 14), (SkillId.PunchPower, 14), (SkillId.Footwork, 16), (SkillId.StrikingDefense, 10), (SkillId.Speed, 8), (SkillId.Discipline, 10), (SkillId.Chin, 10), (SkillId.TakedownDefense, -16) },
            [OriginsDiscipline.Taekwondo] = new[] { (SkillId.KickTechnique, 28), (SkillId.KickPower, 8), (SkillId.Footwork, 20), (SkillId.Speed, 14), (SkillId.Flexibility, 16), (SkillId.PunchTechnique, -12), (SkillId.Chin, -8), (SkillId.TakedownDefense, -20) },
            [OriginsDiscipline.Capoeira] = new[] { (SkillId.KickTechnique, 22), (SkillId.Footwork, 22), (SkillId.StrikingDefense, 14), (SkillId.KickPower, 4), (SkillId.Flexibility, 26), (SkillId.Speed, 14), (SkillId.Strength, -6) },
            [OriginsDiscipline.Judo] = new[] { (SkillId.TakedownOffense, 24), (SkillId.ClinchControl, 26), (SkillId.TakedownDefense, 14), (SkillId.TopControl, 14), (SkillId.SubmissionOffense, 10), (SkillId.SubmissionDefense, 6), (SkillId.Scrambling, 8), (SkillId.Strength, 8), (SkillId.Chin, -4) },
            [OriginsDiscipline.FreestyleWrestling] = new[] { (SkillId.TakedownOffense, 28), (SkillId.TakedownDefense, 26), (SkillId.TopControl, 22), (SkillId.Scrambling, 20), (SkillId.ClinchControl, 12), (SkillId.SubmissionOffense, -10), (SkillId.Strength, 8), (SkillId.Cardio, 12), (SkillId.Heart, 8) },
            [OriginsDiscipline.GrecoRomanWrestling] = new[] { (SkillId.ClinchControl, 28), (SkillId.TakedownOffense, 24), (SkillId.TakedownDefense, 14), (SkillId.TopControl, 12), (SkillId.Strength, 14), (SkillId.Cardio, 4) },
            [OriginsDiscipline.Sumo] = new[] { (SkillId.ClinchControl, 14), (SkillId.TakedownDefense, 20), (SkillId.PunchPower, 6), (SkillId.Strength, 22), (SkillId.Chin, 14), (SkillId.Cardio, -26), (SkillId.Speed, -14), (SkillId.Flexibility, -20), (SkillId.Recovery, -10), (SkillId.TakedownOffense, -2) },
            [OriginsDiscipline.Ssireum] = new[] { (SkillId.ClinchControl, 24), (SkillId.TakedownOffense, 16), (SkillId.TakedownDefense, 16), (SkillId.Strength, 18), (SkillId.Cardio, -6), (SkillId.Flexibility, -12) },
            [OriginsDiscipline.JiuJitsu] = new[] { (SkillId.SubmissionOffense, 30), (SkillId.SubmissionDefense, 26), (SkillId.BottomGame, 30), (SkillId.Scrambling, 16), (SkillId.TopControl, 12), (SkillId.Flexibility, 14), (SkillId.TakedownDefense, -6), (SkillId.Chin, -6), (SkillId.FightIQ, 6) },
            [OriginsDiscipline.Sambo] = new[] { (SkillId.TakedownOffense, 20), (SkillId.SubmissionOffense, 18), (SkillId.TopControl, 16), (SkillId.Scrambling, 16), (SkillId.ClinchControl, 12), (SkillId.SubmissionDefense, 12), (SkillId.TakedownDefense, 10), (SkillId.BottomGame, 8), (SkillId.Strength, 8), (SkillId.Cardio, 8) },
            [OriginsDiscipline.LutaLivre] = new[] { (SkillId.TopControl, 18), (SkillId.SubmissionOffense, 18), (SkillId.ClinchControl, 14), (SkillId.Scrambling, 14), (SkillId.SubmissionDefense, 14), (SkillId.BottomGame, 14), (SkillId.TakedownOffense, 10), (SkillId.TakedownDefense, 6) },
            [OriginsDiscipline.ProWrestling] = new[] { (SkillId.TopControl, 14), (SkillId.ClinchControl, 14), (SkillId.SubmissionOffense, 14), (SkillId.TakedownOffense, 8), (SkillId.SubmissionDefense, 8), (SkillId.Strength, 10), (SkillId.Cardio, 8), (SkillId.Chin, 8), (SkillId.Flexibility, 8) },
            [OriginsDiscipline.Brawler] = new[] { (SkillId.PunchPower, 20), (SkillId.Chin, 20), (SkillId.Heart, 22), (SkillId.Strength, 8), (SkillId.StrikingDefense, -16), (SkillId.FightIQ, -10), (SkillId.Discipline, -14), (SkillId.Cardio, -8), (SkillId.Composure, -4), (SkillId.TakedownDefense, -14) },
        };

    /// <summary>The headline style each discipline files under in the existing roster row.</summary>
    public static PrimaryStyle StyleOf(OriginsDiscipline d) => d switch
    {
        OriginsDiscipline.Boxing => PrimaryStyle.Striker,
        OriginsDiscipline.Karate => PrimaryStyle.Striker,
        OriginsDiscipline.Kickboxing => PrimaryStyle.Kickboxer,
        OriginsDiscipline.MuayThai => PrimaryStyle.Kickboxer,
        OriginsDiscipline.Taekwondo => PrimaryStyle.Kickboxer,
        OriginsDiscipline.Capoeira => PrimaryStyle.Kickboxer,
        OriginsDiscipline.FreestyleWrestling => PrimaryStyle.Wrestler,
        OriginsDiscipline.GrecoRomanWrestling => PrimaryStyle.ClinchFighter,
        OriginsDiscipline.Sumo => PrimaryStyle.ClinchFighter,
        OriginsDiscipline.Ssireum => PrimaryStyle.ClinchFighter,
        OriginsDiscipline.JiuJitsu => PrimaryStyle.BrazilianJiuJitsu,
        OriginsDiscipline.Judo => PrimaryStyle.Grappler,
        OriginsDiscipline.Sambo => PrimaryStyle.Grappler,
        OriginsDiscipline.LutaLivre => PrimaryStyle.Grappler,
        OriginsDiscipline.ProWrestling => PrimaryStyle.Grappler,
        OriginsDiscipline.Brawler => PrimaryStyle.Brawler,
        _ => PrimaryStyle.WellRounded,
    };

    /// <summary>The nearest existing weight class for a natural weight (kilograms): the first class whose limit covers it.</summary>
    public static WeightClass ClassFor(int kg)
    {
        double lbs = kg * 2.20462;
        foreach (var wc in Enum.GetValues<WeightClass>())
            if (lbs <= WeightClasses.LimitLbs(wc)) return wc;
        return WeightClass.Heavyweight;
    }

    /// <summary>Every skill, 1..95: the discipline's profile around the fighter's overall, plus any personal tweaks. Pure arithmetic.</summary>
    public static IReadOnlyDictionary<SkillId, int> SkillsOf(OriginsPioneer p)
    {
        var profile = Profiles[p.Discipline].ToDictionary(x => x.Item1, x => x.Item2);
        var tweaks = p.Tweaks?.ToDictionary(x => x.Skill, x => x.Offset) ?? new Dictionary<SkillId, int>();
        var result = new Dictionary<SkillId, int>();
        foreach (var s in Enum.GetValues<SkillId>())
        {
            int offset = profile.TryGetValue(s, out var o) ? o : Technical.Contains(s) ? UntaughtOffset : 0;
            if (tweaks.TryGetValue(s, out var t)) offset += t;
            result[s] = Math.Clamp(p.Fighter.Overall + offset, 1, 95);
        }
        return result;
    }

    // ------------------------------------------------------------------ row builder
    private static OriginsPioneer P(
        string first, string last, string? nick, string country, OriginsDiscipline d, int cm, int kg, int age, int overall,
        string? org, int popularity, int wins, int losses, string hook, string bio,
        (SkillId, int)[]? tweaks = null, string? rivalOf = null)
    {
        var region = WorldAtlas.ByCode(country)?.Continent ?? throw new ArgumentException($"{first} {last}: unknown country {country}");
        var fighter = new DesignedFighter(first, last, nick, region, ClassFor(kg), StyleOf(d), age, overall, wins, losses,
            org, Champion: false, Popularity: popularity, Country: country, Sex: Sex.Male, RivalOf: rivalOf, Bio: bio);
        return new OriginsPioneer(fighter, d, cm, kg, hook, tweaks is null ? null : tweaks.Select(t => (t.Item1, t.Item2)).ToArray());
    }
}
