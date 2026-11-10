using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MmaManager.Core.Sim.Booking;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Every command-line hook below runs headless and needs a game to exist,
        // so any argument at all boots straight into the deterministic quick-start
        // world. A plain interactive launch shows the start screen instead.
        MainViewModel.HeadlessAutoBoot = e.Args.Length > 0;

        // Offscreen screenshot of the desktop shell (a maximized app + a floating
        // one over it): `MmaManager.Wpf --renderdesktop <path>`.
        if (e.Args.Contains("--renderdesktop"))
        {
            try
            {
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                for (int i = 0; i < 6; i++) vm.AdvanceMonthCommand.Execute(null);
                vm.Desktop.Open("home");     // maximized by default
                vm.Desktop.Open("events");   // floated to show the windowing
                var floating = vm.Desktop.Windows.First(w => w.Spec.Id == "events");
                floating.IsMaximized = false;
                floating.W = 1080; floating.H = 660; floating.X = 250; floating.Y = 80;
                vm.Desktop.Notify("✉", "2 new messages", "Show report: the numbers are in from last night.");

                var root = (FrameworkElement)win.Content;
                const int W = 1600, H = 900;
                var size = new Size(W, H);
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args[^1];
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERDESKTOP OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERDESKTOP FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Offscreen screenshot of the START SCREEN (the interactive front door):
        // `MmaManager.Wpf --renderstart <path>`.
        if (e.Args.Contains("--renderstart"))
        {
            try
            {
                MainViewModel.HeadlessAutoBoot = false;   // keep the menu up — no world
                var win = new MainWindow();
                var root = (FrameworkElement)win.Content;
                const int W = 1520, H = 860;
                var size = new Size(W, H);
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args[^1];
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERSTART OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERSTART FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Faces-only gallery of the flat character generator, numbered so
        // individual faces are easy to refer to in review:
        // `MmaManager.Wpf --renderfaces [count] [seed] <path>`.
        if (e.Args.Contains("--renderfaces"))
        {
            try
            {
                int argAt = Array.IndexOf(e.Args, "--renderfaces");
                int count = e.Args.Length > argAt + 1 && int.TryParse(e.Args[argAt + 1], out var n) ? n : 48;
                int seed = e.Args.Length > argAt + 2 && int.TryParse(e.Args[argAt + 2], out var s) ? s : 42;

                var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = (ulong)seed });
                var byRegion = world.Fighters.Values.GroupBy(f => f.Nationality)
                    .Select(g => new Queue<MmaManager.Core.Model.Fighter>(g)).ToList();
                var picks = new List<MmaManager.Core.Model.Fighter>();
                for (int gi = 0; picks.Count < count && byRegion.Any(q => q.Count > 0); gi++)
                {
                    var q = byRegion[gi % byRegion.Count];
                    if (q.Count > 0) picks.Add(q.Dequeue());
                }
                foreach (var f in picks.Skip(count * 5 / 6)) f.Sex = MmaManager.Core.Model.Sex.Female;

                int cols = Math.Min(5, picks.Count), img = 170, label = 24, cell = img + 14;
                int rows = (picks.Count + cols - 1) / cols;
                int width = cols * cell + 14, height = rows * (cell + label) + 14;
                var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                    for (int i = 0; i < picks.Count; i++)
                    {
                        double x = i % cols * cell + 14, y = i / cols * (cell + label) + 14;
                        dc.DrawImage(CharacterFactory.Face(picks[i], picks[i].AgeOn(world.CurrentDate)),
                            new Rect(x, y, img, img));
                        var num = new FormattedText($"#{i + 1}", System.Globalization.CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, typeface, 14, Brushes.Black, 1.0);
                        dc.DrawText(num, new Point(x + img / 2.0 - num.Width / 2, y + img + 3));
                    }
                }
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args.Length > 1 ? e.Args[^1] : "faces.png";
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERFACES OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERFACES FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Contact sheet of the flat character generator (faces + bodies):
        // `MmaManager.Wpf --renderchars <path>`.
        if (e.Args.Contains("--renderchars"))
        {
            try
            {
                var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 1234 });

                // Interleave regions so the sheet shows the full diversity range.
                var byRegion = world.Fighters.Values.GroupBy(f => f.Nationality)
                    .Select(g => new Queue<MmaManager.Core.Model.Fighter>(g)).ToList();
                var picks = new List<MmaManager.Core.Model.Fighter>();
                for (int gi = 0; picks.Count < 32 && byRegion.Any(q => q.Count > 0); gi++)
                {
                    var q = byRegion[gi % byRegion.Count];
                    if (q.Count > 0) picks.Add(q.Dequeue());
                }
                // Bottom face row: showcase female DNA (the sim doesn't generate women yet).
                foreach (var f in picks.Skip(24)) f.Sex = MmaManager.Core.Model.Sex.Female;

                const int cols = 8, cell = 110, img = 100;
                const int bodyW = 140, bodyImgW = 126, bodyImgH = 189, bodyY = 4 * cell + 14;
                int width = cols * cell + 10, height = bodyY + bodyImgH + 14;

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                    for (int i = 0; i < picks.Count && i < 32; i++)
                    {
                        var f = picks[i];
                        dc.DrawImage(CharacterFactory.Face(f, f.AgeOn(world.CurrentDate)),
                            new Rect(i % cols * cell + 8, i / cols * cell + 8, img, img));
                    }
                    // Body row: spread across weight classes for the build range.
                    var bodies = picks.Where(f => f.Sex == MmaManager.Core.Model.Sex.Male)
                        .GroupBy(f => f.WeightClass).Select(g => g.First())
                        .OrderBy(f => f.WeightClass).Take(5).ToList();
                    bodies.AddRange(picks.Where(f => f.Sex == MmaManager.Core.Model.Sex.Female).Take(1));
                    for (int i = 0; i < bodies.Count; i++)
                    {
                        var f = bodies[i];
                        dc.DrawImage(CharacterFactory.Body(f, f.AgeOn(world.CurrentDate)),
                            new Rect(i * bodyW + 8, bodyY, bodyImgW, bodyImgH));
                    }
                }
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args.Length > 1 ? e.Args[^1] : "characters.png";
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERCHARS OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERCHARS FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Contact sheet of the app-default portrait path (painted pack + fallback):
        // `MmaManager.Wpf --renderpainted <path>`. Face grid at sim ages, an aging
        // strip (one fighter at 22/30/40), and a body row — all via Portraits.
        if (e.Args.Contains("--renderpainted"))
        {
            try
            {
                var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 1234 });

                var byRegion = world.Fighters.Values.GroupBy(f => f.Nationality)
                    .Select(g => new Queue<MmaManager.Core.Model.Fighter>(g)).ToList();
                var picks = new List<MmaManager.Core.Model.Fighter>();
                for (int gi = 0; picks.Count < 32 && byRegion.Any(q => q.Count > 0); gi++)
                {
                    var q = byRegion[gi % byRegion.Count];
                    if (q.Count > 0) picks.Add(q.Dequeue());
                }
                foreach (var f in picks.Skip(24)) f.Sex = MmaManager.Core.Model.Sex.Female;

                const int cols = 8, cell = 110, img = 100;
                const int stripY = 4 * cell + 14, stripH = 110;
                const int bodyW = 140, bodyImgW = 126, bodyImgH = 189, bodyY = stripY + stripH + 14;
                int width = cols * cell + 10, height = bodyY + bodyImgH + 14;

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                    for (int i = 0; i < picks.Count && i < 32; i++)
                        dc.DrawImage(Portraits.For(picks[i], world),
                            new Rect(i % cols * cell + 8, i / cols * cell + 8, img, img));

                    // Aging strip: same fighter across the three life-stage bands.
                    var ager = picks[0];
                    int[] ages = { 22, 30, 40 };
                    for (int i = 0; i < ages.Length; i++)
                        dc.DrawImage(Art.PaintedLibrary.Face(ager, ages[i])
                                     ?? CharacterFactory.Face(ager, ages[i]),
                            new Rect(i * cell + 8, stripY, img, img));

                    var bodies = picks.Where(f => f.Sex == MmaManager.Core.Model.Sex.Male)
                        .GroupBy(f => f.WeightClass).Select(g => g.First())
                        .OrderBy(f => f.WeightClass).Take(5).ToList();
                    bodies.AddRange(picks.Where(f => f.Sex == MmaManager.Core.Model.Sex.Female).Take(1));
                    for (int i = 0; i < bodies.Count; i++)
                        dc.DrawImage(Portraits.Body(bodies[i], world),
                            new Rect(i * bodyW + 8, bodyY, bodyImgW, bodyImgH));
                }
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args.Length > 1 ? e.Args[^1] : "painted.png";
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERPAINTED OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERPAINTED FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Contact sheet of auto-generated fight posters: `MmaManager.Wpf --renderposter <path>`.
        // Eighteen assorted variants off the designed world — every template, many
        // org palettes — written as a grid sheet plus full-size singles ("-1.png"...).
        if (e.Args.Contains("--renderposter"))
        {
            try
            {
                var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
                MmaManager.Core.Model.Fighter F(string last) =>
                    world.Fighters.Values.First(f => f.LastName == last && f.Designed);
                MmaManager.Core.Model.Promotion Org(string abbr) =>
                    world.Promotions.Values.First(p => p.Abbreviation == abbr);
                // Top two of a division — poster pairs for orgs without designed rivals.
                (MmaManager.Core.Model.Fighter A, MmaManager.Core.Model.Fighter B) Pair(
                    string abbr, MmaManager.Core.Model.WeightClass wc,
                    MmaManager.Core.Model.Sex sex = MmaManager.Core.Model.Sex.Male)
                {
                    var two = world.RosterOf(Org(abbr).Id)
                        .Where(f => f.WeightClass == wc && f.Sex == sex && f.Status == MmaManager.Core.Model.FighterStatus.Active)
                        .OrderByDescending(MmaManager.Core.Sim.Ability.FighterAbility.CurrentAbility)
                        .Take(2).ToList();
                    return (two[0], two[1]);
                }
                MmaManager.Core.Model.Event Ev(string abbr, string name,
                    params (MmaManager.Core.Model.Fighter Red, MmaManager.Core.Model.Fighter Blue, bool Title)[] bouts)
                {
                    var org = Org(abbr);
                    var card = new MmaManager.Core.Model.Event
                    {
                        Id = world.NewEventId(), Promotion = org.Id, Name = name,
                        Date = world.CurrentDate.AddDays(30 + world.Events.Count % 90),
                        VenueId = MmaManager.Core.Sim.Geography.WorldMap.PickVenue(world, org)?.Id,
                    };
                    foreach (var (r, b, t) in bouts)   // billing order: last added = main event
                        card.Card.Add(new MmaManager.Core.Model.Fight
                        {
                            Id = world.NewFightId(), RedCorner = r.Id, BlueCorner = b.Id,
                            WeightClass = r.WeightClass, IsTitleFight = t,
                        });
                    return card;
                }

                // The BLB heavyweight war gets two completed meetings -> THE TRILOGY.
                var ozols = F("Ozols"); var urbonas = F("Urbonas");
                for (int i = 0; i < 2; i++)
                {
                    var past = Ev("BLB", $"War {i + 1}", (ozols, urbonas, false));
                    past.Status = MmaManager.Core.Model.EventStatus.Completed;
                    past.Card[0].Outcome = new MmaManager.Core.Model.FightOutcome
                    {
                        Winner = i == 0 ? ozols.Id : urbonas.Id,
                        Method = MmaManager.Core.Model.FightMethod.Knockout, EndRound = 2, EndTimeSeconds = 100,
                    };
                    world.Events[past.Id] = past;
                }
                // Teammates + family staging for the story taglines.
                var sameCamp = world.Camps.Values.First();
                var roth = F("Roth"); var teAriki = F("Te Ariki");
                roth.Camp = sameCamp.Id; teAriki.Camp = sameCamp.Id;
                var seo = F("Seo");
                var kid = world.Fighters.Values.First(x => x.Id != seo.Id && x.WeightClass == seo.WeightClass
                    && x.Sex == MmaManager.Core.Model.Sex.Male && x.Status == MmaManager.Core.Model.FighterStatus.Active && !x.Designed);
                world.Relationships.Add(new MmaManager.Core.Model.Relationship
                {
                    A = seo.Id, B = kid.Id, Kind = MmaManager.Core.Model.RelationshipKind.Family,
                    Strength = 85, Since = world.CurrentDate, Note = "younger brother",
                });

                var scmWomen = Pair("SCM", MmaManager.Core.Model.WeightClass.Flyweight, MmaManager.Core.Model.Sex.Female);
                var shots = new (MmaManager.Core.Model.Event Card, PosterFactory.Template? Force)[]
                {
                    (Ev("AFC", "AFC 300", (F("Mercer"), F("Seo"), true)), PosterFactory.Template.OneSheet),
                    (Ev("AFC", "AFC Fight Night", (F("Volkova"), F("Donovan"), true)), PosterFactory.Template.Gold),
                    (Ev("AFC", "AFC 304", (F("Volkova"), F("Donovan"), false), (F("Mercer"), F("Seo"), true)), PosterFactory.Template.DoubleBill),
                    (Ev("MCC", "Motor City Cage 9", (F("Barone"), F("Weeks"), true)), null),          // City => street bill
                    (Ev("CK", "Cage Kings 55", (F("Prado"), F("Sales"), true)), PosterFactory.Template.Split),
                    (Ev("AFC", "AFC 303", (seo, kid, false)), PosterFactory.Template.Marquee),
                    (Ev("BLB", "Baltic Brawl 44", (ozols, urbonas, true)), null),                     // street, THE TRILOGY
                    (Ev("MCC", "Motor City Cage 1"), null),                                           // street teaser
                    // --- the assorted ten ---
                    (Ev("AFC", "AFC 305", (roth, teAriki, false), (F("Volkova"), F("Donovan"), false), (F("Mercer"), F("Seo"), true)), PosterFactory.Template.TripleBill),
                    (Ev("ICF", "ICF: Emperor Night", (F("Sato"), Pair("ICF", MmaManager.Core.Model.WeightClass.Lightweight).B, true)), PosterFactory.Template.Gold),
                    (Ev("VMMA", "VMMA Uprising", (F("Vukovic"), Pair("VMMA", MmaManager.Core.Model.WeightClass.Heavyweight).B, true)), PosterFactory.Template.OneSheet),
                    (Ev("IFA", "IFA: The Hard School", (F("Volkov"), F("Baranov"), true)), PosterFactory.Template.Arena),
                    (Ev("GFC", "GFC 12", (F("Al-Marri"), Pair("GFC", MmaManager.Core.Model.WeightClass.Lightweight).B, true)), PosterFactory.Template.Marquee),
                    (Ev("UFL", "UFL on Prime", (F("Webb"), Pair("UFL", MmaManager.Core.Model.WeightClass.Middleweight).B, true)), PosterFactory.Template.Split),
                    (Ev("SCL", "SCL: Night in Lagos", (F("Okafor"), Pair("SCL", MmaManager.Core.Model.WeightClass.Middleweight).B, true)), PosterFactory.Template.Closeup),
                    (Ev("CPF", "Cape Fury 6", (F("Ndlovu"), F("Mokoena"), true)), null),              // street, the township feud
                    (Ev("SCM", "Southern Cross 30", (scmWomen.A, scmWomen.B, false), (F("Marsh"), Pair("SCM", MmaManager.Core.Model.WeightClass.Welterweight).B, true)), PosterFactory.Template.DoubleBill),
                    (Ev("AFC", "AFC 306"), PosterFactory.Template.OneSheet),                          // premium teaser
                    (Ev("AFC", "AFC 308", (roth, teAriki, false), (F("Mercer"), F("Seo"), true)), PosterFactory.Template.Stacked),
                    // --- the second wave: fifteen new looks ---
                    (Ev("UFL", "UFL 21", (F("Webb"), Pair("UFL", MmaManager.Core.Model.WeightClass.Middleweight).B, false)), PosterFactory.Template.Neon),
                    (Ev("VMMA", "VMMA: The Trials", (F("Vukovic"), Pair("VMMA", MmaManager.Core.Model.WeightClass.Heavyweight).B, false)), PosterFactory.Template.Broadsheet),
                    (Ev("CK", "Cage Kings 60", (F("Prado"), F("Sales"), false)), PosterFactory.Template.Brutalist),
                    (Ev("MCC", "Motor City Cage 12", (F("Barone"), F("Weeks"), false)), PosterFactory.Template.Vintage),
                    (Ev("AFC", "AFC 309", (F("Mercer"), F("Seo"), true)), PosterFactory.Template.Champion),
                    (Ev("ICF", "ICF: Invasion", (F("Sato"), Pair("ICF", MmaManager.Core.Model.WeightClass.Lightweight).B, false)), PosterFactory.Template.Slash),
                    (Ev("GFC", "GFC 14", (F("Al-Marri"), Pair("GFC", MmaManager.Core.Model.WeightClass.Lightweight).B, false)), PosterFactory.Template.RingLights),
                    (Ev("AFC", "AFC 310", (F("Volkova"), F("Donovan"), true)), PosterFactory.Template.Blackout),
                    (Ev("SCL", "SCL: Homecoming", (F("Okafor"), Pair("SCL", MmaManager.Core.Model.WeightClass.Middleweight).B, false)), PosterFactory.Template.Corners),
                    (Ev("IFA", "IFA: Proving Ground", (F("Volkov"), F("Baranov"), false)), PosterFactory.Template.Tape),
                    (Ev("CPF", "Cape Fury 8", (F("Ndlovu"), F("Mokoena"), false)), PosterFactory.Template.Hometown),
                    (Ev("AFC", "AFC 311", (roth, teAriki, false)), PosterFactory.Template.Countdown),
                    (Ev("ICF", "ICF: The Golden Age", (F("Sato"), Pair("ICF", MmaManager.Core.Model.WeightClass.Lightweight).B, true)), PosterFactory.Template.Deco),
                    (Ev("BLB", "Baltic Brawl 45", (ozols, urbonas, false)), PosterFactory.Template.Bolt),   // 2 past meetings
                    (Ev("SCM", "Southern Cross 32", (F("Marsh"), Pair("SCM", MmaManager.Core.Model.WeightClass.Welterweight).B, true)), PosterFactory.Template.Belt),
                };
                var posters = shots.Select(s => PosterFactory.For(world, s.Card, s.Force)).ToArray();

                // Full-size singles alongside the contact sheet.
                string basePath = e.Args.Length > 1 ? e.Args[^1] : "posters.png";
                for (int i = 0; i < posters.Length; i++)
                {
                    var one = new DrawingVisual();
                    using (var dc = one.RenderOpen()) dc.DrawImage(posters[i], new Rect(0, 0, 600, 800));
                    var bmp = new RenderTargetBitmap(600, 800, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(one);
                    var pngOne = new PngBitmapEncoder(); pngOne.Frames.Add(BitmapFrame.Create(bmp));
                    string onePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(basePath)) ?? ".",
                        Path.GetFileNameWithoutExtension(basePath) + $"-{i + 1}.png");
                    using var ofs = File.Create(onePath);
                    pngOne.Save(ofs);
                }

                const int pw = 330, ph = 440;
                const int cols = 6;
                int rows = (posters.Length + cols - 1) / cols;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, cols * (pw + 8) + 8, rows * (ph + 8) + 8));
                    for (int i = 0; i < posters.Length; i++)
                        dc.DrawImage(posters[i], new Rect(8 + i % cols * (pw + 8), 8 + i / cols * (ph + 8), pw, ph));
                }
                var rtb = new RenderTargetBitmap(cols * (pw + 8) + 8, rows * (ph + 8) + 8, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args.Length > 1 ? e.Args[^1] : "posters.png";
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERPOSTER OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERPOSTER FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Preview several portrait art styles side by side: `MmaManager.Wpf --stylegallery <path>`.
        if (e.Args.Contains("--stylegallery"))
        {
            try
            {
                var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 77 });
                // A handful of visually varied fighters (different regions/hair/beards).
                var samples = world.Fighters.Values.Take(60)
                    .GroupBy(f => f.Nationality).Select(g => g.First()).Take(4).ToList();
                while (samples.Count < 4) samples.Add(world.Fighters.Values.ElementAt(samples.Count));

                var styles = Enum.GetValues<PortraitStudio.Style>();
                const int label = 150, cell = 132, img = 120, headH = 40;
                int width = label + samples.Count * cell;
                int height = headH + styles.Length * cell;

                var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                var dg = new DrawingGroup();
                using (var dc = dg.Open())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                    var title = new FormattedText("Portrait styles — pick the ones you like", System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, typeface, 18, Brushes.Black, 1.0);
                    dc.DrawText(title, new Point(12, 10));

                    for (int r = 0; r < styles.Length; r++)
                    {
                        double y = headH + r * cell;
                        var name = new FormattedText(styles[r].ToString(), System.Globalization.CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, typeface, 14, Brushes.Black, 1.0);
                        dc.DrawText(name, new Point(12, y + cell / 2 - 10));
                        for (int cimg = 0; cimg < samples.Count; cimg++)
                        {
                            var f = samples[cimg];
                            var src = PortraitStudio.Render(f, f.AgeOn(world.CurrentDate), styles[r]);
                            dc.DrawImage(src, new Rect(label + cimg * cell + (cell - img) / 2, y + (cell - img) / 2, img, img));
                        }
                    }
                }
                var di = new DrawingImage(dg); di.Freeze();
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                var host = new System.Windows.Controls.Image { Source = di, Width = width, Height = height };
                host.Measure(new Size(width, height));
                host.Arrange(new Rect(0, 0, width, height));
                rtb.Render(host);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string gPath = e.Args.Length > 1 ? e.Args[^1] : "styles.png";
                using (var fs = File.Create(gPath)) enc.Save(fs);
                Console.WriteLine("STYLEGALLERY OK -> " + Path.GetFullPath(gPath));
            }
            catch (Exception ex) { Console.WriteLine("STYLEGALLERY FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Offscreen screenshot of the Book Event screen:
        // `MmaManager.Wpf --renderbooking [width height] <path>`.
        if (e.Args.Contains("--renderbooking"))
        {
            try
            {
                int at = Array.IndexOf(e.Args, "--renderbooking");
                bool sized = e.Args.Length > at + 2 && int.TryParse(e.Args[at + 1], out _) && int.TryParse(e.Args[at + 2], out _);
                int argW = sized ? int.Parse(e.Args[at + 1]) : 2600;
                int argH = sized ? int.Parse(e.Args[at + 2]) : 1040;
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                var bk = vm.Booking;
                bk.NewEventCommand.Execute(null);
                foreach (var div in bk.Divisions.ToList())
                {
                    bk.SelectedDivision = div;
                    if (bk.RedOptions.Count > 0)
                    {
                        bk.SelectedRed = bk.RedOptions[0];
                        if (bk.BlueOptions.Count > 0) { bk.SelectedBlue = bk.BlueOptions[0]; bk.AddBoutCommand.Execute(null); }
                    }
                }
                bk.SelectedDivision = bk.Divisions.FirstOrDefault();
                if (bk.RedOptions.Count > 0)
                {
                    bk.SelectedRed = bk.RedOptions[0];
                    bk.SuggestOpponentCommand.Execute(null);   // populate the appraisal strip
                }
                vm.NavigateTo(MmaManager.Wpf.ViewModels.Tab.BookEvent);

                var root = (FrameworkElement)win.Content;
                if (root is System.Windows.Controls.Panel panel) panel.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x1E));
                int W = argW, H = argH;   // defaults to an ultrawide-window size
                var size = new Size(W, H);
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                // Drain the dispatcher so the DataGrid's deferred star-column sizing runs,
                // then lay out again — otherwise the * column collapses in the offscreen pass.
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args.Length > 1 ? e.Args[^1] : "booking.png";
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERBOOKING OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERBOOKING FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Offscreen screenshot of the Contracts screen: `MmaManager.Wpf --rendercontracts <path>`.
        if (e.Args.Contains("--rendercontracts"))
        {
            try
            {
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                vm.AdvanceMonthCommand.Execute(null);   // let some deals age
                var ct = vm.Contracts;
                if (ct.FreeAgents.Count > 0) ct.SelectedFreeAgent = ct.FreeAgents[0];
                vm.NavigateTo(MmaManager.Wpf.ViewModels.Tab.Contracts);

                var root = (FrameworkElement)win.Content;
                if (root is System.Windows.Controls.Panel panel) panel.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x1E));
                const int W = 1700, H = 1000;
                var size = new Size(W, H);
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args.Length > 1 ? e.Args[^1] : "contracts.png";
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERCONTRACTS OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERCONTRACTS FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Offscreen screenshot of the Finance screen: `MmaManager.Wpf --renderfinance <path>`.
        if (e.Args.Contains("--renderfinance"))
        {
            try
            {
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                for (int i = 0; i < 4; i++) vm.AdvanceMonthCommand.Execute(null);   // build a few months of books
                vm.NavigateTo(MmaManager.Wpf.ViewModels.Tab.Finance);

                var root = (FrameworkElement)win.Content;
                if (root is System.Windows.Controls.Panel panel) panel.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x1E));
                const int W = 1600, H = 1000;
                var size = new Size(W, H);
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args.Length > 1 ? e.Args[^1] : "finance.png";
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERFINANCE OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERFINANCE FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Render the live-broadcast Watch screen: `MmaManager.Wpf --renderwatch <path>`.
        if (e.Args.Contains("--renderwatch"))
        {
            try
            {
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                var bk = vm.Booking;
                bk.NewEventCommand.Execute(null);
                foreach (var div in bk.Divisions.ToList())
                {
                    bk.SelectedDivision = div;
                    if (bk.RedOptions.Count > 0)
                    {
                        bk.SelectedRed = bk.RedOptions[0];
                        if (bk.BlueOptions.Count > 0) { bk.SelectedBlue = bk.BlueOptions[0]; bk.AddBoutCommand.Execute(null); }
                    }
                }
                bk.RunLiveCommand.Execute(null);   // runs the card live (headless: no window opens)
                vm.Watch.SkipToEnd();              // reveal the whole broadcast for the still image
                // Land the cage-side panel on a decision if there was one, so the
                // judges' card is part of the still.
                if (vm.Watch.BoutWithScorecard is >= 0 and var scored) vm.Watch.ShowBout(scored);

                // The broadcast lives in its own fight-night window now — render that.
                var watchWin = new WatchWindow { DataContext = vm };
                var root = (FrameworkElement)watchWin.Content;
                if (root is System.Windows.Controls.Panel panel) panel.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x1E));
                const int W = 1500, H = 950;
                var size = new Size(W, H);
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args.Length > 1 ? e.Args[^1] : "watch.png";
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERWATCH OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERWATCH FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Generic tab render: `MmaManager.Wpf --rendertab <index> <path>` (advances a year first to fill feeds).
        if (e.Args.Contains("--rendertab"))
        {
            try
            {
                int idx = int.Parse(e.Args[Array.IndexOf(e.Args, "--rendertab") + 1]);
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                for (int i = 0; i < 12; i++) vm.AdvanceMonthCommand.Execute(null);   // a year of world events
                if (e.Args.Contains("--myroster")) vm.MyRosterOnly = true;           // check the roster filter
                vm.NavigateTo(idx);

                var root = (FrameworkElement)win.Content;
                if (root is System.Windows.Controls.Panel panel) panel.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x1E));
                const int W = 1500, H = 950;
                var size = new Size(W, H);
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args[^1];
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERTAB OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERTAB FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Offscreen screenshot of the app (defaults to the Fighter profile page) so
        // the layout can be checked without a display: `MmaManager.Wpf --rendershot <path>`.
        if (e.Args.Contains("--rendershot"))
        {
            try
            {
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                for (int i = 0; i < 6; i++) vm.AdvanceMonthCommand.Execute(null);   // build up some history
                vm.SelectedFighter = vm.Roster.FirstOrDefault(r => r.StatusLine.Contains('★') && r.History.Count > 0)
                    ?? vm.Roster.FirstOrDefault(r => r.History.Count > 0) ?? vm.Roster.FirstOrDefault();
                vm.NavigateTo(MmaManager.Wpf.ViewModels.Tab.Fighter);

                var root = (FrameworkElement)win.Content;
                if (root is System.Windows.Controls.Panel panel) panel.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x1E));
                var size = new Size(1340, 820);
                root.Measure(size);
                root.Arrange(new Rect(size));
                root.UpdateLayout();

                var rtb = new RenderTargetBitmap(1340, 820, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                string shotPath = e.Args.Length > 1 ? e.Args[1] : "shot.png";
                using (var fs = File.Create(shotPath)) enc.Save(fs);
                Console.WriteLine("RENDERSHOT OK -> " + Path.GetFullPath(shotPath));
            }
            catch (Exception ex) { Console.WriteLine("RENDERSHOT FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Render a montage of portraits + logos to a PNG so the generated art can
        // be eyeballed without launching the full GUI: `MmaManager.Wpf --renderart <path>`.
        if (e.Args.Contains("--renderart"))
        {
            try
            {
                var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2026 });
                var fighters = world.Fighters.Values.Take(24).ToList();
                const int cols = 6, cell = 118, pad = 7, img = 104, rows = 4;
                int width = cols * cell, height = rows * cell + cell;

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                    for (int i = 0; i < fighters.Count && i < cols * rows; i++)
                        dc.DrawImage(PortraitFactory.ForFighter(fighters[i], fighters[i].AgeOn(world.CurrentDate)),
                            new Rect(i % cols * cell + pad, i / cols * cell + pad, img, img));
                    string[] logos = { "AFC", "VMMA", "ICF", "CK", "XYZ" };
                    for (int i = 0; i < logos.Length; i++)
                        dc.DrawImage(LogoFactory.ForAbbreviation(logos[i]),
                            new Rect(i * cell + pad, rows * cell + pad, img, img));
                }

                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                string outPath = e.Args.Length > 1 ? e.Args[1] : "art-preview.png";
                using (var fs = File.Create(outPath)) encoder.Save(fs);
                Console.WriteLine("RENDERART OK -> " + Path.GetFullPath(outPath));
            }
            catch (Exception ex) { Console.WriteLine("RENDERART FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Offscreen screenshot of the New Game setup dialog: `MmaManager.Wpf --rendernewgame <path>`.
        if (e.Args.Contains("--rendernewgame"))
        {
            try
            {
                var win = new NewGameWindow();
                var root = (FrameworkElement)win.Content;
                const int W = 640, H = 640;
                var size = new Size(W, H);
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args.Length > 1 ? e.Args[^1] : "newgame.png";
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERNEWGAME OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERNEWGAME FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // A tab of a freshly founded shoestring promotion (the zero-to-hero start):
        // `MmaManager.Wpf --renderfounded <tabIndex> <path>`.
        if (e.Args.Contains("--renderfounded"))
        {
            try
            {
                int idx = int.Parse(e.Args[Array.IndexOf(e.Args, "--renderfounded") + 1]);
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                vm.StartWorld(new WorldBuilder.Options
                {
                    Seed = 7,
                    Founding = new WorldBuilder.FoundingSpec
                    {
                        Name = "New Wave Fighting",
                        Abbreviation = "NWF",
                        Region = MmaManager.Core.Model.Region.Europe,
                    },
                }, "You founded New Wave Fighting.");
                vm.NavigateTo(idx);
                if (idx == MmaManager.Wpf.ViewModels.Tab.Contracts) vm.Contracts.SuggestSigningCommand.Execute(null);   // show the scout's pick

                var root = (FrameworkElement)win.Content;
                if (root is System.Windows.Controls.Panel panel) panel.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x1E));
                const int W = 1500, H = 950;
                var size = new Size(W, H);
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args[^1];
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERFOUNDED OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERFOUNDED FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Headless smoke test (no window): `MmaManager.Wpf --selftest`. Lets us
        // confirm the view model builds a world and its screens populate without
        // a display, since the GUI itself can only be verified by launching it.
        if (e.Args.Contains("--selftest"))
        {
            try
            {
                var vm = new MainViewModel();
                Console.WriteLine($"SELFTEST: promotions={vm.Promotions.Count} roster={vm.Roster.Count} " +
                                  $"events={vm.EventsPage.Events.Count} standings={vm.Standings.Count}");
                vm.AdvanceMonthCommand.Execute(null);
                Console.WriteLine($"After advance -> events={vm.EventsPage.Events.Count}");

                vm.SelectedFighter = vm.Roster.FirstOrDefault();
                var sf = vm.SelectedFighter;
                Console.WriteLine($"Profile: {sf?.Name} | ratingGroups={sf?.RatingGroups.Count} | bio={sf?.Bio}");
                vm.ViewProfileCommand.Execute(null);
                Console.WriteLine($"ViewProfile -> windows open: {string.Join(",", vm.Desktop.Windows.Select(w => w.Spec.Id))}");

                // Click-through: opening a fighter from a rankings row works from anywhere.
                if (vm.Standings.FirstOrDefault() is { } ranked)
                {
                    vm.OpenFighter(ranked.FighterId);
                    Console.WriteLine($"OpenFighter({ranked.FighterId}) -> profile open: {vm.Desktop.Windows.Any(w => w.Spec.Id == "fighter")}, showing {vm.SelectedFighter?.Name}");
                }

                var art = vm.Roster.FirstOrDefault();
                Console.WriteLine($"Art: portrait={art?.Portrait != null}, orgLogo={art?.OrgLogo != null}, "
                    + $"playerLogo={vm.PlayerLogo != null}, promoLogo={vm.Promotions.FirstOrDefault()?.Logo != null}");

                var withHistory = vm.Roster.FirstOrDefault(r => r.History.Count > 0);
                Console.WriteLine(withHistory is null
                    ? "History: no bouts fought yet."
                    : $"History sample: {withHistory.Name} has {withHistory.History.Count} bout(s); latest = {withHistory.History[0].Result} vs {withHistory.History[0].Opponent}.");

                // Booking: create a card, add a bout, advance, confirm it ran.
                var bk = vm.Booking;
                bk.NewEventCommand.Execute(null);
                bk.SelectedDivision = bk.Divisions.FirstOrDefault();
                Console.WriteLine($"Booking: upcoming={bk.UpcomingEvents.Count}, red options={bk.RedOptions.Count}");
                if (bk.RedOptions.Count > 0)
                {
                    bk.SelectedRed = bk.RedOptions[0];
                    if (bk.BlueOptions.Count > 0)
                    {
                        bk.SelectedBlue = bk.BlueOptions[0];
                        bk.AddBoutCommand.Execute(null);
                        Console.WriteLine($"Booked bout -> card has {bk.Bouts.Count} bout(s). status='{bk.Status}'");
                    }
                }
                var bookedId = bk.SelectedEvent?.Model.Id;
                vm.AdvanceMonthCommand.Execute(null);
                Console.WriteLine("After advancing, player event ran = " +
                    (bookedId is { } bid && !vm.Booking.UpcomingEvents.Any(u => u.Model.Id == bid)));

                // Contracts: scout a free agent and sign them at their ask.
                var ct = vm.Contracts;
                Console.WriteLine($"Contracts: freeAgents={ct.FreeAgents.Count}, myRoster={ct.MyContracts.Count}");
                if (ct.FreeAgents.Count > 0)
                {
                    int rosterBefore = ct.MyContracts.Count;
                    ct.SelectedFreeAgent = ct.FreeAgents[0];
                    Console.WriteLine($"Target: {ct.TargetName} | {ct.Asking}");
                    bool inked = ct.SignAtTheirTerms();
                    Console.WriteLine($"Meet-their-terms -> signed={inked}, '{ct.Response}', roster {rosterBefore} -> {ct.MyContracts.Count}");
                }

                // Art coverage: designed headliners should hold three-stage identities,
                // and portrait persona tags should be stamped onto the model.
                var artWorld = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2026 });
                var designedSet = artWorld.Fighters.Values.Where(f => f.Designed).ToList();
                int triptychs = designedSet.Count(Art.PaintedLibrary.HasTriptychFor);
                int personas = artWorld.Fighters.Values.Count(f => Art.PaintedLibrary.PersonaFor(f) is not null);
                Console.WriteLine($"Art: designed with 3-stage portraits = {triptychs}/{designedSet.Count}, persona-tagged fighters = {personas}");

                Console.WriteLine("SELFTEST OK: " + vm.LastAdvance);
            }
            catch (Exception ex)
            {
                Console.WriteLine("SELFTEST FAIL: " + ex);
            }
            // Force-exit so the headless run can't linger and lock the output DLL.
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);
    }
}
