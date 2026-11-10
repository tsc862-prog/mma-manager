using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;
using static MmaManager.Wpf.Art.FlatArt;

namespace MmaManager.Wpf.Art;

/// <summary>
/// Every visual trait of one generated character, rolled deterministically from
/// the fighter's id + attributes (nationality → skin/hair pools, sex → style
/// pools, age → greying/balding/wrinkles, weight class → face width and build,
/// style/record → cauliflower ears, scars). The face and body renderers both
/// consume the same DNA so a fighter's head shot and full-body shot match.
/// </summary>
internal sealed class CharacterDna
{
    // identity / head
    public bool Female;
    public int Age;
    public double FaceW, FaceH;        // head half-extents in local units
    public double Jaw;                 // jaw half-width as a fraction of FaceW
    public double Cheek;               // cheek fullness 0..1
    public Color Skin, SkinShadow, SkinLine, Lip;
    public Color Hair, HairDark;
    public int HairStyle;              // index into the sex-specific style set
    public int Facial;                 // 0 none 1 stubble 2 goatee 3 full 4 moustache 5 chinstrap 6 heavy 7 vandyke
    public Color Eye;
    public int EyeShape;               // 0 almond 1 round 2 narrow 3 heavy-lid
    public int Brow;                   // 0 flat 1 angled 2 thick 3 thin 4 arched
    public int Nose;                   // 0 curve 1 wedge 2 button 3 wide 4 strong
    public int Mouth;                  // 0 neutral 1 smile 2 open smile 3 smirk 4 slight frown 5 full lips
    public bool Cauliflower, Scar, Earring, Wrinkles, Blush;

    // dressing
    public Color Bg;
    public Color Outfit, OutfitDark;
    public bool Tank;                  // tank top vs tee on the face shot
    public Color ShortsMain, ShortsAccent, Glove;

    // body
    public int Build;                  // 0 lean 1 athletic 2 bulky 3 heavy
    public double Muscle;              // 0..1 definition

    public static CharacterDna For(Fighter f, int age)
    {
        var r = new SimRandom(SimRandom.Mix(0xF1A7_FACE_5EEDUL, (ulong)f.Id.Value));
        var d = new CharacterDna { Female = f.Sex == Sex.Female, Age = age };

        // --- skin / regional pools ---------------------------------------
        int bucket = PickBucket(r, f.Nationality);   // 0 dark 1 tan 2 asian 3 light
        d.Skin = r.Pick(bucket switch { 0 => SkinDark, 1 => SkinTan, 2 => SkinAsian, _ => SkinLight });
        d.SkinShadow = Shade(d.Skin, 0.86);
        d.SkinLine = Shade(d.Skin, 0.5);
        d.Lip = bucket == 0 ? Mix(d.Skin, Rgb(0x8E4B44), 0.5) : Mix(d.Skin, Rgb(0xB5615A), 0.6);

        var baseHair = r.Pick(bucket <= 2 ? HairDarkOnly : HairAll);
        double grey = age >= 46 ? 0.75 : age >= 41 ? 0.5 : age >= 36 ? 0.25 : age >= 31 ? 0.08 : 0;
        grey = Math.Min(0.9, grey + r.NextDouble() * 0.1);
        d.Hair = Mix(baseHair, Rgb(0xBDBDBD), grey);
        d.HairDark = Shade(d.Hair, 0.72);
        d.Eye = r.Pick(bucket <= 1 ? EyesDark : bucket == 2 ? EyesDark : EyesAll);

        // --- head geometry -----------------------------------------------
        double wc = (int)f.WeightClass / 8.0;         // 0 strawweight .. 1 heavyweight
        int shape = r.Next(0, 5);                     // 0 oval 1 round 2 square 3 long 4 heart
        (double wMul, double hMul, double jaw, double cheek) = shape switch
        {
            1 => (1.05, 0.95, 0.72, 0.85),
            2 => (1.02, 1.0, 0.82, 0.55),
            3 => (0.93, 1.08, 0.60, 0.55),
            4 => (1.04, 1.0, 0.52, 0.70),
            _ => (1.0, 1.0, 0.64, 0.65),
        };
        d.FaceW = (34 + wc * 8 + r.Range(-2, 2)) * wMul * (d.Female ? 0.93 : 1);
        d.FaceH = (48 + wc * 3 + r.Range(-2, 2)) * hMul;
        d.Jaw = Math.Min(0.9, jaw + wc * 0.10) * (d.Female ? 0.90 : 1);
        d.Cheek = cheek + (d.Female ? 0.08 : 0);

        // --- features -----------------------------------------------------
        d.EyeShape = r.WeightedIndex(bucket == 2 ? EyeShapeAsian : EyeShapeAny);
        d.Brow = r.WeightedIndex(d.Female ? BrowF : BrowM);
        d.Nose = r.WeightedIndex(bucket == 0 ? NoseDark : NoseAny);
        d.Mouth = r.WeightedIndex(d.Female ? MouthF : MouthM);

        // --- hair / facial hair --------------------------------------------
        d.HairStyle = d.Female ? FemaleHair(r, bucket) : MaleHair(r, bucket, age);
        d.Facial = d.Female || age < 20 ? 0 : r.WeightedIndex(FacialW);

        // --- battle wear / extras ------------------------------------------
        bool grappler = f.PrimaryStyle is PrimaryStyle.Wrestler or PrimaryStyle.Grappler
            or PrimaryStyle.BrazilianJiuJitsu or PrimaryStyle.ClinchFighter;
        int bouts = f.Record.TotalBouts;
        d.Cauliflower = grappler && r.Chance(0.30 + Math.Min(0.35, bouts * 0.01));
        d.Scar = r.Chance(0.10 + (f.PrimaryStyle == PrimaryStyle.Brawler ? 0.22 : 0) + Math.Min(0.2, bouts * 0.006));
        d.Earring = r.Chance(d.Female ? 0.4 : 0.06);
        d.Wrinkles = age >= 38;
        d.Blush = r.Chance(0.25);

        // --- dressing -------------------------------------------------------
        d.Bg = r.Pick(Backgrounds);
        d.Outfit = r.Pick(Outfits);
        d.OutfitDark = Shade(d.Outfit, 0.8);
        d.Tank = r.Chance(0.45);
        (d.ShortsMain, d.ShortsAccent) = r.Pick(FlatArt.Shorts);
        d.Glove = r.Pick(Gloves);

        // --- body -----------------------------------------------------------
        d.Build = wc switch { <= 0.25 => 0, <= 0.55 => 1, <= 0.85 => 2, _ => 3 };
        if (d.Build < 3 && r.Chance(0.25)) d.Build = Math.Min(3, d.Build + 1);   // some divisions carry bulk
        d.Muscle = 0.3 + r.NextDouble() * 0.7;

        return d;
    }

    // Which skin bucket a region rolls (0 dark, 1 tan, 2 asian, 3 light).
    private static int PickBucket(SimRandom r, Region region) => region switch
    {
        Region.Africa => 0,
        Region.Asia => 2,
        Region.Europe => r.WeightedIndex(new double[] { 0.06, 0.10, 0.0, 0.84 }),
        Region.Oceania => r.WeightedIndex(new double[] { 0.10, 0.35, 0.05, 0.50 }),
        Region.MiddleEast => r.WeightedIndex(new double[] { 0.08, 0.72, 0.0, 0.20 }),
        Region.SouthAmerica => r.WeightedIndex(new double[] { 0.22, 0.48, 0.0, 0.30 }),
        _ => r.WeightedIndex(new double[] { 0.25, 0.20, 0.12, 0.43 }),   // North America
    };

    // --- feature weight tables (indices documented on the fields above) -----
    private static readonly double[] EyeShapeAny = { 2.0, 1.0, 0.7, 0.9 };
    private static readonly double[] EyeShapeAsian = { 1.0, 0.4, 2.2, 0.8 };
    private static readonly double[] BrowM = { 1.6, 1.4, 1.6, 0.5, 0.3 };
    private static readonly double[] BrowF = { 1.2, 0.8, 0.3, 1.6, 1.8 };
    private static readonly double[] NoseAny = { 1.6, 1.2, 1.0, 0.6, 1.0 };
    private static readonly double[] NoseDark = { 1.0, 0.6, 0.8, 2.0, 1.0 };
    private static readonly double[] MouthM = { 2.0, 1.4, 0.7, 1.0, 0.7, 0.5 };
    private static readonly double[] MouthF = { 1.2, 1.6, 0.8, 0.7, 0.3, 1.8 };
    private static readonly double[] FacialW = { 2.2, 2.0, 1.2, 1.5, 0.5, 0.5, 0.7, 0.6 };

    // Male styles: 0 bald 1 buzz 2 crew 3 side-part 4 curly 5 afro 6 cornrows
    //              7 top-knot 8 mohawk 9 slick-back 10 receding 11 long 12 flat-top
    private static readonly double[] MaleHairDark =
        { 1.2, 1.8, 1.2, 0.4, 1.0, 1.1, 1.1, 0.25, 0.3, 0.2, 0.5, 0.2, 0.7 };
    private static readonly double[] MaleHairAsian =
        { 0.5, 1.2, 1.6, 1.6, 0.6, 0.0, 0.05, 0.9, 0.4, 0.9, 0.5, 0.5, 0.2 };
    private static readonly double[] MaleHairAny =
        { 0.8, 1.4, 1.7, 1.4, 0.9, 0.05, 0.1, 0.5, 0.35, 1.0, 0.9, 0.5, 0.15 };

    private static int MaleHair(SimRandom r, int bucket, int age)
    {
        var w = (double[])(bucket switch { 0 => MaleHairDark, 2 => MaleHairAsian, _ => MaleHairAny }).Clone();
        double aging = Math.Max(0, age - 32) * 0.09;
        w[0] += aging; w[10] += aging;                 // bald / receding rise with age
        if (age > 36) { w[7] *= 0.3; w[8] *= 0.3; w[11] *= 0.5; }
        return r.WeightedIndex(w);
    }

    // Female styles: 0 bun 1 ponytail 2 bob 3 long 4 braids 5 pixie 6 afro 7 cornrow-bun 8 curly-bob
    private static readonly double[] FemaleHairDark =
        { 1.2, 1.0, 0.6, 0.8, 1.6, 0.4, 1.3, 1.3, 1.2 };
    private static readonly double[] FemaleHairAsian =
        { 1.4, 1.6, 1.3, 1.5, 0.3, 0.7, 0.0, 0.2, 0.4 };
    private static readonly double[] FemaleHairAny =
        { 1.3, 1.6, 1.3, 1.4, 0.4, 0.8, 0.05, 0.15, 0.8 };

    private static int FemaleHair(SimRandom r, int bucket) =>
        r.WeightedIndex(bucket switch { 0 => FemaleHairDark, 2 => FemaleHairAsian, _ => FemaleHairAny });
}
