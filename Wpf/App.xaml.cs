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

        // The crash net (log + rescue save + tell the player) is for real play only.
        // Harness modes must keep failing loudly on the console with a non-zero exit.
        if (!MainViewModel.HeadlessAutoBoot) CrashGuard.Install(this);

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

                // Leave a result on the shell's result strip: a real shortlist pin
                // (routine), or with --alert a must-not-miss one (headless: strip only,
                // never a dialog — the dialog itself is rendered to <path>-dialog.png).
                if (e.Args.Contains("--alert"))
                    vm.Alert("SAVE FAILED", "Save failed — afc-career.mmasave was not written: Access to the path is denied.",
                        "The career was not saved to afc-career.mmasave. Nothing in the game has changed; try saving again, to another name or folder.",
                        "Access to the path is denied.");
                else if (vm.Roster.FirstOrDefault() is { } pin)
                    vm.ToggleShortlistCommand.Execute(pin.Id);

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

                if (e.Args.Contains("--alert"))
                {
                    var dlg = new MessageWindow("SAVE FAILED",
                        "The career was not saved to afc-career.mmasave. Nothing in the game has changed; try saving again, to another name or folder.",
                        "Access to the path is denied.", severe: true);
                    var droot = (FrameworkElement)dlg.Content;
                    droot.Measure(new Size(480, double.PositiveInfinity));
                    var dsize = new Size(480, Math.Ceiling(droot.DesiredSize.Height));
                    droot.Arrange(new Rect(dsize)); droot.UpdateLayout();
                    var drtb = new RenderTargetBitmap((int)dsize.Width, (int)dsize.Height, 96, 96, PixelFormats.Pbgra32);
                    drtb.Render(droot);
                    var denc = new PngBitmapEncoder(); denc.Frames.Add(BitmapFrame.Create(drtb));
                    string dp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(p))!, Path.GetFileNameWithoutExtension(p) + "-dialog.png");
                    using (var dfs = File.Create(dp)) denc.Save(dfs);
                    Console.WriteLine("RENDERDESKTOP DIALOG OK -> " + dp);
                }
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

        // Corporate art through the loaders: `MmaManager.Wpf --rendercorporate <path>`. Every person (PeopleLibrary)
        // and every logo (BrandLibrary), plus two unknown names to show the fallbacks.
        if (e.Args.Contains("--rendercorporate"))
        {
            try
            {
                var people = Art.PeopleLibrary.Names.OrderBy(n => n, StringComparer.Ordinal).ToList();
                people.Add("Unpictured Person"); people.Add("Dr. Missing Name");
                var brands = Art.BrandLibrary.Names().ToList();
                brands.Add("Unknown Partner Co."); brands.Add("Made Up Lager");
                const int pc = 10, pcell = 100, bc = 12, bcell = 84;
                int prow = (people.Count + pc - 1) / pc, brow = (brands.Count + bc - 1) / bc;
                int width = Math.Max(pc * pcell, bc * bcell) + 16, height = prow * pcell + brow * bcell + 40;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x34)), null, new Rect(0, 0, width, height));
                    for (int i = 0; i < people.Count; i++)
                        dc.DrawImage(Art.PeopleLibrary.Portrait(people[i]), new Rect(8 + i % pc * pcell, 8 + i / pc * pcell, pcell - 6, pcell - 6));
                    int by = prow * pcell + 24;
                    for (int i = 0; i < brands.Count; i++)
                        dc.DrawImage(Art.BrandLibrary.Logo(brands[i]), new Rect(8 + i % bc * bcell, by + i / bc * bcell, bcell - 6, bcell - 6));
                }
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args.Length > 1 ? e.Args[^1] : "corporate.png";
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine($"RENDERCORPORATE OK ({people.Count} people, {brands.Count} logos) -> " + Path.GetFullPath(p));
                // What the screens draw (controls:CorporateArt): faces cropped, logos trimmed.
                Console.WriteLine($"RENDERCORPORATE screens: {people.Count(MmaManager.Wpf.Controls.CorporateArt.HasFace)} real faces, "
                    + $"{brands.Count(MmaManager.Wpf.Controls.CorporateArt.HasBrand)} trimmed logos; "
                    + string.Join(", ", brands.Take(3).Select(b => MmaManager.Wpf.Controls.CorporateArt.Logo(b) is BitmapSource s ? $"{b} {s.PixelWidth}x{s.PixelHeight}" : $"{b} none")));
            }
            catch (Exception ex) { Console.WriteLine("RENDERCORPORATE FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Origins pioneers through the name binding: `MmaManager.Wpf --renderpioneers <path>`. Each pioneer's face + body from
        // PaintedLibrary (bare base, then the tops-on pair where there is one), plus a namesake-less control that must fall through.
        if (e.Args.Contains("--renderpioneers"))
        {
            try
            {
                string[] names = { "Walt Corbin", "Lemalu Tofaeono", "Lyle Brannock", "Aurelio Neves", "Tetsuya Hagiwara", "Kiyoshi Aizawa",
                                   "Jurandir Salles", "Pongsak Thanasarn", "Dermot Quirke", "Ademir Bastos", "Nobody Atall" };
                var fighters = names.Select((n, i) => new MmaManager.Core.Model.Fighter
                {
                    Id = new MmaManager.Core.Model.FighterId(9000 + i),
                    FirstName = n.Split(' ')[0], LastName = n.Split(' ')[1], BirthDate = new DateOnly(1960, 1, 1),
                }).ToList();
                const int cell = 150, bodyH = 300, pad = 8;
                int width = (names.Length + 2) * (cell + pad) + pad, height = cell + bodyH * 2 + pad * 4;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x34)), null, new Rect(0, 0, width, height));
                    for (int i = 0; i < fighters.Count; i++)
                    {
                        var f = fighters[i];
                        double x = pad + i * (cell + pad);
                        Console.WriteLine($"{names[i]}: pioneer={Art.PaintedLibrary.IsPioneer(f)} tops={Art.PaintedLibrary.HasPioneerTops(f)}");
                        if (Art.PaintedLibrary.PioneerFace(f) is { } face) dc.DrawImage(face, new Rect(x, pad, cell, cell));
                        if (Art.PaintedLibrary.PioneerBody(f) is { } body) dc.DrawImage(body, new Rect(x, cell + pad * 2, cell * 0.38, bodyH));
                        if (Art.PaintedLibrary.PioneerBody(f, tops: true) is { } tb && Art.PaintedLibrary.HasPioneerTops(f))
                            dc.DrawImage(tb, new Rect(x, cell + bodyH + pad * 3, cell * 0.38, bodyH));
                    }
                }
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args.Length > 1 ? e.Args[^1] : "pioneers.png";
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERPIONEERS OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERPIONEERS FAIL: " + ex); }
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

        // Offscreen screenshot of the Card Desk (Book Event):
        // `MmaManager.Wpf --renderbooking [W H] [--floating] [--founded] [--event] [--bouts N]
        //  [--maincard N] [--picker suggest|ladder|opponent|division|undercard|replace|local]
        //  [--search text] [--filters NeedsFight,Ranked] [--room] [--propose] [--look]
        //  [--sheettable] [--table] [--poster] [--confirm] <path>`.
        // ladder: the finder browsing the roster; opponent: a first corner chosen
        // (his opponents, best fight first); division: champion vs his best
        // opponent on the fight sheet (a title fight); undercard: two unranked on
        // the sheet; replace: a corner change previewed on the sheet; --look: the
        // sheet for the first bulk proposal; --sheettable: the sheet's Table toggle.
        // W x H is the whole screen (desk + taskbar); the app is maximized on it,
        // or floating at its default size with --floating. The big org (AFC) by
        // default: a card at the top tier it can stage, filled the way a player
        // would (the whole card proposed and booked; --bouts N keeps the first N).
        // --founded: a freshly founded promotion on day one (no fighters, no card;
        // --event puts its first card on the calendar). --maincard N sets the
        // split; --picker opens the opponent picker in a mode (division: a
        // pairing compared; replace: change a corner of the co-main); --room the
        // room chooser; --propose an undercard proposal; --table / --poster the
        // other views; --confirm also draws the Cancel question.
        if (e.Args.Contains("--renderbooking"))
        {
            try
            {
                int at = Array.IndexOf(e.Args, "--renderbooking");
                bool sized = e.Args.Length > at + 2 && int.TryParse(e.Args[at + 1], out _) && int.TryParse(e.Args[at + 2], out _);
                int W = sized ? int.Parse(e.Args[at + 1]) : 1600;
                int H = sized ? int.Parse(e.Args[at + 2]) : 950;
                string? Arg(string flag) { int i = Array.IndexOf(e.Args, flag); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null; }
                bool founded = e.Args.Contains("--founded");
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                var bk = vm.Booking;
                if (founded)
                {
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
                    // --sign N: the first signings, at their asking price, as a player would.
                    if (int.TryParse(Arg("--sign"), out var sign))
                    {
                        var ct = vm.Contracts;
                        for (int i = 0; i < sign && ct.FreeAgents.Count > 0; i++)
                        {
                            ct.SelectedFreeAgent = ct.FreeAgents[0];
                            if (!ct.SignAtTheirTerms()) Console.WriteLine("RENDERBOOKING could not sign " + ct.TargetName);
                        }
                        vm.Contracts.OnChanged?.Invoke();
                    }
                    if (e.Args.Contains("--event")) bk.NewEventCommand.Execute(null);
                }
                else
                {
                    bk.SelectedTier = bk.Tiers.Last();
                    bk.NewEventCommand.Execute(null);
                }
                if (bk.SelectedEvent is not null && (!founded || Arg("--bouts") is not null))
                {
                    int keep = int.TryParse(Arg("--bouts"), out var nb) ? nb : int.MaxValue;
                    if (keep > 0)
                    {
                        bk.ProposeWholeCardCommand.Execute(null);
                        int k = 0;
                        foreach (var p in bk.Proposed) p.IsChecked = k++ < keep;
                        bk.BookProposedCommand.Execute(null);
                    }
                    if (int.TryParse(Arg("--maincard"), out var mc))
                        bk.SelectedMainCard = bk.MainCardOptions.FirstOrDefault(o => o.Value == mc) ?? bk.SelectedMainCard;
                }
                switch (Arg("--picker"))
                {
                    case "suggest":
                    {
                        var open = System.Diagnostics.Stopwatch.StartNew();
                        bk.OpenNextSlotCommand.Execute(null);
                        Console.WriteLine($"RENDERBOOKING suggestions: {bk.Suggestions.Count} cards with the night projected, opened in {open.Elapsed.TotalMilliseconds:0} ms");
                        open.Restart();
                        bk.PickMode = PickMode.Division;
                        Console.WriteLine($"RENDERBOOKING finder: built {bk.Ladder.Count} rows in {open.Elapsed.TotalMilliseconds:0} ms (first build reads every fighter)");
                        open.Restart();
                        bk.PickMode = PickMode.Suggested;
                        bk.PickMode = PickMode.Division;
                        Console.WriteLine($"RENDERBOOKING finder: rebuilt {bk.Ladder.Count} rows in {open.Elapsed.TotalMilliseconds:0} ms (facts cached)");
                        bk.PickMode = PickMode.Suggested;
                        break;
                    }
                    case "division":
                        bk.OpenNextSlotCommand.Execute(null);
                        bk.PickMode = PickMode.Division;
                        var div = bk.Divisions.FirstOrDefault(d => { bk.SelectedDivision = d; return bk.RedOptions.Count >= 2; });
                        if (div is not null)
                        {
                            bk.PickLadderCommand.Execute(bk.Ladder.First(l => l.Available));
                            if (bk.Ladder.FirstOrDefault(l => l.Available) is { } opp) bk.PickLadderCommand.Execute(opp);
                        }
                        break;
                    case "ladder":
                        // the finder, browsing the whole roster
                        bk.OpenNextSlotCommand.Execute(null);
                        bk.PickMode = PickMode.Division;
                        break;
                    case "opponent":
                        // the finder with a first corner chosen: his opponents, best fight first
                        bk.OpenNextSlotCommand.Execute(null);
                        bk.PickMode = PickMode.Division;
                        if (bk.Ladder.FirstOrDefault(l => l.Available && l.Rank != "C") is { } first) bk.PickLadderCommand.Execute(first);
                        break;
                    case "undercard":
                        // an ordinary undercard pairing on the fight sheet: two unranked fighters
                        bk.OpenNextSlotCommand.Execute(null);
                        bk.PickMode = PickMode.Division;
                        foreach (var nr in bk.Ladder.Where(l => l.Available && l.Rank == "NR").ToList())
                        {
                            bk.PickLadderCommand.Execute(nr);
                            if (bk.Ladder.FirstOrDefault(l => l.Available && l.Rank == "NR") is { } opp2) { bk.PickLadderCommand.Execute(opp2); break; }
                            bk.ClearRedCommand.Execute(null);
                        }
                        break;
                    case "replace":
                        if ((bk.Bouts.Skip(1).FirstOrDefault() ?? bk.Bouts.FirstOrDefault()) is { } target) bk.ReplaceCornerCommand.Execute(target);
                        // the first available replacement previewed on the sheet
                        if (bk.Ladder.FirstOrDefault(l => l.Available) is { } inc) bk.PreviewReplaceCommand.Execute(inc);
                        break;
                    case "local":
                        bk.OpenNextSlotCommand.Execute(null);
                        bk.PickMode = PickMode.Local;
                        if (bk.Ladder.FirstOrDefault(l => l.Available) is { } mine) bk.PickLadderCommand.Execute(mine);
                        break;
                }
                // The finder: --search text, --filters NeedsFight,WinStreak (chip names), timed.
                if (Arg("--filters") is { } chips)
                    foreach (var c in bk.FinderChips)
                        if (chips.Split(',').Any(x => string.Equals(x.Trim(), c.Filter.ToString(), StringComparison.OrdinalIgnoreCase))) c.IsOn = true;
                if (Arg("--search") is { } q)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    bk.FinderSearch = q;
                    Console.WriteLine($"RENDERBOOKING search '{q}': {(bk.ShowTableView ? bk.TableRows.Count : bk.Ladder.Count)} rows ({bk.FinderCount}), "
                                      + $"filter+sort {bk.LastFinderMs:0.0} ms, rebuild {sw.Elapsed.TotalMilliseconds:0.0} ms");
                }
                if (e.Args.Contains("--sheettable")) bk.ShowSheetTable = true;
                if (e.Args.Contains("--room")) bk.ChangeRoomCommand.Execute(null);
                if (e.Args.Contains("--propose")) bk.ProposeUndercardCommand.Execute(null);
                // --look: "Look closer" on the first proposal (the bulk list's fight sheet).
                if (e.Args.Contains("--look") && bk.Proposed.FirstOrDefault() is { } look) bk.LookCloserCommand.Execute(look);
                if (e.Args.Contains("--table")) bk.View = DeskView.Table;
                if (e.Args.Contains("--poster")) bk.View = DeskView.Poster;
                vm.NavigateTo(MmaManager.Wpf.ViewModels.Tab.BookEvent);

                var root = (FrameworkElement)win.Content;
                if (root is System.Windows.Controls.Panel panel) panel.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x1E));
                var size = new Size(W, H);
                void Layout()
                {
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                }
                Layout();
                if (e.Args.Contains("--floating") && vm.Desktop.Windows.FirstOrDefault(w => w.Spec.Id == "book") is { } book)
                {
                    book.IsMaximized = false;
                    book.X = 46; book.Y = 26; book.W = book.Spec.W; book.H = book.Spec.H;
                    vm.Desktop.FitToArea(book);
                    Console.WriteLine($"RENDERBOOKING floating window {book.W:0}x{book.H:0} on a {vm.Desktop.Area.Width:0}x{vm.Desktop.Area.Height:0} desk");
                }
                // Faces decode on worker threads; the still waits for them.
                var t = System.Diagnostics.Stopwatch.StartNew();
                do
                {
                    Layout();
                    System.Threading.Thread.Sleep(2);
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                } while (Portraits.PendingThumbs > 0 && t.ElapsedMilliseconds < 15000);
                Layout();

                foreach (var view in FindAll<MmaManager.Wpf.Views.BookingView>(root))
                {
                    // Read the screen back: any one-line text that needs more room than it
                    // has? Trimmed text ends in "..."; unwrapped, untrimmed text is simply
                    // cut off at its edge (the "This is automatic for nc" kind), so both are
                    // counted. Each run is measured in its own font, size and weight.
                    int trimmed = 0;
                    foreach (var tb in FindAll<System.Windows.Controls.TextBlock>(view).Where(x => Shown(x, view) && x.ActualWidth > 0 && x.TextWrapping == TextWrapping.NoWrap))
                    {
                        double dpi = VisualTreeHelper.GetDpi(tb).PixelsPerDip, need = 0;
                        foreach (var run in tb.Inlines.OfType<System.Windows.Documents.Run>())
                        {
                            if (string.IsNullOrEmpty(run.Text)) continue;
                            var face = new Typeface(run.FontFamily, run.FontStyle, run.FontWeight, run.FontStretch);
                            need += new FormattedText(run.Text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, run.FontSize,
                                Brushes.White, dpi).WidthIncludingTrailingWhitespace;
                        }
                        // An unwrapped line wider than its slot keeps its full ActualWidth and
                        // is clipped by layout, so the room is the slot the parent gave it.
                        var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(tb);
                        double room = Math.Min(tb.ActualWidth, slot.Width - tb.Margin.Left - tb.Margin.Right);
                        need = Math.Max(need, tb.ActualWidth);
                        if (need > room + 1)
                        {
                            trimmed++;
                            string text = new System.Windows.Documents.TextRange(tb.ContentStart, tb.ContentEnd).Text;
                            Console.WriteLine($"  {(tb.TextTrimming == TextTrimming.None ? "CLIPPED" : "TRIMMED")} ({need:0} > {room:0}): {text}");
                        }
                    }
                    Console.WriteLine($"RENDERBOOKING screen {view.ActualWidth:0}x{view.ActualHeight:0} | event {bk.SelectedEvent?.Name} | bouts {bk.Bouts.Count} "
                        + $"(main card {bk.MainCardRows.Count + (bk.HasMainEvent ? 1 : 0)}, prelims {bk.PrelimRows.Count}) | rail {bk.Rail} | view {bk.View} "
                        + $"| missed terms {bk.Warnings.Count} | padding {(bk.HasPadding ? bk.PaddingBouts.Count : 0)} | trimmed lines {trimmed}");
                }
                if (bk.HasStatus) Console.WriteLine($"RENDERBOOKING status: {bk.Status}");
                if (bk.IsRailPicker) Console.WriteLine($"RENDERBOOKING finder: {bk.FinderCount} | ladder {bk.Ladder.Count} | last filter {bk.LastFinderMs:0.0} ms | sheet {(bk.HasSheet ? bk.Sheet!.Red.Name + " vs " + bk.Sheet.Blue.Name : "none")}");
                else if (bk.HasSheet) Console.WriteLine($"RENDERBOOKING sheet {bk.Sheet!.Red.Name} vs {bk.Sheet.Blue.Name}");

                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string path = e.Args.Length > 1 ? e.Args[^1] : "booking.png";
                using (var fs = File.Create(path)) enc.Save(fs);
                Console.WriteLine("RENDERBOOKING OK -> " + Path.GetFullPath(path));

                // --confirm: also draw the Cancel question (never shown as a dialog here) to <path>-confirm.png.
                if (e.Args.Contains("--confirm") && bk.DescribeCancel() is var (head, body, detail))
                {
                    var dlg = new MessageWindow(head, body, detail, severe: true, confirm: "Cancel the event", keep: "Keep the event");
                    var droot = (FrameworkElement)dlg.Content;
                    droot.Measure(new Size(480, double.PositiveInfinity));
                    var dsize = new Size(480, Math.Ceiling(droot.DesiredSize.Height));
                    droot.Arrange(new Rect(dsize)); droot.UpdateLayout();
                    var drtb = new RenderTargetBitmap((int)dsize.Width, (int)dsize.Height, 96, 96, PixelFormats.Pbgra32);
                    drtb.Render(droot);
                    var denc = new PngBitmapEncoder(); denc.Frames.Add(BitmapFrame.Create(drtb));
                    string dp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileNameWithoutExtension(path) + "-confirm.png");
                    using (var dfs = File.Create(dp)) denc.Save(dfs);
                    Console.WriteLine("RENDERBOOKING CONFIRM OK -> " + dp);
                }
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

        // THE MOMENT over the desk: `MmaManager.Wpf --rendermoment <W> <H> [--first] <path>`.
        // The quick-start world (seed 2026) books tonight's card the way a player does —
        // every belt on the line, champion against the best contender who is free — and sims
        // it through the real fight-night path; a belt that changes hands takes the screen.
        // A night with no new champion moves on a week and tries again. --first commissions a
        // new belt in a division without one, so the night crowns its first champion.
        if (e.Args.Contains("--rendermoment"))
        {
            try
            {
                int at = Array.IndexOf(e.Args, "--rendermoment");
                int W = int.Parse(e.Args[at + 1]), H = int.Parse(e.Args[at + 2]);
                bool first = e.Args.Contains("--first");
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                var booking = new PlayerBooking();
                int nights = 0;
                if (first)
                {
                    // A founder's first belt: a new shoestring promotion signs two free agents of
                    // one weight at their asking terms, then sanctions the belt they will fight for.
                    vm.StartWorld(new WorldBuilder.Options
                    {
                        Seed = 7,
                        Founding = new WorldBuilder.FoundingSpec { Name = "New Wave Fighting", Abbreviation = "NWF", Region = MmaManager.Core.Model.Region.Europe },
                    }, "You founded New Wave Fighting.");
                    var fw = vm.LiveWorld!;
                    var pool = vm.Contracts.FreeAgents.Select(r => fw.Get(r.Id)).OfType<MmaManager.Core.Model.Fighter>()
                        .Where(f => f.Status == MmaManager.Core.Model.FighterStatus.Active && f.IsMatchReady(fw.CurrentDate))
                        .GroupBy(f => (f.WeightClass, f.Sex)).Where(g => g.Count() >= 2)
                        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.WeightClass).First().Select(f => f.Id).ToList();
                    // Sign four (fight week can cost a bout to an injury; the next week books the others).
                    foreach (var id in pool)
                    {
                        if (fw.RosterOf(fw.PlayerPromotion).Count() >= 4) break;
                        if (vm.Contracts.FreeAgents.FirstOrDefault(r => r.Id == id) is not { } row) continue;
                        vm.Contracts.SelectedFreeAgent = row;
                        bool signed = vm.Contracts.SignAtTheirTerms();
                        Console.WriteLine($"RENDERMOMENT signed {fw.Get(id)?.FullName}: {signed}, ready {fw.Get(id)?.IsMatchReady(fw.CurrentDate)} (from {fw.Get(id)?.ReadyToFightOn})");
                    }
                }
                for (int attempt = 0; attempt < 16 && !vm.Moments.IsShowing; attempt++)
                {
                    if (attempt > 0) vm.AdvanceWeekCommand.Execute(null);
                    var w = vm.LiveWorld!;
                    var promo = w.Get(w.PlayerPromotion)!;
                    var date = w.CurrentDate;
                    var ev = booking.CreateEvent(w, date);
                    bool Free(MmaManager.Core.Model.Fighter f) =>
                        f.Status == MmaManager.Core.Model.FighterStatus.Active && f.Contract?.Promotion == promo.Id && f.IsMatchReady(date)
                        && !w.Events.Values.Any(x => x.Status == MmaManager.Core.Model.EventStatus.Scheduled
                                                     && x.Card.Any(b => b.RedCorner == f.Id || b.BlueCorner == f.Id));
                    if (first)
                    {
                        // A division of yours with two fighters ready and no champion: sanction its
                        // belt if it has none (a founded promotion's belts start vacant).
                        bool Unclaimed((MmaManager.Core.Model.WeightClass Wc, MmaManager.Core.Model.Sex Sex) k) =>
                            promo.Divisions.FirstOrDefault(d => d.WeightClass == k.Wc && d.Sex == k.Sex)?.Title is not { } t
                            || w.Get(t) is { CurrentChampion: null, Lineage.Count: 0 };
                        var pick = w.RosterOf(promo.Id).Where(Free)
                            .GroupBy(f => (f.WeightClass, f.Sex))
                            .Where(g => g.Count() >= 2 && Unclaimed(g.Key))
                            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.WeightClass).FirstOrDefault();
                        if (pick is null)
                        {
                            Console.WriteLine("RENDERMOMENT no unclaimed division with two fighters ready: "
                                + string.Join("; ", w.RosterOf(promo.Id).Select(f => $"{f.LastName} {f.WeightClass} ready={f.IsMatchReady(date)} free={Free(f)}")));
                            break;
                        }
                        PromotionSetup.CommissionTitle(w, promo, pick.Key.WeightClass, pick.Key.Sex, date);
                        var two = pick.OrderByDescending(PromotionSetup.SeedScore).ThenBy(f => f.Id.Value).Take(2).ToList();
                        if (!booking.AddBout(w, ev, two[0].Id, two[1].Id, 5, true, out var why))
                            Console.WriteLine("RENDERMOMENT could not book the first title fight: " + why);
                    }
                    else
                    {
                        foreach (var div in promo.Divisions)
                        {
                            if (div.Title is not { } tid || w.Get(tid)?.CurrentChampion is not { } cid || w.Get(cid) is not { } champ || !Free(champ)) continue;
                            var challenger = div.Rankings.Select(id => w.Get(id)).FirstOrDefault(c => c is not null && c.Id != cid && Free(c) && div.Matches(c));
                            if (challenger is not null)
                            {
                                booking.AddBout(w, ev, champ.Id, challenger.Id, 5, true, out _);
                                // --tie: harness only, plant bad blood between the two so the quotes can say so
                                // (Quotes.Say with the world picks a tie line on about 70% of calls for rivals).
                                if (e.Args.Contains("--tie") && !w.Rivalries.Any(r => r.Involves(champ.Id) && r.Involves(challenger.Id)))
                                    w.Rivalries.Add(new MmaManager.Core.Model.Rivalry { A = champ.Id, B = challenger.Id, Intensity = 85, Since = date.AddMonths(-8) });
                            }
                        }
                    }
                    if (ev.Card.Count == 0) { booking.CancelEvent(w, ev); continue; }
                    // Run every card of yours that is due tonight (ours, and any already on the calendar).
                    while (!vm.Moments.IsShowing && w.Events.Values.Any(x => x.Status == MmaManager.Core.Model.EventStatus.Scheduled
                               && x.Promotion == w.PlayerPromotion && x.Date <= w.CurrentDate))
                    {
                        vm.SimTonightCommand.Execute(null);
                        nights++;
                    }
                    foreach (var b in ev.Card)
                        Console.WriteLine($"RENDERMOMENT night {nights}: {w.Get(b.RedCorner)?.LastName} v {w.Get(b.BlueCorner)?.LastName} title={b.IsTitleFight} missBy={b.WeightMissBy} -> {b.Outcome?.Method} winner {(b.Outcome?.Winner is { } wn ? w.Get(wn)?.LastName : "-")}");
                }
                if (vm.Moments.Current is not { } m)
                {
                    Console.WriteLine($"RENDERMOMENT FAIL: no belt changed hands in {nights} nights");
                    Environment.Exit(1);
                    return;
                }
                Console.WriteLine($"RENDERMOMENT after {nights} night(s): {m.GetType().Name} - {m.Headline} {m.Name}, {m.Subline} | {m.Kicker}");
                Console.WriteLine($"RENDERMOMENT quote: {m.Quote} | answer: {m.Answer?.Text} (face {m.Answer?.HasPicture}) | {m.AlsoLine}");
                if (m.SubjectId is int sid && vm.LiveWorld!.Get(new MmaManager.Core.Model.FighterId(sid)) is { } champNow)
                {
                    var mw = vm.LiveWorld!;
                    var lastTitle = mw.Events.Values.Where(x => x.Promotion == mw.PlayerPromotion && x.Status == MmaManager.Core.Model.EventStatus.Completed)
                        .OrderBy(x => x.Date).SelectMany(x => x.Card.Select(b => (x, b))).LastOrDefault(t => t.b.IsTitleFight && t.b.Outcome?.Winner == champNow.Id);
                    if (lastTitle.b?.Outcome?.Loser is { } lid && mw.Get(lid) is { } beaten)
                        Console.WriteLine($"RENDERMOMENT tie: {MmaManager.Core.Sim.Flavor.Ties.Strongest(mw, champNow, beaten, lastTitle.x.Date)?.Kind.ToString() ?? "none"}; "
                            + $"world-less line would be {MmaManager.Core.Sim.Flavor.Quotes.Say(champNow, MmaManager.Core.Sim.Flavor.Moment.TitleWin, lastTitle.x.Date, beaten)}");
                }
                foreach (var f in m.Facts) Console.WriteLine($"RENDERMOMENT fact: {f.Label}: {f.Value} - {f.Detail}");

                var root = (FrameworkElement)win.Content;
                var size = new Size(W, H);
                void Layout()
                {
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                }
                Layout();
                var settle = System.Diagnostics.Stopwatch.StartNew();
                while (Portraits.PendingThumbs > 0 && settle.ElapsedMilliseconds < 15000)
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                Layout();
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args[^1];
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERMOMENT OK -> " + Path.GetFullPath(p));

                // Carrying on takes it down and leaves the desk free for the clock.
                vm.Moments.DismissCommand.Execute(null);
                Console.WriteLine($"RENDERMOMENT dismissed: showing {vm.Moments.IsShowing}");
            }
            catch (Exception ex) { Console.WriteLine("RENDERMOMENT FAIL: " + ex); Environment.Exit(1); }
            Environment.Exit(0);
            return;
        }

        // THE TABLE (the negotiation window) in one of its states:
        // `MmaManager.Wpf --rendertable <none|talked|endofdeal|broke|freeagent|letgo|lodged|cameback|notenough|promises|wary> [--afc] <path>`.
        // promises: an AFC contender who wants a belt, re-signing, with a title shot and a matching right
        // on the slip (then his number is met and signed, and the signing line printed); wary: the same
        // with a promise to someone else broken in public a month ago.
        // A newly founded shoestring promotion (seed 7) unless --afc (the quick-start AFC career).
        // Rival offers are planted by hand (harness only): the biggest other promotion puts
        // paper in front of the top free agent (freeagent, lodged, cameback, notenough) or the
        // player's best-known fighter (endofdeal). lodged puts his own number in writing through
        // the real PutInWriting; notenough writes a lowball and shows the refusal; talked tables
        // one offer under his number with no rival, so the reactions and transcript show. broke is
        // endofdeal with the bank overdrawn (Match it is off, and says why); letgo is a free
        // agent's offer already let go, the week not yet turned.
        if (e.Args.Contains("--rendertable"))
        {
            try
            {
                int at = Array.IndexOf(e.Args, "--rendertable");
                string scenario = e.Args[at + 1];
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                // (promises / wary sit down with an AFC contender: a promotion with belts to promise a shot at.)
                bool promising = scenario is "promises" or "wary";
                if (!e.Args.Contains("--afc") && !promising)
                    vm.StartWorld(new WorldBuilder.Options
                    {
                        Seed = 7,
                        Founding = new WorldBuilder.FoundingSpec
                        {
                            Name = "New Wave Fighting", Abbreviation = "NWF", Region = MmaManager.Core.Model.Region.Europe,
                        },
                    }, "You founded New Wave Fighting.");
                var w = vm.LiveWorld!;
                var mine = w.Get(w.PlayerPromotion)!;
                var valuation = new MmaManager.Core.Sim.Contracts.FighterValuation();

                if (promising)
                {
                    // Re-signing talks with a contender (ranked #2-#4 in a division with a reigning
                    // champion) who wants a belt most, by his own priorities.
                    var who2 = mine.Divisions.Where(d => d.Title is { } t && w.Get(t)?.CurrentChampion is not null)
                        .SelectMany(d => d.Rankings.Skip(1).Take(3))
                        .Select(id => w.Get(id)).OfType<MmaManager.Core.Model.Fighter>()
                        .Where(f => f.Contract?.Promotion == mine.Id && f.Status == MmaManager.Core.Model.FighterStatus.Active)
                        .OrderByDescending(f => valuation.Evaluate(w, f).Priorities.TitleOpportunity).ThenBy(f => f.Id.Value).First();
                    if (scenario == "wary")
                    {
                        // Harness only: a promise to someone else broken a month ago, in public.
                        var other = w.RosterOf(mine.Id).First(f => f.Id != who2.Id && f.Status == MmaManager.Core.Model.FighterStatus.Active);
                        w.Promises.Add(new MmaManager.Core.Model.Promise
                        {
                            Id = w.NewPromiseId(), Fighter = other.Id, Promotion = mine.Id, Kind = MmaManager.Core.Model.PromiseKind.Activity, Fights = 3,
                            Made = w.CurrentDate.AddDays(-400), Deadline = w.CurrentDate.AddDays(-90),
                            State = MmaManager.Core.Model.PromiseState.Broken, Resolved = w.CurrentDate.AddDays(-30), Note = "the deadline passed",
                        });
                    }
                    var pExp = valuation.Evaluate(w, who2);
                    var pSession = MmaManager.Core.Sim.Contracts.NegotiationSession.Resume(w, who2, mine, pExp);
                    var pVm = new NegotiationViewModel(w, who2, mine, pExp, pSession, renewal: true);
                    string before = pVm.HisPurse;
                    string statedTitle = pVm.TitlePromise.Effect;
                    pVm.TitlePromise.IsOn = true;
                    string withShot = pVm.HisPurse;
                    string statedRight = pVm.MatchingRightEffect;
                    pVm.OfferMatchingRight = true;
                    // Check: the stated discount and cost are exactly how far his number moves.
                    decimal Amt(string s) => decimal.Parse(s.TrimStart('$').Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture);
                    Console.WriteLine($"RENDERTABLE   check: title stated '{statedTitle}', moved {Amt(before) - Amt(withShot):N0}; right stated '{statedRight}', moved {Amt(pVm.HisPurse) - Amt(withShot):N0}");
                    Console.WriteLine($"RENDERTABLE {scenario}: {who2.FullName} (title priority {pExp.Priorities.TitleOpportunity}); his purse {before} -> {withShot} with a title shot -> {pVm.HisPurse} with a matching right too");
                    Console.WriteLine($"RENDERTABLE   title: {pVm.TitlePromise.Allowed} | {pVm.TitlePromise.Note} | {pVm.TitlePromise.Effect}");
                    Console.WriteLine($"RENDERTABLE   home:  {pVm.HomePromise.Allowed} | {pVm.HomePromise.Note} | {pVm.HomePromise.Effect}");
                    Console.WriteLine($"RENDERTABLE   fights:{pVm.ActivityPromise.Allowed} | {pVm.ActivityPromise.Note} | {pVm.ActivityPromise.Effect} | picks {string.Join(",", pVm.ActivityPromise.Picks.Select(pk => $"{pk.Fights}{(pk.IsChecked ? "*" : "")}{(pk.Allowed ? "" : "x")}"))}");
                    Console.WriteLine($"RENDERTABLE   right: {pVm.MatchingRightEffect} | held '{pVm.MatchingRightHeld}' | wary '{pVm.WaryLine}' | stance '{pVm.Stance}'");
                    RenderRoom(pVm, who2.FullName);
                    if (scenario == "promises")
                    {
                        // Meet his number with the promise on the slip, and sign it as the Contracts desk does
                        // (a fresh view model: the drawn window still listens to the first one).
                        var sVm = new NegotiationViewModel(w, who2, mine, pExp,
                            MmaManager.Core.Sim.Contracts.NegotiationSession.Resume(w, who2, mine, pExp), renewal: true);
                        sVm.TitlePromise.IsOn = true;
                        sVm.OfferMatchingRight = true;
                        sVm.MeetTermsCommand.Execute(null);
                        if (sVm.SignedOffer is { } deal)
                        {
                            MmaManager.Core.Sim.Contracts.ContractLifecycle.Sign(w, who2, deal, w.CurrentDate);
                            Console.WriteLine($"RENDERTABLE   signed: '{ContractsViewModel.PromisedAtSigning(w, who2, deal).Trim()}'");
                        }
                        else Console.WriteLine($"RENDERTABLE   not signed: {sVm.HisLine}");
                    }
                    Environment.Exit(0);
                    return;
                }

                // The window drawn off-screen at its own size, written to the last argument.
                void RenderRoom(NegotiationViewModel rvm, string name)
                {
                    var rw = new NegotiationWindow { DataContext = rvm };
                    var rr = (FrameworkElement)rw.Content;
                    rw.Content = null;
                    var rs = new Size(rw.Width, rw.Height);
                    var rh = new System.Windows.Controls.Border { Background = (Brush)Current.Resources["Bg"] ?? Brushes.Black, Child = rr, DataContext = rvm };
                    rh.Measure(rs); rh.Arrange(new Rect(rs)); rh.UpdateLayout();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                    rh.Measure(rs); rh.Arrange(new Rect(rs)); rh.UpdateLayout();
                    var rb = new RenderTargetBitmap((int)rs.Width, (int)rs.Height, 96, 96, PixelFormats.Pbgra32);
                    rb.Render(rh);
                    var re = new PngBitmapEncoder(); re.Frames.Add(BitmapFrame.Create(rb));
                    using (var fs = File.Create(e.Args[^1])) re.Save(fs);
                    Console.WriteLine($"RENDERTABLE {scenario} OK -> {Path.GetFullPath(e.Args[^1])}: {name}");
                    rw.Close();
                }

                bool endOfDeal = scenario is "endofdeal" or "broke";
                var freeAgents = w.Fighters.Values.Where(f => f.Status == MmaManager.Core.Model.FighterStatus.Active && f.Contract is null && !f.LocalTalent)
                    .OrderByDescending(f => f.Intangibles.Popularity.Value).ThenBy(f => f.Id.Value).ToList();
                var who = endOfDeal
                    ? w.RosterOf(mine.Id).Where(f => f.Status == MmaManager.Core.Model.FighterStatus.Active && f.Contract is not null && !f.LocalTalent)
                        .OrderByDescending(f => f.Intangibles.Popularity.Value).ThenBy(f => f.Id.Value).FirstOrDefault()
                    : freeAgents[0];
                if (who is null)
                {
                    // A day-one shoestring roster is all unknowns: sign a known free agent at his
                    // number first (harness only), so there is someone worth an offer.
                    who = freeAgents[1];
                    var signExp = valuation.Evaluate(w, who);
                    var terms = new MmaManager.Core.Sim.Contracts.NegotiationSession(w, who, mine, signExp)
                        .TheirTermsAsOffer(MmaManager.Core.Sim.Contracts.Negotiator.WantsExclusivity(mine), clause: false);
                    MmaManager.Core.Sim.Contracts.ContractLifecycle.Sign(w, who, terms, w.CurrentDate);
                }
                var exp = valuation.Evaluate(w, who);

                if (scenario is "endofdeal" or "broke" or "freeagent" or "letgo" or "lodged" or "cameback" or "notenough")
                {
                    var rival = w.Promotions.Values.Where(p => p.Id != mine.Id && !p.Defunct)
                        .OrderByDescending(p => p.Prestige).ThenBy(p => p.Id.Value).First();
                    decimal purse = Math.Ceiling(exp.AskingPurse.Amount * 1.12m / 500m) * 500m;
                    var offer = new MmaManager.Core.Model.RivalOffer
                    {
                        Id = w.RivalOffers.Count == 0 ? 1 : w.RivalOffers.Max(o => o.Id) + 1,
                        Fighter = who.Id, From = rival.Id, Holder = mine.Id,
                        Kind = endOfDeal ? MmaManager.Core.Model.RivalOfferKind.EndOfDeal : MmaManager.Core.Model.RivalOfferKind.FreeAgent,
                        Purse = new MmaManager.Core.Model.Money(purse),
                        WinBonus = new MmaManager.Core.Model.Money(Math.Ceiling(purse * 0.5m / 500m) * 500m),
                        SigningBonus = new MmaManager.Core.Model.Money(endOfDeal ? 0m : Math.Ceiling(purse * 0.25m / 500m) * 500m),
                        Fights = exp.DesiredFights, Exclusive = true,
                        Made = w.CurrentDate.AddDays(-3), Deadline = w.CurrentDate.AddDays(endOfDeal ? 18 : 9),
                    };
                    if (scenario == "cameback")
                    {
                        offer.Purse = new MmaManager.Core.Model.Money(purse + 2_000m);
                        offer.Improvements = 1;
                        offer.Deadline = w.CurrentDate.AddDays(7);
                    }
                    w.RivalOffers.Add(offer);
                    if (scenario == "letgo") MmaManager.Core.Sim.Contracts.OfferMarket.LetGo(w, offer, w.CurrentDate);
                    if (scenario == "broke") mine.Balance = new MmaManager.Core.Model.Money(-50_000m);
                }

                var session = MmaManager.Core.Sim.Contracts.NegotiationSession.Resume(w, who, mine, exp);
                var roomVm = new NegotiationViewModel(w, who, mine, exp, session, renewal: who.Contract?.Promotion == mine.Id);
                if (scenario is "lodged" or "notenough" or "talked")
                {
                    var his = session.View(roomVm.OfferExclusive, false).Position;
                    decimal k = scenario == "lodged" ? 1m : 0.8m;
                    roomVm.OfferPurse = Math.Ceiling(his.PursePerFight.Amount * k / 500m) * 500m;
                    roomVm.OfferWinBonus = his.WinBonus.Amount;
                    roomVm.OfferSigningBonus = his.SigningBonus.Amount;
                    roomVm.OfferFights = his.Fights;
                    if (scenario == "talked") roomVm.TableOfferCommand.Execute(null);
                    else roomVm.PutInWritingCommand.Execute(null);
                }

                var roomWin = new NegotiationWindow { DataContext = roomVm };
                var roomRoot = (FrameworkElement)roomWin.Content;
                roomWin.Content = null;
                var roomSize = new Size(roomWin.Width, roomWin.Height);
                var host = new System.Windows.Controls.Border
                {
                    Background = (Brush)Current.Resources["Bg"] ?? Brushes.Black,
                    Child = roomRoot, DataContext = roomVm,
                };
                host.Measure(roomSize); host.Arrange(new Rect(roomSize)); host.UpdateLayout();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                host.Measure(roomSize); host.Arrange(new Rect(roomSize)); host.UpdateLayout();
                var bmp = new RenderTargetBitmap((int)roomSize.Width, (int)roomSize.Height, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(host);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                string p = e.Args[^1];
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine($"RENDERTABLE {scenario} OK -> {Path.GetFullPath(p)}: {who.FullName} ({mine.Abbreviation}); "
                                  + $"his purse {roomVm.HisPurse}; rival {(roomVm.HasRival ? roomVm.RivalAbbr + " " + roomVm.RivalPurse : "none")}; "
                                  + $"leaning {roomVm.LeaningWords}; status '{roomVm.Status}'; final '{roomVm.FinalLine}'");
                roomWin.Close();
            }
            catch (Exception ex) { Console.WriteLine("RENDERTABLE FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // PROMISES around the app: `MmaManager.Wpf --renderpromises <profile|briefing|home|planner> W H <path>`.
        // The quick-start AFC career with promises planted by hand (harness only): one of your
        // fighters was promised 3 fights in a year, due in 45 days with nothing booked (at risk),
        // and had a fight at home kept for him last year; another was promised a title shot whose
        // deadline is long gone. Then the real advance: week by week until the monthly test breaks
        // the old one. profile = his profile; home = Home's "Needs you"; planner = the Planner;
        // briefing = the briefing after the first week (the promise headlined) and, written beside
        // it as <path>-broken.png, the briefing the week the other promise broke. Every promise
        // letter in the inbox is printed with its buttons.
        if (e.Args.Contains("--renderpromises"))
        {
            try
            {
                int at = Array.IndexOf(e.Args, "--renderpromises");
                string scenario = e.Args[at + 1];
                int W = int.Parse(e.Args[at + 2]), H = int.Parse(e.Args[at + 3]);
                string p = e.Args[^1];
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                vm.BriefingOnStop = true;
                var root = (FrameworkElement)win.Content;
                var size = new Size(W, H);
                void Layout()
                {
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                }
                void Shot(string path)
                {
                    Layout();
                    var settle = System.Diagnostics.Stopwatch.StartNew();
                    while (Portraits.PendingThumbs > 0 && settle.ElapsedMilliseconds < 15000)
                    {
                        System.Threading.Thread.Sleep(2);
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                    }
                    Layout();
                    var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(root);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (var fs = File.Create(path)) enc.Save(fs);
                    Console.WriteLine("RENDERPROMISES OK -> " + Path.GetFullPath(path));
                }
                Layout();
                var w = vm.LiveWorld!;
                var mine = w.Get(w.PlayerPromotion)!;
                var today = w.CurrentDate;
                bool Booked(MmaManager.Core.Model.Fighter f) => w.Events.Values.Any(ev => ev.Status == MmaManager.Core.Model.EventStatus.Scheduled
                    && ev.Card.Any(x => x.RedCorner == f.Id || x.BlueCorner == f.Id));
                bool Champ(MmaManager.Core.Model.Fighter f) => w.Titles.Values.Any(t => t.CurrentChampion == f.Id);
                var pool = w.RosterOf(mine.Id).Where(f => f.Status == MmaManager.Core.Model.FighterStatus.Active && f.Contract is { FightsRemaining: >= 3 }
                                                          && !Booked(f) && !Champ(f) && !f.LocalTalent)
                    .OrderByDescending(f => f.Intangibles.Popularity.Value).ThenBy(f => f.Id.Value).ToList();
                var x = pool[0];
                var y = pool.Skip(1).First(f => mine.Divisions.Any(d => d.Matches(f) && d.Title is not null && !d.Rankings.Take(10).Contains(f.Id)));
                var yTitle = mine.Divisions.First(d => d.Matches(y)).Title;
                w.Promises.Add(new MmaManager.Core.Model.Promise
                {
                    Id = w.NewPromiseId(), Fighter = x.Id, Promotion = mine.Id, Kind = MmaManager.Core.Model.PromiseKind.HomeShow,
                    Made = today.AddDays(-700), Deadline = today.AddDays(-335),
                    State = MmaManager.Core.Model.PromiseState.Kept, Resolved = today.AddDays(-400), Note = "kept",
                });
                w.Promises.Add(new MmaManager.Core.Model.Promise
                {
                    Id = w.NewPromiseId(), Fighter = x.Id, Promotion = mine.Id, Kind = MmaManager.Core.Model.PromiseKind.Activity, Fights = 3,
                    Made = today.AddDays(-320), Deadline = today.AddDays(45),
                });
                // The one that breaks: its deadline just passed with nothing booked (it breaks when the month turns).
                MmaManager.Core.Model.Promise? broken = null;
                void PlantBreak()
                {
                    broken = new MmaManager.Core.Model.Promise
                    {
                        Id = w.NewPromiseId(), Fighter = y.Id, Promotion = mine.Id, Kind = MmaManager.Core.Model.PromiseKind.TitleShot, Title = yTitle,
                        Made = w.CurrentDate.AddDays(-370), Deadline = w.CurrentDate.AddDays(-5), Warned = true,
                    };
                    w.Promises.Add(broken);
                }
                if (scenario != "briefing") PlantBreak();
                Console.WriteLine($"RENDERPROMISES at risk: {x.FullName}; to break: {y.FullName}; {today:d MMM yyyy}");
                vm.Contracts.OnChanged?.Invoke();   // the app-wide refresh

                void Week()
                {
                    vm.AdvanceWeekCommand.Execute(null);
                    if (vm.IsFightNight) vm.SimTonightCommand.Execute(null);
                    vm.Moments.Reset();
                    Console.WriteLine($"RENDERPROMISES week to {w.CurrentDate:d MMM}: briefing {vm.Briefing.Kind} | {vm.Briefing.Headline} | {y.FullName}'s promise {broken?.State.ToString() ?? "not made yet"}");
                }
                void Letters()
                {
                    vm.Mail.Attach(w);
                    foreach (var l in vm.Mail.Letters.Where(l => l.FighterId == x.Id.Value || l.FighterId == y.Id.Value))
                        Console.WriteLine($"RENDERPROMISES letter '{l.Subject}': [{string.Join(" | ", l.Actions.Select(a => (a.IsPrimary ? "*" : "") + a.Text))}] {l.ActionNote}");
                }

                switch (scenario)
                {
                    case "briefing":
                        Week();
                        vm.Desktop.Open("briefing");
                        Console.WriteLine($"RENDERPROMISES briefing: {vm.Briefing.Kicker} | {vm.Briefing.Headline} | {vm.Briefing.Lede}");
                        Console.WriteLine($"RENDERPROMISES actions: {string.Join(" | ", vm.Briefing.Actions.Select(a => (a.IsPrimary ? "*" : "") + a.Text))}");
                        Shot(p);
                        PlantBreak();
                        for (int i = 0; i < 6 && broken!.State == MmaManager.Core.Model.PromiseState.Open; i++) Week();
                        vm.Desktop.Open("briefing");
                        Console.WriteLine($"RENDERPROMISES digest: {string.Join(" / ", vm.Briefing.DigestShown.Select(i => $"[{i.Kicker}] {i.Text} — {i.Detail}"))}");
                        Console.WriteLine($"RENDERPROMISES waiting: {string.Join(" / ", vm.Briefing.Waiting.Select(i => $"{i.Text} ({i.Detail}) {i.ActionText}"))}");
                        Shot(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(p))!, Path.GetFileNameWithoutExtension(p) + "-broken.png"));
                        Letters();
                        break;
                    case "profile":
                    case "home":
                    case "planner":
                        // profile: stop the week before the old promise breaks (both open, the old one overdue);
                        // home / planner: a couple of weeks on.
                        if (scenario != "profile") { Week(); Week(); }
                        foreach (var d in vm.Desktop.Windows.ToList()) vm.Desktop.Close(d);
                        if (scenario == "profile") vm.OpenFighter(x.Id.Value);
                        else vm.Desktop.Open(scenario);
                        Shot(p);
                        Letters();
                        if (scenario == "profile")
                        {
                            // "Book him": with no card, the Planner and why; with a card planned, the card desk with
                            // him in the first corner (drawn beside the profile as <path>-bookhim.png).
                            AppCommandsBook(x.Id.Value);
                            Console.WriteLine($"RENDERPROMISES book him (no card) -> windows [{string.Join(", ", vm.Desktop.Windows.Select(d => d.Spec.Id))}]; strip: {vm.StatusText}");
                            vm.Booking.NewEventDate = w.CurrentDate.ToDateTime(TimeOnly.MinValue).AddDays(24);
                            vm.Booking.NewEventCommand.Execute(null);
                            vm.Contracts.OnChanged?.Invoke();
                            foreach (var d in vm.Desktop.Windows.ToList()) vm.Desktop.Close(d);
                            AppCommandsBook(x.Id.Value);
                            Console.WriteLine($"RENDERPROMISES book him -> windows [{string.Join(", ", vm.Desktop.Windows.Select(d => d.Spec.Id))}], red corner {vm.Booking.SelectedRed?.Id.Value.ToString() ?? "none"}, card {vm.Booking.SelectedEvent?.Model.Name ?? "none"}; strip: {vm.StatusText}");
                            Shot(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(p))!, Path.GetFileNameWithoutExtension(p) + "-bookhim.png"));
                        }
                        break;
                    case "foughtout":
                    {
                        // His deal is fought out (harness only: no fights left) with the promise still live:
                        // a week on, his re-sign window is open. Home shows "Talk terms" (and why), the
                        // profile too (<path>-profile.png); "Book him" from anywhere opens the table instead.
                        x.Contract!.FightsRemaining = 0;
                        Week();
                        foreach (var d in vm.Desktop.Windows.ToList()) vm.Desktop.Close(d);
                        vm.Desktop.Open("home");
                        var row = vm.Dashboard.NeedsYou.FirstOrDefault(a => a.Title.StartsWith($"You promised {x.FirstName} {x.LastName}", StringComparison.Ordinal));
                        Console.WriteLine($"RENDERPROMISES home row: {row?.Title} | {row?.Detail} | [{row?.PrimaryText} | {row?.SecondaryText}]");
                        Shot(p);
                        foreach (var d in vm.Desktop.Windows.ToList()) vm.Desktop.Close(d);
                        vm.OpenFighter(x.Id.Value);
                        Shot(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(p))!, Path.GetFileNameWithoutExtension(p) + "-profile.png"));
                        Letters();
                        AppCommandsBook(x.Id.Value);
                        Console.WriteLine($"RENDERPROMISES book him (fought out) -> windows [{string.Join(", ", vm.Desktop.Windows.Select(d => d.Spec.Id))}]; strip: {vm.StatusText}");
                        break;
                    }
                    default:
                        throw new ArgumentException("unknown scenario " + scenario);
                }
                static void AppCommandsBook(int id) => MmaManager.Wpf.Mvvm.AppCommands.BookFighter.Execute(id);
            }
            catch (Exception ex) { Console.WriteLine("RENDERPROMISES FAIL: " + ex); Environment.Exit(1); }
            Environment.Exit(0);
            return;
        }

        // THE BRIEFING over the desk after the clock stops: `MmaManager.Wpf --renderbriefing
        // <scenario> <W> <H> <path>`. Scenarios, each reached through the real advance:
        //   hole       a card of yours five weeks out loses an undercard bout (a booked fighter
        //              is hurt past the date; the weekly audit pulls the bout and mails you);
        //   main       the same, but the main event: the advance itself stops for it;
        //   decision   months go by until a call lands on The Desk;
        //   expiring   weeks go by until a deal on its last fight is headlined (if none comes,
        //              one fighter's deal is set to its last fight first);
        //   quiet      weeks go by until nothing new needs you (the plain weekly digest);
        //   fightnight a card of yours is booked five days out and the week runs into it;
        //   card       that card is then simmed (any Moment is carried on from first);
        //   freeagentoffer  a founded promotion courts the free agents; a rival's offer to one of
        //              them reaches its last week and stops the clock (sit down / let go);
        //   desk      nothing advanced: the briefing opened by hand on day one.
        if (e.Args.Contains("--renderbriefing"))
        {
            try
            {
                int at = Array.IndexOf(e.Args, "--renderbriefing");
                string scenario = e.Args[at + 1];
                int W = int.Parse(e.Args[at + 2]), H = int.Parse(e.Args[at + 3]);
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                vm.BriefingOnStop = true;
                var root = (FrameworkElement)win.Content;
                var size = new Size(W, H);
                void Layout()
                {
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                }
                Layout();   // the desk learns its size, so the briefing can centre on it
                var w = vm.LiveWorld!;

                // Book a card of yours `days` out the way a player does: one bout per division, top two.
                MmaManager.Core.Model.Event BookCard(int days)
                {
                    var bk = vm.Booking;
                    bk.NewEventDate = w.CurrentDate.ToDateTime(TimeOnly.MinValue).AddDays(days);
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
                    vm.Contracts.OnChanged?.Invoke();
                    var ev = w.Events.Values.Where(x => x.Promotion == w.PlayerPromotion && x.Status == MmaManager.Core.Model.EventStatus.Scheduled)
                        .OrderByDescending(x => x.Id.Value).First();
                    Console.WriteLine($"RENDERBRIEFING booked {ev.Name} on {ev.Date:d MMM yyyy}: {ev.Card.Count} bouts");
                    return ev;
                }
                // Hurt one corner of a booked bout past the card's date (harness only).
                void Hurt(MmaManager.Core.Model.Event ev, MmaManager.Core.Model.Fight bout)
                {
                    var f = w.Get(bout.RedCorner)!;
                    var back = ev.Date.AddDays(40);
                    f.CurrentInjury = new MmaManager.Core.Model.Injury
                    {
                        Severity = MmaManager.Core.Model.InjurySeverity.Moderate, Description = "broken hand",
                        OccurredOn = w.CurrentDate, ExpectedReturn = back,
                    };
                    f.Status = MmaManager.Core.Model.FighterStatus.Injured;
                    f.ReadyToFightOn = back;
                    Console.WriteLine($"RENDERBRIEFING hurt {f.FullName} (booked on {ev.Name})");
                }
                void Report(string what) =>
                    Console.WriteLine($"RENDERBRIEFING {what}: {vm.Briefing.Kind} | {vm.Briefing.Kicker} | {vm.Briefing.Headline} | {w.CurrentDate:d MMM yyyy}");

                switch (scenario)
                {
                    case "hole":
                    case "main":
                    {
                        var ev = BookCard(35);
                        if (ev.Card.Count < 2) throw new InvalidOperationException("the card needs two bouts");
                        Hurt(ev, scenario == "main" ? ev.Card[^1] : ev.Card[0]);
                        vm.AdvanceWeekCommand.Execute(null);
                        Report("after the week");
                        break;
                    }
                    case "decision":
                        for (int i = 0; i < 30 && vm.Briefing.Kind != BriefingKind.Decision; i++)
                        {
                            vm.AdvanceMonthCommand.Execute(null);
                            if (vm.IsFightNight) vm.SimTonightCommand.Execute(null);
                            vm.Moments.Reset();
                            Report($"month {i + 1}");
                        }
                        break;
                    case "expiring":
                        for (int i = 0; i < 6 && vm.Briefing.Kind != BriefingKind.ExpiringDeal; i++)
                        {
                            if (i == 3 && w.RosterOf(w.PlayerPromotion).OrderBy(f => f.Id.Value)
                                    .FirstOrDefault(f => f.Contract is { FightsRemaining: > 1 }) is { } pick)
                            {
                                pick.Contract!.FightsRemaining = 1;
                                Console.WriteLine($"RENDERBRIEFING set {pick.FullName}'s deal to its last fight");
                            }
                            vm.AdvanceWeekCommand.Execute(null);
                            Report($"week {i + 1}");
                        }
                        break;
                    case "quiet":
                        for (int i = 0; i < 40 && vm.Briefing.Kind != BriefingKind.Quiet; i++)
                        {
                            vm.AdvanceWeekCommand.Execute(null);
                            if (vm.IsFightNight) vm.SimTonightCommand.Execute(null);
                            vm.Moments.Reset();
                            Report($"week {i + 1}");
                        }
                        break;
                    case "fightnight":
                    case "card":
                    {
                        BookCard(5);
                        vm.AdvanceWeekCommand.Execute(null);
                        Report("after the week");
                        if (scenario == "card")
                        {
                            vm.SimTonightCommand.Execute(null);
                            if (vm.Moments.IsShowing)
                            {
                                Console.WriteLine($"RENDERBRIEFING a moment came first: {vm.Moments.Current?.Headline} {vm.Moments.Current?.Name}; carrying on");
                                vm.Moments.DismissCommand.Execute(null);
                            }
                            Report("after the card");
                        }
                        break;
                    }
                    case "window":
                    case "lastweek":
                    {
                        // Harness only: your best-known champion fights out his deal (no fights left).
                        // "window" lets the weekly pass open his 30-day re-sign window; "lastweek"
                        // starts him twelve days from its end, so the week lands him in its last week.
                        var champ = w.Titles.Values.Where(t => t.Promotion == w.PlayerPromotion && t.CurrentChampion is not null)
                            .Select(t => w.Get(t.CurrentChampion!.Value)).OfType<MmaManager.Core.Model.Fighter>()
                            .Where(f => f.Contract?.Promotion == w.PlayerPromotion)
                            .OrderByDescending(f => f.Intangibles.Popularity.Value).ThenBy(f => f.Id.Value).First();
                        champ.Contract!.FightsRemaining = 0;
                        if (scenario == "lastweek")
                        {
                            champ.Contract.WindowEndsOn = w.CurrentDate.AddDays(12);
                            champ.Contract.WindowWarned = false;
                        }
                        Console.WriteLine($"RENDERBRIEFING {champ.FullName}'s deal is fought out ({scenario})");
                        vm.AdvanceWeekCommand.Execute(null);
                        Report("after the week");
                        break;
                    }
                    case "rivaloffer":
                    {
                        // A founded shoestring promotion (every other org is bigger) signs the best free
                        // agents by the scout's read, its deals are set to their last fight (harness only),
                        // and the most popular is made the face of the company. Weeks then go by through the
                        // real advance until a rival's offer for a top-billed fighter stops the clock.
                        vm.StartWorld(new WorldBuilder.Options
                        {
                            Seed = 7,
                            Founding = new WorldBuilder.FoundingSpec { Name = "New Wave Fighting", Abbreviation = "NWF", Region = MmaManager.Core.Model.Region.Europe },
                        }, "You founded New Wave Fighting.");
                        w = vm.LiveWorld!;
                        var fw = w;
                        foreach (var row in vm.Contracts.FreeAgents.OrderByDescending(r => FighterRow.Scouted(fw, fw.Get(r.Id)!)).ThenBy(r => r.Id.Value).Take(8).ToList())
                        {
                            if (vm.Contracts.FreeAgents.FirstOrDefault(r => r.Id == row.Id) is not { } fresh) continue;
                            vm.Contracts.SelectedFreeAgent = fresh;
                            vm.Contracts.SignAtTheirTerms();
                        }
                        var mine = w.RosterOf(w.PlayerPromotion).Where(f => f.Contract is not null).ToList();
                        foreach (var f in mine) f.Contract!.FightsRemaining = 1;
                        var face = mine.OrderByDescending(f => f.Intangibles.Popularity.Value).ThenBy(f => f.Id.Value).First();
                        MmaManager.Core.Sim.Org.Franchise.SetFace(w, w.Get(w.PlayerPromotion)!, face, w.CurrentDate);
                        Console.WriteLine($"RENDERBRIEFING signed {mine.Count}, deals on their last fight; face of the company: {face.FullName}");
                        vm.Contracts.OnChanged?.Invoke();
                        for (int i = 0; i < 60 && vm.Briefing.Kind != BriefingKind.RivalOffer; i++)
                        {
                            vm.AdvanceWeekCommand.Execute(null);
                            if (vm.IsFightNight) vm.SimTonightCommand.Execute(null);
                            vm.Moments.Reset();
                        }
                        Report("after the weeks");
                        if (vm.Briefing.Kind != BriefingKind.RivalOffer)
                            throw new InvalidOperationException($"no rival offer stopped the clock ({w.RivalOffers.Count} offers made)");
                        break;
                    }
                    case "freeagentoffer":
                    {
                        // A founded shoestring promotion has open talks with every free agent on the
                        // market (harness only: the talks are written straight into World.Talks and kept
                        // fresh each week). Weeks go by through the real advance until a rival that would
                        // have signed one lodges an offer instead and its last week stops the clock.
                        vm.StartWorld(new WorldBuilder.Options
                        {
                            Seed = 7,
                            Founding = new WorldBuilder.FoundingSpec { Name = "New Wave Fighting", Abbreviation = "NWF", Region = MmaManager.Core.Model.Region.Europe },
                        }, "You founded New Wave Fighting.");
                        w = vm.LiveWorld!;
                        void Court()
                        {
                            foreach (var fa in w.FreeAgents().Where(x => x.Status == MmaManager.Core.Model.FighterStatus.Active && !x.LocalTalent).OrderBy(x => x.Id.Value).ToList())
                            {
                                var t = w.Talks.FirstOrDefault(x => x.Fighter == fa.Id && x.Promotion == w.PlayerPromotion);
                                if (t is null) w.Talks.Add(new MmaManager.Core.Model.TalksMemory { Fighter = fa.Id, Promotion = w.PlayerPromotion, Round = 1, Mood = 60, Patience = 2, LastTalked = w.CurrentDate });
                                else if (t.SouredUntil is null) t.LastTalked = w.CurrentDate;
                            }
                        }
                        // (The week it lands it headlines; the stop comes in its last week.)
                        bool Stopped() => vm.Briefing.Kind == BriefingKind.RivalOffer
                                          && vm.Briefing.Kicker.StartsWith("TIME STOPPED", StringComparison.Ordinal)
                                          && DeskCalls.Courted(w).Any(o => o.Fighter.Value == vm.Briefing.StopFighterId);
                        for (int i = 0; i < 26 && !Stopped(); i++)
                        {
                            Court();
                            vm.AdvanceWeekCommand.Execute(null);
                            if (vm.IsFightNight) vm.SimTonightCommand.Execute(null);
                            vm.Moments.Reset();
                            Report($"week {i + 1} ({w.RivalOffers.Count(o => o.Kind == MmaManager.Core.Model.RivalOfferKind.FreeAgent)} free-agent offers)");
                        }
                        if (!Stopped())
                            throw new InvalidOperationException($"no free-agent offer stopped the clock ({w.RivalOffers.Count} offers made)");
                        break;
                    }
                    case "desk":
                        break;
                    case "offer":
                        // A sponsor and a network on the table; the desk leads with the most pressing call.
                        PlantOffers(w);
                        vm.Contracts.OnChanged?.Invoke();
                        break;
                    case "board":
                    {
                        // The board asks for a veteran off the books (harness plants the call the sim makes).
                        var vet = w.RosterOf(w.PlayerPromotion).Where(f => f.Status == MmaManager.Core.Model.FighterStatus.Active)
                            .OrderByDescending(f => f.AgeOn(w.CurrentDate)).ThenBy(f => f.Id.Value).First();
                        w.Decisions.Add(new MmaManager.Core.Model.Decision
                        {
                            Id = w.Decisions.Count == 0 ? 1 : w.Decisions.Max(d => d.Id) + 1,
                            Kind = MmaManager.Core.Model.DecisionKind.VeteranCut, Created = w.CurrentDate, Deadline = w.CurrentDate.AddDays(10),
                            Fighter = vet.Id,
                            Detail = $"The board wants {vet.FullName} off the books: an expensive name on a slide. Cut the deal and the payroll breathes; keep it and the board remembers.",
                        });
                        vm.Contracts.OnChanged?.Invoke();
                        Console.WriteLine($"RENDERBRIEFING planted a veteran-cut call on {vet.FullName}");
                        break;
                    }
                    default:
                        throw new ArgumentException("unknown scenario " + scenario);
                }
                vm.Desktop.Open("briefing");
                Layout();
                var settle = System.Diagnostics.Stopwatch.StartNew();
                while (Portraits.PendingThumbs > 0 && settle.ElapsedMilliseconds < 15000)
                {
                    System.Threading.Thread.Sleep(2);
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                }
                Layout();
                var b = vm.Briefing;
                Report("drawn");
                Console.WriteLine($"RENDERBRIEFING lede: {b.Lede}");
                Console.WriteLine($"RENDERBRIEFING people: {string.Join(", ", b.People.Select(p => $"{p.Name} ({p.Detail})"))}");
                Console.WriteLine($"RENDERBRIEFING actions: {string.Join(" | ", b.Actions.Select(a => (a.IsPrimary ? "*" : "") + a.Text))}");
                Console.WriteLine($"RENDERBRIEFING digest ({b.DigestWindow}): {string.Join(" / ", b.DigestShown.Select(i => i.Text))}{(b.HasMoreDigest ? " ..." : "")}");
                Console.WriteLine($"RENDERBRIEFING waiting: {string.Join(" / ", b.Waiting.Select(i => i.Text))}");
                Console.WriteLine($"RENDERBRIEFING next: {b.NextTitle} {b.NextCountdown} {b.NextCountdownUnit} | {b.NextDetail} | {b.NextDeadline}{b.NoCardLine}");
                Console.WriteLine($"RENDERBRIEFING strip: {vm.StatusText}");

                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args[^1];
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERBRIEFING OK -> " + Path.GetFullPath(p));

                if (scenario == "freeagentoffer")
                {
                    // Press the two doors: "Sit down" must land on free-agent talks (not a re-sign
                    // window), and "Let him go" must hand him to the rival when the week turns.
                    var courted = w.Get(new MmaManager.Core.Model.FighterId(b.StopFighterId))!;
                    b.Actions.First(a => a.Text.StartsWith("Sit down", StringComparison.Ordinal)).Command.Execute(null);
                    Console.WriteLine($"RENDERBRIEFING sit down -> windows [{string.Join(", ", vm.Desktop.Windows.Select(x => x.Spec.Id))}], target {vm.Contracts.TargetName} | {vm.Contracts.TargetSub} | own={vm.Contracts.IsOwnFighter}");
                    vm.Desktop.Open("briefing");
                    vm.Briefing.Actions.First(a => a.Text.StartsWith("Let ", StringComparison.Ordinal)).Command.Execute(null);
                    Layout();
                    Report("let go");
                    Console.WriteLine($"RENDERBRIEFING strip: {vm.StatusText}");
                    var rtb2 = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                    rtb2.Render(root);
                    var enc2 = new PngBitmapEncoder(); enc2.Frames.Add(BitmapFrame.Create(rtb2));
                    string p2 = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(p))!, Path.GetFileNameWithoutExtension(p) + "-letgo.png");
                    using (var fs2 = File.Create(p2)) enc2.Save(fs2);
                    Console.WriteLine("RENDERBRIEFING OK -> " + p2);
                    vm.AdvanceWeekCommand.Execute(null);
                    Console.WriteLine($"RENDERBRIEFING a week on: {courted.FullName} is with {(courted.Contract is { } cc ? w.Get(cc.Promotion)?.Abbreviation : "nobody")}");
                }
            }
            catch (Exception ex) { Console.WriteLine("RENDERBRIEFING FAIL: " + ex); Environment.Exit(1); }
            Environment.Exit(0);
            return;
        }

        // One app on the desktop at a real screen size, maximised or floating at its
        // default window size: `MmaManager.Wpf --renderapp <appId> <W> <H> [--founded]
        // [--floating] [--months N] [--card] [--cards N] [--table] <path>` (the Planner adds
        // --agenda, --scope <name>, --select <days|card|clash>, --injure). Big org (AFC) by default, a
        // month into the career; --founded = a newly founded shoestring promotion
        // on day one; --card books a next card first; --table flips the Roster to its Table view.
        if (e.Args.Contains("--renderapp"))
        {
            try
            {
                int at = Array.IndexOf(e.Args, "--renderapp");
                string appId = e.Args[at + 1];
                int W = int.Parse(e.Args[at + 2]), H = int.Parse(e.Args[at + 3]);
                int months = e.Args.Contains("--months") ? int.Parse(e.Args[Array.IndexOf(e.Args, "--months") + 1]) : 1;
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                if (e.Args.Contains("--founded"))
                {
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
                }
                else if (e.Args.Contains("--origins"))
                {
                    // An Origins career (1 January 1993): --origins <0 Crucible | 1 Shinken | 2 Duelo>,
                    // then --weeks N of play (each fight night of yours simmed as it comes, unless
                    // --holdnight leaves the last one waiting on the fight-night bar).
                    int house = int.Parse(e.Args[Array.IndexOf(e.Args, "--origins") + 1]);
                    vm.StartWorld(WorldBuilder.Options.Origins(1993, house), OriginsHarness.Summary(house));
                    // --months N plays N months first (years of the story); --reform adopts every reform HQ
                    // offers as it opens; --untilchapter N then plays on (at most N weeks) until a chapter fires.
                    int weeks = e.Args.Contains("--weeks") ? int.Parse(e.Args[Array.IndexOf(e.Args, "--weeks") + 1]) : 0;
                    int omonths = e.Args.Contains("--months") ? months : 0;
                    bool reform = e.Args.Contains("--reform") || e.Args.Contains("--rulesonly");
                    OriginsHarness.RulesOnly = e.Args.Contains("--rulesonly");
                    OriginsHarness.Play(vm, weeks, hold: e.Args.Contains("--holdnight"), bookNight: e.Args.Contains("--booknight"), months: omonths, reform: reform, busy: e.Args.Contains("--busy"), stopAtBan: e.Args.Contains("--stopatban"));
                    if (e.Args.Contains("--rankorg"))   // the Rankings app viewing another house (by abbreviation)
                    {
                        string abbr = e.Args[Array.IndexOf(e.Args, "--rankorg") + 1];
                        vm.Desktop.Open("rankings");
                        vm.SelectedOrg = vm.Organizations.FirstOrDefault(p => p.Abbreviation == abbr) ?? vm.SelectedOrg;
                        Console.WriteLine($"ORIGINS rankings of {vm.SelectedOrg?.Abbreviation}: picks [{string.Join(" | ", vm.RankingDivisions.Select(p => p.Label))}]; divisions [{string.Join(" | ", vm.SelectedOrg?.Divisions.Select(FighterRow.DivisionLabel) ?? Array.Empty<string>())}]");
                    }
                    if (e.Args.Contains("--plantcode"))
                    {
                        // Harness only: the commissions' one code arrives early. Your home region is licensed and the
                        // chapter is recorded; then a week of play lets the sim's own weekly pass bind your house to it.
                        var w = vm.LiveWorld!;
                        if (w.Get(w.PlayerPromotion) is { HomeCity: { } hc } && w.Get(hc) is { } city && w.Get(city.Country) is { } country
                            && !w.Era.Sanctions.Any(s => s.Country == country.Code && (s.Area is null || s.Area == city.Area)))
                            w.Era.Sanctions.Add(new MmaManager.Core.Model.Sanction { Country = country.Code, Area = city.Area, On = w.CurrentDate });
                        MmaManager.Core.Sim.Living.Era.Fire(w, MmaManager.Core.Sim.Living.OriginsReforms.Unified, w.CurrentDate);
                        vm.AdvanceWeekCommand.Execute(null);
                        var mine = w.Get(w.PlayerPromotion)!;
                        Console.WriteLine($"ORIGINS code planted: bound {MmaManager.Core.Sim.Living.OriginsReforms.BoundByCode(w, mine)}, under it {MmaManager.Core.Sim.Living.OriginsReforms.HasUnified(mine)}, "
                            + $"rounds {mine.Rules.StandardRounds}/{mine.Rules.TitleRounds} x {mine.Rules.RoundMinutes} min, rankings recognised {MmaManager.Core.Sim.Living.OriginsReforms.RankingsRecognised(w)}");
                    }
                    if (e.Args.Contains("--nochapters"))
                    {
                        // The Story's empty-era state (harness only: the world is the harness's own).
                        vm.LiveWorld!.Era.Chapters.Clear();
                        vm.Contracts.OnChanged?.Invoke();
                    }
                    if (e.Args.Contains("--untilchapter"))
                        OriginsHarness.UntilChapter(vm, int.Parse(e.Args[Array.IndexOf(e.Args, "--untilchapter") + 1]), reform, e.Args.Contains("--busy"));
                    if (e.Args.Contains("--replacecheck")) OriginsHarness.ReplaceCheck(vm);   // a band bout's corner changed through the desk
                    if (e.Args.Contains("--nightin"))   // look at a night N days out on the desk's form
                        vm.Tournament.Date = vm.LiveWorld!.CurrentDate.AddDays(int.Parse(e.Args[Array.IndexOf(e.Args, "--nightin") + 1])).ToDateTime(TimeOnly.MinValue);
                    if (e.Args.Contains("--nightin")) vm.Tournament.ShowNextNight = true;
                    if (e.Args.Contains("--bracketcase"))
                    {
                        // A planted hard-case bracket (walkovers, withdrawals, the alternate, overtime), shown on the desk.
                        var night = OriginsHarness.PlantBracket(vm.LiveWorld!, e.Args[Array.IndexOf(e.Args, "--bracketcase") + 1]);
                        vm.Contracts.OnChanged?.Invoke();
                        vm.Tournament.ShowNight(night.Id);
                    }
                }
                else
                    for (int i = 0; i < months; i++) vm.AdvanceMonthCommand.Execute(null);
                if (e.Args.Contains("--card"))
                {
                    // Book a next card the way a player would: one bout per division, top two.
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
                    vm.Contracts.OnChanged?.Invoke();
                }
                if (e.Args.Contains("--cards"))
                {
                    // Several cards of yours ahead (the Planner's state): N cards a month apart,
                    // three weeks out first, each filled the way --card fills one.
                    int n = int.Parse(e.Args[Array.IndexOf(e.Args, "--cards") + 1]);
                    var bk = vm.Booking;
                    var today = vm.LiveWorld!.CurrentDate.ToDateTime(TimeOnly.MinValue);
                    for (int k = 0; k < n; k++)
                    {
                        bk.NewEventDate = today.AddDays(19 + 30 * k);
                        bk.NewEventCommand.Execute(null);
                        int bouts = 0;
                        foreach (var div in bk.Divisions.ToList())
                        {
                            if (bouts >= 5 - k) break;      // later cards are thinner (still being built)
                            bk.SelectedDivision = div;
                            if (bk.RedOptions.Count > 0)
                            {
                                bk.SelectedRed = bk.RedOptions[0];
                                if (bk.BlueOptions.Count > 0) { bk.SelectedBlue = bk.BlueOptions[0]; bk.AddBoutCommand.Execute(null); bouts++; }
                            }
                        }
                    }
                    vm.Contracts.OnChanged?.Invoke();
                }
                Console.WriteLine($"RENDERAPP world ready in {clock.ElapsedMilliseconds} ms");
                if (e.Args.Contains("--offers")) { PlantOffers(vm.LiveWorld!); vm.Contracts.OnChanged?.Invoke(); }
                // --platform <keeps|misses|ask-misses|shutdown|shutdown-alone|accepted|shutdone>: the player on his own streaming platform with a
                // streaming giant's offer on the desk (his record keeping its terms, or missing them); "shutdown" opens the
                // shutdown confirmation, "shutdone" confirms it, "accepted" signs the offer (both as the desk's buttons do).
                string? platformMode = e.Args.Contains("--platform") ? e.Args[Array.IndexOf(e.Args, "--platform") + 1] : null;
                if (platformMode is not null) { PlantPlatformTrade(vm.LiveWorld!, platformMode.EndsWith("misses", StringComparison.Ordinal), alone: platformMode.EndsWith("alone", StringComparison.Ordinal)); vm.Contracts.OnChanged?.Invoke(); }
                // --hire <name>: hire that member of staff off the market the way the HQ screen does (exact name).
                if (e.Args.Contains("--hire"))
                {
                    var w = vm.LiveWorld!;
                    string who = e.Args[Array.IndexOf(e.Args, "--hire") + 1];
                    if (w.StaffMarket.FirstOrDefault(s => s.Name == who) is { } hire)
                    {
                        MmaManager.Core.Sim.Org.StaffService.Hire(w, w.Get(w.PlayerPromotion)!, hire);
                        vm.Contracts.OnChanged?.Invoke();
                    }
                    Console.WriteLine($"RENDERAPP hired {who}: {(w.Get(w.PlayerPromotion)!.Staff.Any(s => s.Name == who) ? "yes" : "not on the market")}");
                }
                // --boardcut: the board's veteran-cut call, answered yes through the sim's own Decisions.Accept,
                // which posts the owner's letter (a letter signed by a named owner, for the inbox render).
                if (e.Args.Contains("--boardcut"))
                {
                    var w = vm.LiveWorld!;
                    var vet = w.RosterOf(w.PlayerPromotion).Where(f => f.Status == MmaManager.Core.Model.FighterStatus.Active)
                        .OrderByDescending(f => f.AgeOn(w.CurrentDate)).ThenBy(f => f.Id.Value).First();
                    var call = new MmaManager.Core.Model.Decision
                    {
                        Id = w.Decisions.Count == 0 ? 1 : w.Decisions.Max(d => d.Id) + 1,
                        Kind = MmaManager.Core.Model.DecisionKind.VeteranCut, Created = w.CurrentDate, Deadline = w.CurrentDate.AddDays(10),
                        Fighter = vet.Id, Detail = "The board wants an expensive name off the books.",
                    };
                    w.Decisions.Add(call);
                    MmaManager.Core.Sim.Living.Decisions.Accept(w, call, w.CurrentDate);
                    vm.Contracts.OnChanged?.Invoke();
                    Console.WriteLine($"RENDERAPP board cut answered: {vet.FullName}");
                }
                if (e.Args.Contains("--table"))
                {
                    vm.MyRoster.ShowTable = true;
                    vm.FightersPage.ShowTable = true;
                    vm.Org.ShowStaffTable = true;
                    vm.Finance.ShowSponsorTable = true;
                    vm.Media.ShowDealTable = true;
                }
                if (e.Args.Contains("--bykind")) vm.News.ByKind = true;
                if (e.Args.Contains("--syncfaces")) Portraits.SyncThumbs = true;   // A/B: the old full-size, UI-thread decode
                var root = (FrameworkElement)win.Content;
                var size = new Size(W, H);
                void Layout()
                {
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                }
                // Card faces decode on worker threads; the still image waits for them.
                void WaitThumbs(string what)
                {
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    while (Portraits.PendingThumbs > 0 && t.ElapsedMilliseconds < 15000)
                    {
                        System.Threading.Thread.Sleep(2);
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                    }
                    Layout();
                    if (what.Length > 0) Console.WriteLine($"RENDERAPP {what}: every face in {t.ElapsedMilliseconds} ms more");
                }
                Layout();
                WaitThumbs("");
                clock.Restart();
                double cpu0 = UiCpuMs();
                vm.Desktop.Open(appId);
                // First frame: the window built, laid out and drawn once (what the player waits for
                // before the app is on screen and answers clicks). Faces decoding off-thread come after.
                double cpuOpen = UiCpuMs() - cpu0;
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                double cpuLayout = UiCpuMs() - cpu0 - cpuOpen;
                new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32).Render(root);
                Console.WriteLine($"RENDERAPP   UI-thread CPU split: open {cpuOpen:0} ms, layout {cpuLayout:0} ms, render {UiCpuMs() - cpu0 - cpuOpen - cpuLayout:0} ms");
                Console.WriteLine($"RENDERAPP open {appId}, first frame: {clock.ElapsedMilliseconds} ms wall, {UiCpuMs() - cpu0:0} ms UI-thread CPU");
                var settle = System.Diagnostics.Stopwatch.StartNew();
                while (Portraits.PendingThumbs > 0 && settle.ElapsedMilliseconds < 15000)
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                Layout();
                Console.WriteLine($"RENDERAPP open {appId} + first layout: {clock.ElapsedMilliseconds} ms wall, {UiCpuMs() - cpu0:0} ms UI-thread CPU (every face in and laid out)");
                if (e.Args.Contains("--floating") && vm.Desktop.Windows.FirstOrDefault(w => w.Spec.Id == appId) is { } app)
                {
                    app.IsMaximized = false;
                    app.X = 46; app.Y = 26; app.W = app.Spec.W; app.H = app.Spec.H;
                    vm.Desktop.FitToArea(app);
                    Console.WriteLine($"RENDERAPP floating window {app.W:0}x{app.H:0} on a {vm.Desktop.Area.Width:0}x{vm.Desktop.Area.Height:0} desk");
                    Layout();
                }

                // Cost of a full screen refresh with this app open (what an advance pays).
                clock.Restart();
                vm.Contracts.OnChanged?.Invoke();
                Layout();
                Console.WriteLine($"RENDERAPP refresh + relayout: {clock.ElapsedMilliseconds} ms");
                clock.Restart();
                vm.Contracts.OnChanged?.Invoke();
                Layout();
                Console.WriteLine($"RENDERAPP second refresh + relayout (warm): {clock.ElapsedMilliseconds} ms");
                clock.Restart();
                if (appId == "home") vm.Dashboard.Attach(vm.LiveWorld!);
                else if (appId == "roster") vm.MyRoster.Attach(vm.LiveWorld!);
                else if (appId == "fighters") vm.FightersPage.Attach(vm.LiveWorld!, new Dictionary<MmaManager.Core.Model.FightId, (MmaManager.Core.Model.Fight, MmaManager.Core.Model.Event)>());
                else if (appId == "news") vm.News.Attach(vm.LiveWorld!);
                else if (appId == "inbox") vm.Mail.Attach(vm.LiveWorld!);
                else if (appId == "planner") vm.Planner.Attach(vm.LiveWorld!);
                long attach = clock.ElapsedMilliseconds;
                if (appId == "fighters")
                    Console.WriteLine($"RENDERAPP fighters index (scouted read for every active fighter) {vm.FightersPage.LastIndexMs} ms, filter {vm.FightersPage.LastFilterMs} ms; {vm.FightersPage.CountLine}");
                Layout();
                Console.WriteLine($"RENDERAPP this app alone: rebuild {attach} ms + relayout {clock.ElapsedMilliseconds - attach} ms");
                if (e.Args.Contains("--rankorg"))   // (a refresh puts the viewed house back to the player's: view the rival again)
                {
                    string abbr = e.Args[Array.IndexOf(e.Args, "--rankorg") + 1];
                    vm.SelectedOrg = vm.Organizations.FirstOrDefault(p => p.Abbreviation == abbr) ?? vm.SelectedOrg;
                    Layout();
                }
                if (e.Args.Contains("--advance"))
                {
                    // A week of the world going by with this app open (sim tick + every refresh + relayout).
                    clock.Restart();
                    vm.AdvanceWeekCommand.Execute(null);
                    Layout();
                    Console.WriteLine($"RENDERAPP advance a week with {appId} open: {clock.ElapsedMilliseconds} ms");
                }
                // --search <text>: type it into the Fighters search one key at a time, timing
                // each keystroke's filter + relayout (search-as-you-type), and leave it there.
                if (e.Args.Contains("--search"))
                {
                    string text = e.Args[Array.IndexOf(e.Args, "--search") + 1];
                    var times = new List<long>();
                    for (int i = 1; i <= text.Length; i++)
                    {
                        clock.Restart();
                        vm.FightersPage.Search = text[..i];
                        Layout();
                        times.Add(clock.ElapsedMilliseconds);
                    }
                    Console.WriteLine($"RENDERAPP search '{text}': per keystroke (filter + relayout) {string.Join(", ", times)} ms; max {times.Max()} ms, " +
                                      $"last filter alone {vm.FightersPage.LastFilterMs} ms -> {vm.FightersPage.CountLine}");
                }
                // --folder <name>: open an inbox folder; --letter: open the first letter with an action in it.
                if (e.Args.Contains("--folder"))
                {
                    var name = e.Args[Array.IndexOf(e.Args, "--folder") + 1];
                    if (Enum.TryParse<MailFolder>(name, true, out var folder)) vm.Mail.ShowFolder(folder);
                    Layout();
                }
                // --letterfrom <owner|offer|partner>: open the newest letter from the owner, an offer letter, or a partner writing in.
                if (e.Args.Contains("--letterfrom"))
                {
                    string who = e.Args[Array.IndexOf(e.Args, "--letterfrom") + 1];
                    string owner = MmaManager.Core.Sim.Living.Mail.Owner(vm.LiveWorld!);
                    vm.Mail.ShowFolder(MailFolder.Everything);
                    Layout();
                    var letter = who switch
                    {
                        "owner" => vm.Mail.Letters.FirstOrDefault(l => l.Sender == owner || l.Sender == "The Owner"),
                        "offer" => vm.Mail.Letters.FirstOrDefault(l => l.HasOfferBrand),
                        _ => vm.Mail.Letters.FirstOrDefault(l => l.HasBrand),
                    };
                    vm.Mail.Selected = letter;
                    Console.WriteLine($"RENDERAPP letter from {who}: '{letter?.Subject}' from {letter?.Sender} ({letter?.SenderRole}); face {letter?.HasPicture}, brand '{letter?.Brand}', offer brand '{letter?.OfferBrand}'");
                    Layout();
                }
                if (e.Args.Contains("--letter"))
                {
                    var letter = vm.Mail.Letters.FirstOrDefault(l => l.Actions.Any(a => a.Parameter is MmaManager.Core.Model.Decision))
                                 ?? vm.Mail.Letters.FirstOrDefault(l => l.HasActions)
                                 ?? vm.Mail.Letters.FirstOrDefault();
                    vm.Mail.Selected = letter;
                    Console.WriteLine($"RENDERAPP letter: '{letter?.Subject}' from {letter?.Sender} — actions: {string.Join(" | ", letter?.Actions.Select(a => a.Text) ?? Array.Empty<string>())}");
                    Layout();
                }
                // --profile <people|plain>: open the Fighter Profile on a fighter with a people passage (mentor, family,
                // feud, camp; the richest one first) or one with none, to see the bio area both ways.
                if (e.Args.Contains("--profile"))
                {
                    var w = vm.LiveWorld!;
                    bool people = e.Args[Array.IndexOf(e.Args, "--profile") + 1] != "plain";
                    var pick = w.Fighters.Values.Where(f => f.Status == MmaManager.Core.Model.FighterStatus.Active && !f.LocalTalent)
                        .Select(f => (f, n: MmaManager.Core.Sim.Flavor.Biographer.People(w, f).Count))
                        .Where(t => people ? t.n > 0 : t.n == 0)
                        .OrderByDescending(t => people ? Math.Min(t.n, 3) : 0).ThenBy(t => t.f.Id.Value).Select(t => t.f).FirstOrDefault();
                    if (pick is not null) vm.OpenFighter(pick.Id.Value);
                    Layout();
                    Console.WriteLine($"RENDERAPP profile ({(people ? "people" : "plain")}): {pick?.FullName ?? "nobody"} - {(pick is null ? "" : MmaManager.Core.Sim.Flavor.Biographer.PeopleText(w, pick))}");
                }
                // --fame <star|regional|unknown|mine>: the fame heat map. A fighter (the best-known name in the world,
                // a name known in one region only, or one known nowhere) is opened on his profile and, unless the
                // app is the profile itself, put on the World Map's fame layer the way "Where he's known" does;
                // "mine" switches the map to where the player's own promotion is known.
                if (e.Args.Contains("--fame"))
                {
                    var w = vm.LiveWorld!;
                    string kind = e.Args[Array.IndexOf(e.Args, "--fame") + 1];
                    if (kind == "mine")
                    {
                        vm.WorldMap.ShowPromotion();
                        var mine = w.Get(w.PlayerPromotion);
                        Console.WriteLine($"RENDERAPP fame (mine): {mine?.Name}; known in {mine?.PopularityByCity.Count(kv => kv.Value.Value > 0) ?? 0} cities; {vm.WorldMap.SubjectLine}");
                    }
                    else
                    {
                        static int TopCity(MmaManager.Core.Model.Fighter f) => f.CityReputation.Count == 0 ? 0 : f.CityReputation.Values.Max(v => v.Value);
                        var pool = w.Fighters.Values.Where(f => f.Status != MmaManager.Core.Model.FighterStatus.Retired).OrderBy(f => f.Id.Value).ToList();
                        int RegionsKnown(MmaManager.Core.Model.Fighter f) => Enum.GetValues<MmaManager.Core.Model.Region>()
                            .Count(r => MmaManager.Core.Sim.Geography.Reputation.FighterInRegion(w, f, r) >= 8);
                        var pick = kind switch
                        {
                            // the widest-known name: most cities that know him well, then the deepest fame
                            "star" => pool.OrderByDescending(f => f.CityReputation.Values.Count(v => v.Value >= 20))
                                          .ThenByDescending(f => MmaManager.Core.Sim.Geography.Reputation.FighterBest(w, f)).ThenBy(f => f.Id.Value).FirstOrDefault(),
                            // a working regional name: known (30-55) in his best city, and in one region only
                            "regional" => pool.Where(f => !f.LocalTalent && TopCity(f) is >= 30 and <= 55 && RegionsKnown(f) == 1)
                                              .OrderByDescending(f => f.CityReputation.Values.Count(v => v.Value > 0)).ThenBy(f => f.Id.Value).FirstOrDefault(),
                            // the least-known name on file (a signed or listed fighter before a scouted local)
                            _ => pool.OrderBy(TopCity).ThenBy(f => f.LocalTalent).ThenBy(f => f.Id.Value).FirstOrDefault(),
                        };
                        if (pick is not null)
                        {
                            vm.OpenFighter(pick.Id.Value);
                            if (appId != "fighter") MmaManager.Wpf.Mvvm.AppCommands.ShowFame.Execute(pick.Id.Value);
                        }
                        Console.WriteLine($"RENDERAPP fame ({kind}): {pick?.FullName ?? "nobody"} pop {pick?.Intangibles.Popularity.Value} best region "
                            + $"{(pick is null ? 0 : MmaManager.Core.Sim.Geography.Reputation.FighterBest(w, pick))} top city {(pick is null ? 0 : TopCity(pick))}; "
                            + $"map layer {vm.WorldMap.Layer}; {vm.WorldMap.SubjectLine}");
                    }
                    Layout();
                }
                // --drillfame: open the country of the subject's hottest city on the fame map (the city level).
                if (e.Args.Contains("--drillfame") && vm.WorldMap.HotSpots.FirstOrDefault() is { } spot)
                {
                    vm.WorldMap.HotSpotCommand.Execute(spot);
                    Layout();
                    Console.WriteLine($"RENDERAPP fame drilled into {spot.Where} ({spot.City} {spot.Fame})");
                }
                // The drawn World Map's states: --maplist (the List of place cards instead), --mapregion <Europe|MiddleEast|...>
                // (zoomed to a region, as a click on its name does), --mapcity <name> (a city clicked: its country's cards
                // beside the zoomed map), --hover <city name> / --hovercountry <Natural Earth code> (the hover card).
                if (appId == "map")
                {
                    var wm = vm.WorldMap;
                    if (e.Args.Contains("--maplist")) wm.ShowList = true;
                    if (e.Args.Contains("--mapregion"))
                        wm.MapRegionCommand.Execute(Enum.Parse<MmaManager.Core.Model.Region>(e.Args[Array.IndexOf(e.Args, "--mapregion") + 1], true));
                    if (e.Args.Contains("--mapcity"))
                    {
                        string name = e.Args[Array.IndexOf(e.Args, "--mapcity") + 1];
                        wm.MapSpotCommand.Execute(wm.MapSpots.FirstOrDefault(s => s.Name == name));
                    }
                    Layout();
                    if (e.Args.Contains("--hover"))
                    {
                        string name = e.Args[Array.IndexOf(e.Args, "--hover") + 1];
                        wm.HoverSpot = wm.MapSpots.FirstOrDefault(s => s.Name == name);
                    }
                    if (e.Args.Contains("--hovercountry")) wm.HoverCountry = e.Args[Array.IndexOf(e.Args, "--hovercountry") + 1];
                    // --mapadvance: a week goes by with the map drilled in (the drill, layer and subject must survive the refresh).
                    if (e.Args.Contains("--mapadvance"))
                    {
                        string before = $"{string.Join(" > ", wm.Breadcrumb.Select(c => c.Label))} / {wm.Layer} / list {wm.ShowList}";
                        vm.AdvanceWeekCommand.Execute(null);
                        string after = $"{string.Join(" > ", wm.Breadcrumb.Select(c => c.Label))} / {wm.Layer} / list {wm.ShowList}";
                        Console.WriteLine($"RENDERAPP map across a week: before [{before}] after [{after}] {(before == after ? "KEPT" : "CHANGED")}");
                    }
                    Layout();
                    Console.WriteLine($"RENDERAPP map: list {wm.ShowList}, level {string.Join(" > ", wm.Breadcrumb.Select(c => c.Label))}, layer {wm.Layer}, "
                        + $"{wm.MapSpots.Count} cities ({wm.MapSpots.Count(s => s.Lit)} lit, {wm.MapSpots.Count(s => s.AlwaysLabel)} always labelled), "
                        + $"{wm.MapFills.Count} tinted lands, {MmaManager.Wpf.Map.WorldShapes.Land.Count} land shapes; hover '{wm.HoverTitle}' {wm.HoverValue} / {wm.HoverOrgs}");
                }
                // --talks <fa|lifer|unhappy>: put someone across the Contracts table (or on the
                // Fighter Profile, when that's the app): the top free agent, or one of our own
                // made a holding lifer (a company man at loyalty 90) — "unhappy" also puts the
                // books in the red, the grievance he can read.
                if (e.Args.Contains("--talks"))
                {
                    string mode = e.Args[Array.IndexOf(e.Args, "--talks") + 1];
                    var w = vm.LiveWorld!;
                    MmaManager.Core.Model.Fighter? who = null;
                    if (mode == "fa")
                        who = vm.Contracts.FreeAgents.Select(r => w.Get(r.Id)).FirstOrDefault(f => f is not null);
                    else
                    {
                        foreach (var f in w.RosterOf(w.PlayerPromotion)
                                     .Where(f => f.Status == MmaManager.Core.Model.FighterStatus.Active && f.Contract is { FightsRemaining: > 0 })
                                     .OrderBy(f => f.Id.Value))
                        {
                            f.Disposition = MmaManager.Core.Model.Disposition.CompanyMan;
                            f.Loyalty = 90;
                            if (MmaManager.Core.Sim.Contracts.Lifers.IsHolding(w, f)) { who = f; break; }
                        }
                        if (mode == "unhappy") w.Get(w.PlayerPromotion)!.Balance = new MmaManager.Core.Model.Money(-50_000m);
                    }
                    if (who is not null)
                    {
                        if (appId == "fighter") vm.OpenFighter(who.Id.Value);
                        else vm.Contracts.OpenTalksWith(who.Id);
                    }
                    Layout();
                    Console.WriteLine($"RENDERAPP talks ({mode}): {who?.FullName ?? "nobody"} — {vm.Contracts.Asking}");
                    // --room: also draw the negotiating room for him, as it opens, to <path>-room.png.
                    if (e.Args.Contains("--room") && who is not null && w.Get(w.PlayerPromotion) is { } mine)
                    {
                        var exp = new MmaManager.Core.Sim.Contracts.FighterValuation().Evaluate(w, who);
                        var session = new MmaManager.Core.Sim.Contracts.NegotiationSession(w, who, mine, exp);
                        var roomVm = new NegotiationViewModel(w, who, mine, exp, session, renewal: who.Contract?.Promotion == mine.Id);
                        var roomWin = new NegotiationWindow { DataContext = roomVm };
                        var roomRoot = (FrameworkElement)roomWin.Content;
                        roomWin.Content = null;
                        var roomSize = new Size(roomWin.Width is > 0 and var rw ? rw : 900, roomWin.Height is > 0 and var rh ? rh : 760);
                        var host = new System.Windows.Controls.Border
                        {
                            Background = (Brush)Current.Resources["Bg"] ?? Brushes.Black,
                            Child = roomRoot, DataContext = roomVm,
                        };
                        host.Measure(roomSize); host.Arrange(new Rect(roomSize)); host.UpdateLayout();
                        var roomBmp = new RenderTargetBitmap((int)roomSize.Width, (int)roomSize.Height, 96, 96, PixelFormats.Pbgra32);
                        roomBmp.Render(host);
                        var roomEnc = new PngBitmapEncoder(); roomEnc.Frames.Add(BitmapFrame.Create(roomBmp));
                        string roomPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[^1]))!,
                            Path.GetFileNameWithoutExtension(e.Args[^1]) + "-room.png");
                        using (var fs = File.Create(roomPath)) roomEnc.Save(fs);
                        Console.WriteLine($"RENDERAPP room -> {roomPath}: his purse {roomVm.HisPurse}; {roomVm.AskWhy}");
                        roomWin.Close();
                    }
                }
                // The Planner's states: --agenda (the list), --scope <Just mine|Near me|Everywhere|Flagships>,
                // --select <days ahead | card | clash> (a picked date: N days out, your first card's
                // date, or the first date whose clash window holds a card that would cost the house).
                if (appId == "planner")
                {
                    var pl = vm.Planner;
                    if (e.Args.Contains("--injure"))
                    {
                        // Harness only: hurt a few of your unbooked fighters (returns two to eight weeks
                        // out) so the "cleared to fight" markers have something to show.
                        var w = vm.LiveWorld!;
                        int k = 0;
                        foreach (var f in w.RosterOf(w.PlayerPromotion).Where(f => f.Status == MmaManager.Core.Model.FighterStatus.Active
                                     && !MmaManager.Core.Sim.Living.ForwardCalendar.IsCommitted(w, f.Id)).OrderBy(f => f.Id.Value).Take(3))
                        {
                            var back = w.CurrentDate.AddDays(14 + 21 * k++);
                            f.CurrentInjury = new MmaManager.Core.Model.Injury
                            {
                                Severity = MmaManager.Core.Model.InjurySeverity.Moderate, Description = k == 1 ? "broken hand" : k == 2 ? "torn meniscus" : "cut over the eye",
                                OccurredOn = w.CurrentDate, ExpectedReturn = back,
                            };
                            f.Status = MmaManager.Core.Model.FighterStatus.Injured;
                            f.ReadyToFightOn = back;
                        }
                        vm.Contracts.OnChanged?.Invoke();
                    }
                    pl.Wanted();
                    if (e.Args.Contains("--agenda")) pl.ShowAgenda = true;
                    if (e.Args.Contains("--scope")) pl.Scope = e.Args[Array.IndexOf(e.Args, "--scope") + 1];
                    if (e.Args.Contains("--select"))
                    {
                        string what = e.Args[Array.IndexOf(e.Args, "--select") + 1];
                        var w = vm.LiveWorld!;
                        var today = w.CurrentDate;
                        DateOnly? pick = null;
                        if (what == "card")
                            pick = MmaManager.Core.Sim.Living.ForwardCalendar.Upcoming(w, today, promotion: w.PlayerPromotion).FirstOrDefault()?.Date;
                        else if (what == "clash")
                        {
                            for (int d = 1; d < 60 && pick is null; d++)
                                if (MmaManager.Core.Sim.Living.ForwardCalendar.RivalCardsNear(w, w.PlayerPromotion, today.AddDays(d)).Rivals.Any(r => r.Charged))
                                    pick = today.AddDays(d);
                        }
                        else pick = today.AddDays(int.Parse(what));
                        pl.Selected = pick;
                        Console.WriteLine($"RENDERAPP planner picked {pick:ddd d MMM yyyy}: {pl.ClashLine}");
                    }
                    Layout();
                    Console.WriteLine($"RENDERAPP planner: {pl.Headline} / {pl.Subline}; {pl.Days.Count} squares, {pl.Agenda.Count} agenda rows, {pl.HeadsUp.Count} heads-up, {pl.Flagships.Count} flagships");
                }
                if (platformMode is not null)
                {
                    var md = vm.Media;
                    var w = vm.LiveWorld!;
                    var mine = w.Get(w.PlayerPromotion)!;
                    // The desk's own buttons, found in the drawn tree: proves the template's bindings reach the commands.
                    var buttons = FindAll<System.Windows.Controls.Button>(root).ToList();
                    var sign = buttons.FirstOrDefault(b => b.Content is string s && s.StartsWith("Sign", StringComparison.Ordinal) && b.DataContext is OfferRow);
                    var quiet = buttons.FirstOrDefault(b => ReferenceEquals(b.Command, md.AskShutdownCommand));
                    Console.WriteLine($"RENDERAPP platform: sign button '{sign?.Content}' wired {sign?.Command is not null}; pass wired "
                        + $"{buttons.Any(b => b.DataContext is OfferRow && ReferenceEquals(b.Command, md.DeclineOfferCommand))}; shut-down link '{(quiet?.Content)}' wired {quiet is not null}");
                    if (md.Offers.FirstOrDefault(o => o.HasTrade)?.Trade is { } tv)
                        Console.WriteLine($"RENDERAPP trade: net {tv.PlatformNet}, pays {tv.OfferPays}, gain {tv.Gain}; warning '{tv.Warning}'");
                    if (platformMode.StartsWith("shut", StringComparison.Ordinal))
                    {
                        if (quiet is not null) quiet.Command.Execute(null); else md.AskShutdownCommand.Execute(null);
                        Layout();
                        Console.WriteLine($"RENDERAPP shutdown asked: {md.Shutdown?.Subscribers} subs, net {md.Shutdown?.NetPerQuarter}, relaunch {md.Shutdown?.Relaunch}; {md.Shutdown?.Reach}");
                        if (platformMode == "shutdone")
                        {
                            var confirm = FindAll<System.Windows.Controls.Button>(root).FirstOrDefault(b => ReferenceEquals(b.Command, md.ShutdownCommand));
                            Console.WriteLine($"RENDERAPP shutdown confirm button wired {confirm is not null}");
                            (confirm?.Command ?? md.ShutdownCommand).Execute(null);
                        }
                    }
                    if (platformMode is "accepted" or "ask-misses")
                    {
                        // The one question the Sign button asks first (never shown as a dialog here), drawn to <path>-confirm.png.
                        if (md.Offers.FirstOrDefault(o => o.HasTrade) is { } asked && md.SignQuestion(asked.Offer) is { } q)
                        {
                            var dlg = new MessageWindow(q.Heading, q.Body, q.Detail, severe: true, confirm: q.Confirm, keep: q.Keep);
                            var droot = (FrameworkElement)dlg.Content;
                            droot.Measure(new Size(480, double.PositiveInfinity));
                            var dsize = new Size(480, Math.Ceiling(droot.DesiredSize.Height));
                            droot.Arrange(new Rect(dsize)); droot.UpdateLayout();
                            var drtb = new RenderTargetBitmap((int)dsize.Width, (int)dsize.Height, 96, 96, PixelFormats.Pbgra32);
                            drtb.Render(droot);
                            var denc = new PngBitmapEncoder(); denc.Frames.Add(BitmapFrame.Create(drtb));
                            string dp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[^1]))!, Path.GetFileNameWithoutExtension(e.Args[^1]) + "-confirm.png");
                            using (var dfs = File.Create(dp)) denc.Save(dfs);
                            Console.WriteLine("RENDERAPP sign question -> " + dp);
                            dlg.Close();
                        }
                        if (platformMode != "accepted") { }   // ask-misses: the question only, nothing signed
                        else if (sign is not null) sign.Command.Execute(sign.CommandParameter);
                        else if (md.Offers.FirstOrDefault(o => o.HasTrade) is { } row) md.AcceptOfferCommand.Execute(row);
                    }
                    Layout();
                    Console.WriteLine($"RENDERAPP platform after '{platformMode}': stream {(mine.Stream?.Name ?? "none")}, streaming lane "
                        + $"{MmaManager.Core.Sim.Finance.MediaService.DealIn(mine, MmaManager.Core.Model.MediaLane.Streaming)?.Partner ?? "open"}, "
                        + $"offers {md.Offers.Count}, deals on the desk [{string.Join(", ", md.Deals.Select(d => d.PartnerName))}], strip '{vm.StatusText}', "
                        + $"news '{w.News.LastOrDefault()?.Headline}'");
                }
                WaitThumbs("before the still");
                foreach (var wall in FindAll<MmaManager.Wpf.Controls.CardWall>(root))
                    Console.WriteLine($"RENDERAPP card wall: {wall.Lines.Count} lines x {wall.Columns} columns, {wall.ActualWidth:0}x{wall.ActualHeight:0} px, "
                        + $"cards drawn {FindAll<MmaManager.Wpf.Controls.RatingBadge>(wall).Count()}, lines realised "
                        + $"{FindAll<System.Windows.Controls.VirtualizingStackPanel>(wall).FirstOrDefault()?.Children.Count} "
                        + $"(viewport {FindAll<System.Windows.Controls.ScrollViewer>(wall).FirstOrDefault()?.ViewportHeight:0}, line heights "
                        + string.Join(",", (FindAll<System.Windows.Controls.VirtualizingStackPanel>(wall).FirstOrDefault()?.Children.OfType<FrameworkElement>() ?? Enumerable.Empty<FrameworkElement>()).Take(8).Select(c => c.ActualHeight.ToString("0"))) + ")");

                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                string p = e.Args[^1];
                using (var fs = File.Create(p)) enc.Save(fs);
                Console.WriteLine("RENDERAPP OK -> " + Path.GetFullPath(p));
            }
            catch (Exception ex) { Console.WriteLine("RENDERAPP FAIL: " + ex); }
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
                if (e.Args.Contains("--myroster")) vm.FightersPage.ShowMyPromotion(); // check the promotion filter
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

        // Watching from ringside (an Origins world with no house of the player's, seed 1993 unless --seed N):
        // `MmaManager.Wpf --renderringside <ringside|found|founded|tryout|recurring|recurringheld|founder|founderheld> W H [--years N] [--months N]
        //   [--city NAME] [--name NAME] [--lines a,b] [--house N] [--keep N] [--open APP] [--max] <path>`.
        //   ringside  the Ringside app (after --years/--months of the spectator's clock: three-month skips, so the
        //             last stop's banner shows; every stop is printed);
        //   found     the Found Your Promotion window, a name typed and a city picked (--city, else the first open);
        //   founded   the desktop just after founding: the full set of apps back, the Open Tryout panel on top;
        //   tryout    the same after the tryout was called (the men it signed);
        //   recurring the founded house played on (nobody else signed) until the sim offers the recurring tryout;
        //   recurringheld  the same after it was called (its men, and when the next could come);
        //   founder / founderheld  a founding house run from day one (--house N), thinned by --keep N releases until short.
        //   [--lines a,b] toggles weight lines on the founding form; [--open APP] brings an app to the front last.
        if (e.Args.Contains("--renderringside"))
        {
            try
            {
                int at = Array.IndexOf(e.Args, "--renderringside");
                string state = e.Args[at + 1];
                int W = int.Parse(e.Args[at + 2]), H = int.Parse(e.Args[at + 3]);
                string Arg(string flag, string fallback) => e.Args.Contains(flag) ? e.Args[Array.IndexOf(e.Args, flag) + 1] : fallback;
                ulong seed = ulong.Parse(Arg("--seed", "1993"));
                int years = int.Parse(Arg("--years", "0")), months = int.Parse(Arg("--months", "0"));
                string p = e.Args[^1];
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                vm.StartWorld(WorldBuilder.Options.OriginsObserver(seed), OriginsStart.RingsideSummary);
                var w0 = vm.LiveWorld!;
                var until = w0.CurrentDate.AddYears(years).AddMonths(months);
                while (w0.CurrentDate < until)
                {
                    vm.AdvanceQuarterCommand.Execute(null);
                    if (vm.Ringside.Stop is { Big: true } s) Console.WriteLine($"RINGSIDE stop {w0.CurrentDate:d MMM yyyy}: {s.Headline}");
                }
                Console.WriteLine($"RINGSIDE {w0.CurrentDate:d MMM yyyy}: observing={vm.IsObserving}, apps [{string.Join(", ", vm.Desktop.PromotionApps.Select(a => a.Id))}]");

                var root = (FrameworkElement)win.Content;
                void Snap(FrameworkElement el, int w, int h, string path)
                {
                    var size = new Size(w, h);
                    for (int pass = 0; pass < 3; pass++)
                    {
                        el.Measure(size); el.Arrange(new Rect(size)); el.UpdateLayout();
                        var t = System.Diagnostics.Stopwatch.StartNew();
                        do System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                        while (Portraits.PendingThumbs > 0 && t.ElapsedMilliseconds < 15000);
                    }
                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(el);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (var fs = File.Create(path)) enc.Save(fs);
                    Console.WriteLine("RENDERRINGSIDE OK -> " + Path.GetFullPath(path));
                }

                FoundingViewModel Founding()
                {
                    var f = new FoundingViewModel(w0) { Name = Arg("--name", "Lantern Street Fighting") };
                    string city = Arg("--city", "");
                    f.SelectedCity = f.Cities.FirstOrDefault(c => c.Open && c.Name == city) ?? f.Cities.FirstOrDefault(c => c.Open) ?? f.SelectedCity;
                    // --enclosure ring|cage: pick the fighting area on the form (when the city leaves it open).
                    if (e.Args.Contains("--enclosure"))
                        f.Enclosure = f.EnclosureChoices.FirstOrDefault(c => string.Equals(c.Enclosure?.ToString(), Arg("--enclosure", ""), StringComparison.OrdinalIgnoreCase)) ?? f.Enclosure;
                    Console.WriteLine($"RINGSIDE founding: {f.Cities.Count(c => c.Open)} open of {f.Cities.Count} listed; city {f.SelectedCity?.Name}; abbreviation {f.Abbreviation}; "
                                      + $"rules picks: minutes {f.MinuteChoices.Count}, gloves {f.GloveChoices.Count}, enclosure {f.EnclosureChoices.Count}");
                    // --lines a,b,...: toggle these weight lines (by class name) after the city's suggestion is in.
                    if (e.Args.Contains("--lines"))
                        foreach (var name in Arg("--lines", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                            if (f.LineGroups.SelectMany(g => g.Lines).FirstOrDefault(l => string.Equals(l.Class.ToString(), name, StringComparison.OrdinalIgnoreCase)) is { } line)
                                line.On = !line.On;
                    Console.WriteLine($"RINGSIDE lines: picker {f.HasLinePicker}; " + string.Join(" | ", f.LineGroups.Select(g => g.Title + ": "
                        + string.Join(", ", g.Lines.Select(l => $"{l.Class}{(l.On ? "+" : "-")}{(l.Locked ? " locked" : "")}{(l.Suggested ? " suggested" : "")}")))));
                    Console.WriteLine($"RINGSIDE divisions: {f.Divisions}");
                    return f;
                }

                // Play week by week (his nights simmed as they come; nobody signed) until the sim offers a tryout, then open it.
                static void RecurringUntilOffered(MainViewModel m, MmaManager.Core.Model.World w, int maxWeeks)
                {
                    for (int i = 0; i < maxWeeks && !MmaManager.Core.Sim.Living.PlayerFounding.TryoutOptions(w, w.CurrentDate).Available; i++)
                        OriginsHarness.Play(m, 1, hold: false);
                    var o = MmaManager.Core.Sim.Living.PlayerFounding.TryoutOptions(w, w.CurrentDate);
                    Console.WriteLine($"RINGSIDE {w.CurrentDate:d MMM yyyy}: offer available {o.Available}, free {o.Free}, turnout {o.Turnout}, next {o.NextPossibleOn}; refusal '{o.Refusal}'; "
                                      + $"apps [{string.Join(", ", m.Desktop.PromotionApps.Select(a => a.Id))}]");
                    m.Briefing.Attach(w);
                    Console.WriteLine($"RINGSIDE briefing waiting: {string.Join(" | ", m.Briefing.Waiting.Select(x => x.Text + " / " + x.Detail))}");
                    m.Desktop.Open("tryout");
                }
                static void HoldRecurring(MainViewModel m, MmaManager.Core.Model.World w)
                {
                    m.Tryout.HoldCommand.Execute(null);
                    var o = MmaManager.Core.Sim.Living.PlayerFounding.TryoutOptions(w, w.CurrentDate);
                    Console.WriteLine($"RINGSIDE recurring tryout held: {m.Tryout.Signed.Count} signed; status: {m.StatusText}; next offer: available {o.Available}, next {o.NextPossibleOn}; "
                                      + $"kicker '{m.Tryout.Kicker}', next line '{m.Tryout.NextLine}'");
                }

                if (state == "found")
                {
                    var dlg = new FoundPromotionWindow(Founding(), interactive: false);
                    Snap((FrameworkElement)dlg.Content, (int)dlg.Width, (int)dlg.Height, p);
                }
                else
                {
                    if (state is "founded" or "tryout")
                    {
                        // Lay the desk out first, so the centred Open Tryout panel knows the desk's size (as in play).
                        root.Measure(new Size(W, H)); root.Arrange(new Rect(new Size(W, H))); root.UpdateLayout();
                        var f = Founding();
                        f.FoundCommand.Execute(null);
                        if (f.Founded is null) throw new InvalidOperationException("founding refused: " + f.Error);
                        vm.AfterFounding(f.Founded);
                        Console.WriteLine($"RINGSIDE founded {f.Founded.Name} ({f.Founded.Abbreviation}): observing={vm.IsObserving}, apps [{string.Join(", ", vm.Desktop.PromotionApps.Select(a => a.Id))}], "
                                          + $"open [{string.Join(", ", vm.Desktop.Windows.Select(x => x.Spec.Id))}]; status: {vm.StatusText}");
                        Console.WriteLine($"RINGSIDE picked \"{f.Enclosure?.Label}\"; the house rules now read: {string.Join(" | ", vm.Tryout.Rules)}");
                        if (state == "tryout")
                        {
                            vm.Tryout.HoldCommand.Execute(null);
                            Console.WriteLine($"RINGSIDE tryout: {vm.Tryout.Signed.Count} signed; ledger $0 lines skipped; status: {vm.StatusText}");
                        }
                    }
                    else if (state is "recurring" or "recurringheld")
                    {
                        // A house he founded, its free tryout held, then played week by week (nobody else signed) until the
                        // sim offers the recurring tryout (short of men, half a year on); "recurringheld" then calls it.
                        root.Measure(new Size(W, H)); root.Arrange(new Rect(new Size(W, H))); root.UpdateLayout();
                        var f = Founding();
                        f.FoundCommand.Execute(null);
                        if (f.Founded is null) throw new InvalidOperationException("founding refused: " + f.Error);
                        vm.AfterFounding(f.Founded);
                        vm.Tryout.HoldCommand.Execute(null);
                        vm.Tryout.LaterCommand?.Execute(null);
                        Console.WriteLine($"RINGSIDE free tryout: {vm.Tryout.Signed.Count} signed on {w0.CurrentDate:d MMM yyyy}");
                        RecurringUntilOffered(vm, w0, 60);
                        if (state == "recurringheld") HoldRecurring(vm, w0);
                    }
                    else if (state is "founder" or "founderheld")
                    {
                        // A founding house of the sport run from day one (--house 0 Crucible | 1 | 2), its roster thinned by
                        // releases (through the game's own release) until it is short of men: the recurring tryout only.
                        int house = int.Parse(Arg("--house", "0"));
                        vm.StartWorld(WorldBuilder.Options.Origins(1993, house), OriginsHarness.Summary(house));
                        var w1 = vm.LiveWorld!;
                        root.Measure(new Size(W, H)); root.Arrange(new Rect(new Size(W, H))); root.UpdateLayout();
                        var promo = w1.Get(w1.PlayerPromotion)!;
                        int keep = int.Parse(Arg("--keep", "3"));
                        foreach (var man in w1.RosterOf(promo.Id).OrderByDescending(x => x.Id.Value).Skip(keep).ToList())
                            MmaManager.Core.Sim.Contracts.ContractLifecycle.Release(w1, man, dentReputation: false);
                        vm.Contracts.OnChanged?.Invoke();
                        var offer = MmaManager.Core.Sim.Living.PlayerFounding.TryoutOptions(w1, w1.CurrentDate);
                        Console.WriteLine($"RINGSIDE founder {promo.Abbreviation}: founded by player {MmaManager.Core.Sim.Living.PlayerFounding.FoundedByPlayer(promo)}; "
                                          + $"{w1.RosterOf(promo.Id).Count()} on the roster; offer available {offer.Available}, free {offer.Free}, turnout {offer.Turnout}; "
                                          + $"refusal '{offer.Refusal}'; apps [{string.Join(", ", vm.Desktop.PromotionApps.Select(a => a.Id))}]");
                        if (offer.Free) throw new InvalidOperationException("a founding house was offered the free tryout");
                        vm.Desktop.Open("tryout");
                        if (state == "founderheld") HoldRecurring(vm, w1);
                    }
                    else vm.Desktop.Open("ringside");
                    // --open <app>: bring another app to the front last (the briefing's "also on your desk", say).
                    if (e.Args.Contains("--open")) vm.Desktop.Open(Arg("--open", "home"));
                    // --max: maximize that app's window (with a tall render it shows all of it).
                    if (e.Args.Contains("--max") && vm.Desktop.Windows.FirstOrDefault(x => x.Spec.Id == Arg("--open", "tryout")) is { } front) front.IsMaximized = true;
                    // --skipping: the taskbar as it reads while a Next Chapter skip is running (progress, Stop, the rest disabled).
                    if (e.Args.Contains("--skipping"))
                    {
                        vm.ShowSkipStateForHarness($"Watching…  {w0.CurrentDate.AddDays(7 * 23):MMMM yyyy}  ·  23 weeks");
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    }
                    Snap(root, W, H, p);
                }
            }
            catch (Exception ex) { Console.WriteLine("RENDERRINGSIDE FAIL: " + ex); }
            Environment.Exit(0);
            return;
        }

        // Offscreen screenshot of the New Game setup dialog: `MmaManager.Wpf --rendernewgame [--takeover ABBR] <path>`.
        if (e.Args.Contains("--rendernewgame"))
        {
            try
            {
                var win = new NewGameWindow();
                // --takeover <ABBR>: the "take over" option picked, with that house selected (its owner's card shows).
                if (e.Args.Contains("--takeover"))
                {
                    string abbr = e.Args[Array.IndexOf(e.Args, "--takeover") + 1];
                    win.TakeoverMode.IsChecked = true;
                    foreach (var item in win.TakeoverBox.Items)
                        if (item is NewGameWindow.TakeoverRow r && r.Spec.Abbreviation == abbr) win.TakeoverBox.SelectedItem = item;
                }
                if (e.Args.Contains("--origins")) win.ChooseOrigins(int.Parse(e.Args[Array.IndexOf(e.Args, "--origins") + 1]));
                if (e.Args.Contains("--ringside")) win.ChooseRingside();   // Origins, watching from ringside
                var root = (FrameworkElement)win.Content;
                // The window sizes to its content: measure it at its width, then draw it at the height it wants.
                for (int pass = 0; pass < 2; pass++)
                {
                    root.Measure(new Size(win.Width, double.PositiveInfinity));
                    root.Arrange(new Rect(new Size(win.Width, root.DesiredSize.Height))); root.UpdateLayout();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                }
                root.Measure(new Size(win.Width, double.PositiveInfinity));
                int W = (int)win.Width, H = (int)Math.Ceiling(root.DesiredSize.Height);
                Console.WriteLine($"RENDERNEWGAME window {W}x{H}");
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

        // Open every desktop app, one at a time, and lay it out and render it
        // offscreen — once for the quick-start big org (AFC, a month in) and once
        // for a freshly founded shoestring promotion: `MmaManager.Wpf --smokeapps`.
        // One OK/FAIL line per app; exits 1 if any app failed to open.
        if (e.Args.Contains("--smokeapps"))
        {
            int run = 0, failed = 0;
            Exception? deferred = null;   // errors raised in queued dispatcher work (Loaded handlers, bindings)
            DispatcherUnhandledException += (_, de) => { deferred ??= de.Exception; de.Handled = true; };

            static string Describe(Exception ex)
            {
                string msg = ex.GetType().Name + ": " + ex.Message;
                if (ex.InnerException is { } inner) msg += " -> " + inner.GetType().Name + ": " + inner.Message;
                return msg.Replace("\r", " ").Replace("\n", " ");
            }

            void Smoke(string label, Action<MainViewModel> setup)
            {
                MainWindow win;
                MainViewModel vm;
                try
                {
                    win = new MainWindow();
                    vm = (MainViewModel)win.DataContext;
                    setup(vm);
                }
                catch (Exception ex)
                {
                    run++; failed++;
                    Console.WriteLine($"FAIL  {label,-8} (world setup)  {Describe(ex)}");
                    return;
                }

                var root = (FrameworkElement)win.Content;
                const int W = 1500, H = 950;
                var size = new Size(W, H);
                foreach (var spec in vm.Desktop.PromotionApps.Concat(vm.Desktop.WorldApps).ToList())
                {
                    run++;
                    deferred = null;
                    try
                    {
                        foreach (var open in vm.Desktop.Windows.ToList()) vm.Desktop.Close(open);
                        // An app can leave the desk when its window closes (the Open Tryout, once the tryout is held).
                        if (!vm.Desktop.PromotionApps.Concat(vm.Desktop.WorldApps).Contains(spec))
                        {
                            Console.WriteLine($"OK    {label,-8} {spec.Id,-14} {spec.Title}  (left the desk when its window closed, as it should)");
                            continue;
                        }
                        vm.Desktop.Open(spec.Id);
                        if (!vm.Desktop.Windows.Any(w => w.Spec.Id == spec.Id))
                            throw new InvalidOperationException("the app window did not open");
                        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                            System.Windows.Threading.DispatcherPriority.SystemIdle, new Action(() => { }));
                        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                        new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32).Render(root);
                        if (deferred is not null) throw deferred;
                        Console.WriteLine($"OK    {label,-8} {spec.Id,-14} {spec.Title}");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine($"FAIL  {label,-8} {spec.Id,-14} {spec.Title}  {Describe(ex)}");
                    }
                }
            }

            Smoke("bigorg", vm =>
            {
                vm.AdvanceMonthCommand.Execute(null);   // a month of results in the feeds
                vm.SelectedFighter = vm.Roster.FirstOrDefault();
            });
            Smoke("founded", vm =>
            {
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
                vm.SelectedFighter = vm.Roster.FirstOrDefault();
            });
            // Origins from ringside: no house of the player's (day one, then two years on).
            Smoke("ringside", vm => vm.StartWorld(WorldBuilder.Options.OriginsObserver(1993), "Ringside."));
            Smoke("ringside+", vm =>
            {
                vm.StartWorld(WorldBuilder.Options.OriginsObserver(1993), "Ringside.");
                for (int i = 0; i < 8; i++) vm.AdvanceQuarterCommand.Execute(null);
                vm.AdvanceChapterCommand.Execute(null);
                // Save and load it back through the app's own load path (the autosave is off in harness runs).
                string tmp = Path.Combine(Path.GetTempPath(), $"ringside-smoke-{Environment.ProcessId}.mmasave");
                MmaManager.Persistence.SaveGame.Save(vm.LiveWorld!, tmp);
                vm.LoadFromPath(tmp);
                File.Delete(tmp);
                if (!vm.IsObserving) throw new InvalidOperationException("a ringside save loaded back with a house");
                Console.WriteLine($"OK    ringside+ saved and loaded at {vm.LiveWorld!.CurrentDate:d MMM yyyy}; apps [{string.Join(", ", vm.Desktop.PromotionApps.Select(a => a.Id))}]");
                vm.AdvanceWeekCommand.Execute(null);
            });
            // Skips equal single weeks: three-month and Next Chapter skips vs the same number of Week clicks.
            static string Fingerprint(MmaManager.Core.Model.World w) =>
                $"{w.CurrentDate:yyyy-MM-dd} fighters {w.Fighters.Count} pop {w.Fighters.Values.Sum(f => (long)f.Intangibles.Popularity.Value)} "
                + $"wins {w.Fighters.Values.Sum(f => (long)f.Record.Wins)} personas {w.Fighters.Values.Count(f => f.PersonaTag is { Length: > 0 })} "
                + $"events {w.Events.Count} news {w.News.Count} chapters {w.Era.Chapters.Count} houses {w.Promotions.Count} "
                + $"bank {w.Promotions.Values.Sum(p => p.Balance.Amount)}";
            Smoke("skips", vm =>
            {
                vm.StartWorld(WorldBuilder.Options.OriginsObserver(1993), "Ringside.");
                var start = vm.LiveWorld!.CurrentDate;
                // A stopped-early skip: three-month skips until one ends early on a big moment.
                bool early = false;
                for (int i = 0; i < 40 && !early; i++)
                {
                    vm.AdvanceQuarterCommand.Execute(null);
                    early = vm.Ringside.Stop?.Kicker.StartsWith("TIME STOPPED EARLY", StringComparison.Ordinal) == true;
                }
                if (!early) throw new InvalidOperationException("no three-month skip stopped early in ten years");
                Console.WriteLine($"OK    skips    stopped early: {vm.Ringside.Stop!.Kicker} | {vm.Ringside.Stop.Headline}");
                // A Next Chapter skip.
                int chapters = vm.LiveWorld.Era.Chapters.Count;
                vm.AdvanceChapterCommand.Execute(null);
                Console.WriteLine($"OK    skips    next chapter: {vm.Ringside.Stop!.Kicker} | {vm.Ringside.Stop.Headline} (chapters {chapters} -> {vm.LiveWorld.Era.Chapters.Count})");
                // Stop pressed mid-skip (after the third week): the skip ends there, unless a big moment came first.
                var before = vm.LiveWorld.CurrentDate;
                vm.SkipProbe = n => { if (n == 3) vm.StopSkipCommand.Execute(null); };
                vm.AdvanceChapterCommand.Execute(null);
                vm.SkipProbe = null;
                int ran = (vm.LiveWorld.CurrentDate.DayNumber - before.DayNumber) / 7;
                if (ran > 3 || vm.IsSkipping) throw new InvalidOperationException($"Stop did not end the skip: {ran} weeks, skipping={vm.IsSkipping}");
                Console.WriteLine($"OK    skips    stop pressed: {ran} weeks | {vm.Ringside.Stop!.Kicker}");
                // Two more years of skips (Next Chapter and three months), so the comparison spans newgens and many nights.
                while (vm.LiveWorld.CurrentDate < start.AddYears(3))
                {
                    vm.AdvanceChapterCommand.Execute(null);
                    vm.AdvanceQuarterCommand.Execute(null);
                }
                // The same number of single Week clicks in a fresh world must give the same world.
                int weeks = (vm.LiveWorld.CurrentDate.DayNumber - start.DayNumber) / 7;
                var twin = (MainViewModel)new MainWindow().DataContext;
                twin.StartWorld(WorldBuilder.Options.OriginsObserver(1993), "Ringside.");
                for (int i = 0; i < weeks; i++) twin.AdvanceWeekCommand.Execute(null);
                string a = Fingerprint(vm.LiveWorld), b = Fingerprint(twin.LiveWorld!);
                if (a != b) throw new InvalidOperationException($"skips and single weeks differ after {weeks} weeks: skips {a} / weeks {b}");
                Console.WriteLine($"OK    skips    {weeks} weeks by skips == {weeks} single weeks: {a}");
            });
            Smoke("founded+", vm =>
            {
                vm.StartWorld(WorldBuilder.Options.OriginsObserver(1993), "Ringside.");
                for (int i = 0; i < 12; i++) vm.AdvanceQuarterCommand.Execute(null);
                var f = new FoundingViewModel(vm.LiveWorld!) { Name = "Lantern Street Fighting" };
                f.FoundCommand.Execute(null);
                if (f.Founded is null) throw new InvalidOperationException("founding refused: " + f.Error);
                vm.AfterFounding(f.Founded);
                vm.Tryout.HoldCommand.Execute(null);
                vm.AdvanceMonthCommand.Execute(null);
                vm.SelectedFighter = vm.Roster.FirstOrDefault();
            });

            Console.WriteLine(failed == 0
                ? $"SMOKEAPPS OK: {run} app opens, 0 failed"
                : $"SMOKEAPPS FAIL: {failed} of {run} app opens failed");
            Environment.Exit(failed == 0 ? 0 : 1);
            return;
        }

        // Closed windows are let go: `MmaManager.Wpf --closecheck` (see CloseCheckHarness).
        if (e.Args.Contains("--closecheck"))
        {
            int code;
            try { code = CloseCheckHarness.Run(hold: e.Args.Contains("--hold")); }
            catch (Exception ex) { Console.WriteLine("CLOSECHECK FAIL: " + ex); code = 1; }
            Environment.Exit(code);
            return;
        }

        // Segmented toggles under stress: `MmaManager.Wpf --radiocheck`. First replays the owner's
        // crash in a shown (off-screen) shell: Card Desk opened, closed, reopened, toggled. Then opens every desktop app,
        // finds every screen holding radio buttons, then builds two more copies of that screen on
        // the SAME view model - once detached (no window: how every closed app window and every
        // harness render lives) and once side by side in one shown window - and clicks every
        // option in both copies in turn. A radio group shared across copies used to ping-pong
        // through the bindings until the stack overflowed (the Card Desk's Story/Table toggle,
        // 2026-11-20), which kills this process outright; a clean run prints RADIOCHECK OK.
        // It also fails on any RadioButton with a GroupName: named groups are app-wide, so
        // segments must group by their panel instead (see SegmentedOption in App.xaml).
        if (e.Args.Contains("--radiocheck"))
        {
            int failures = 0, flips = 0;
            void Fail(string what) { failures++; Console.WriteLine("FAIL  " + what); }
            try
            {
                var win = new MainWindow();
                var vm = (MainViewModel)win.DataContext;
                vm.AdvanceMonthCommand.Execute(null);
                var root = (FrameworkElement)win.Content;
                var size = new Size(1500, 950);
                void Lay(FrameworkElement el)
                {
                    el.Measure(size); el.Arrange(new Rect(size)); el.UpdateLayout();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                }

                // The way the owner hit it, in a real shown shell (parked off-screen): the Card Desk
                // opened and closed twice, opened again, and its sheet toggled. A closed app window's
                // screen stays in memory, bound to the view model, until the GC takes it.
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.Left = -32000; win.Top = -32000; win.ShowInTaskbar = false; win.ShowActivated = false;
                win.Show();
                for (int i = 0; i < 2; i++)
                {
                    vm.Desktop.Open("book"); Lay(root);
                    Console.WriteLine($"      shown shell: Card Desk open #{i + 1} laid out");
                    foreach (var w in vm.Desktop.Windows.ToList()) vm.Desktop.Close(w);
                    Lay(root);
                }
                vm.Desktop.Open("book"); Lay(root);
                Console.WriteLine("      shown shell: Card Desk open #3 laid out");
                for (int i = 0; i < 4; i++) { vm.Booking.ShowSheetTable = !vm.Booking.ShowSheetTable; flips++; Lay(root); }
                Console.WriteLine("OK    shown shell: Card Desk opened and closed twice, opened again, its sheet view flipped four times");

                // Every screen (nearest UserControl) that holds a bound radio, with the context it runs on.
                var hosts = new List<(Type Type, object? Context)>();
                foreach (var spec in vm.Desktop.PromotionApps.Concat(vm.Desktop.WorldApps).ToList())
                {
                    foreach (var open in vm.Desktop.Windows.ToList()) vm.Desktop.Close(open);
                    vm.Desktop.Open(spec.Id);
                    Lay(root);
                    foreach (var rb in RadiosIn(root))
                    {
                        if (!string.IsNullOrEmpty(rb.GroupName)) Fail($"{spec.Id}: radio '{rb.Content}' has GroupName=\"{rb.GroupName}\" (an app-wide group)");
                        if (HostOf(rb) is { } host && !hosts.Any(h => h.Type == host.GetType()))
                            hosts.Add((host.GetType(), host.DataContext));
                    }
                }
                foreach (var open in vm.Desktop.Windows.ToList()) vm.Desktop.Close(open);
                foreach (var rb in RadiosIn(new NewGameWindow()))
                    if (!string.IsNullOrEmpty(rb.GroupName)) Fail($"NewGameWindow: radio '{rb.Name}' has GroupName=\"{rb.GroupName}\"");

                // Click every option in each copy; the two copies must agree with each other (one
                // view model) and each panel of options must show exactly one on.
                void Exercise(string label, System.Windows.Controls.UserControl a, System.Windows.Controls.UserControl b)
                {
                    static List<System.Windows.Controls.RadioButton> Bound(DependencyObject v) => RadiosIn(v)
                        .Where(r => System.Windows.Data.BindingOperations.IsDataBound(r, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)).ToList();
                    var ra = Bound(a); var rbs = Bound(b);
                    if (ra.Count != rbs.Count) { Fail($"{label}: the copies differ ({ra.Count} vs {rbs.Count} radios)"); return; }
                    for (int i = 0; i < ra.Count; i++)
                        foreach (var (click, side) in new[] { (ra, "first"), (rbs, "second") })
                        {
                            var target = click[i];
                            target.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);   // what a click does
                            flips++;
                            for (int j = 0; j < ra.Count; j++)
                                if (ra[j].IsChecked != rbs[j].IsChecked)
                                    Fail($"{label}: after clicking '{target.Content}' in the {side} copy, '{ra[j].Content}' reads {ra[j].IsChecked} vs {rbs[j].IsChecked}");
                            foreach (var copy in new[] { ra, rbs })
                                foreach (var panel in copy.GroupBy(r => LogicalTreeHelper.GetParent(r)))
                                    if (panel.Count() > 1 && panel.Count(r => r.IsChecked == true) != 1)
                                        Fail($"{label}: after clicking '{target.Content}', options [{string.Join("|", panel.Select(r => r.Content))}] have {panel.Count(r => r.IsChecked == true)} on");
                        }
                    Console.WriteLine($"OK    {label}: {ra.Count} bound radios, every one clicked in both copies");
                }

                foreach (var (type, context) in hosts)
                {
                    System.Windows.Controls.UserControl Make()
                    {
                        var v = (System.Windows.Controls.UserControl)Activator.CreateInstance(type)!;
                        v.DataContext = context;
                        return v;
                    }
                    // Two copies with no window (a closed app window is exactly this).
                    var a = Make(); var b = Make();
                    Lay(a); Lay(b);
                    Exercise($"{type.Name} detached x2", a, b);
                    // Two copies in one shown window (the shell is one window).
                    var c = Make(); var d = Make();
                    var grid = new System.Windows.Controls.Grid();
                    grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
                    grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
                    System.Windows.Controls.Grid.SetColumn(d, 1);
                    grid.Children.Add(c); grid.Children.Add(d);
                    var shown = new Window
                    {
                        Content = grid, Width = 1600, Height = 900, Left = -32000, Top = -32000,
                        WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
                    };
                    shown.Show();
                    Lay(grid);
                    Exercise($"{type.Name} one window x2", c, d);
                    shown.Close();
                }
                if (hosts.Count == 0) Fail("no screen with radio buttons found");
            }
            catch (Exception ex)
            {
                Fail(ex.GetType().Name + ": " + ex.Message);
            }
            Console.WriteLine(failures == 0 ? $"RADIOCHECK OK: {flips} clicks, no named radio groups" : $"RADIOCHECK FAIL: {failures} problems");
            Environment.Exit(failures == 0 ? 0 : 1);
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
                // A refresh from elsewhere in the app (here: a contracts change) must
                // keep the card being planned and the matchup being built — and drop
                // only what became invalid, saying so on the screen's status line.
                if (bk.SelectedEvent is { } evRow)
                {
                    var div = bk.Divisions.FirstOrDefault(d => { bk.SelectedDivision = d; return bk.RedOptions.Count >= 4; });
                    if (div is not null)
                    {
                        bk.SelectedRed = bk.RedOptions[1];
                        bk.SelectedBlue = bk.BlueOptions[^1];
                        int rounds = bk.RoundsOptions.First(r => r != bk.SelectedRounds);
                        bk.SelectedRounds = rounds;
                        var planned = bk.NewEventDate.AddDays(40);
                        bk.NewEventDate = planned;
                        var (redId, blueId) = (bk.SelectedRed.Id, bk.SelectedBlue.Id);

                        vm.Contracts.OnChanged?.Invoke();
                        bool kept = bk.SelectedEvent?.Model.Id == evRow.Model.Id && bk.SelectedDivision == div
                                    && bk.SelectedRed?.Id == redId && bk.SelectedBlue?.Id == blueId
                                    && bk.SelectedRounds == rounds && bk.NewEventDate == planned && bk.Tape.Count > 0;
                        Console.WriteLine($"Booking refresh keeps picks: {kept} (event {bk.SelectedEvent?.Name}, {bk.SelectedDivision}, "
                            + $"{bk.SelectedRed?.Display} vs {bk.SelectedBlue?.Display}, {bk.SelectedRounds} rds, date {bk.NewEventDate:d MMM yyyy}) status='{bk.Status}'");
                        if (!kept) throw new InvalidOperationException("a refresh wiped the booking screen's picks");

                        // Now book the blue corner elsewhere on the card behind the screen's
                        // back: the next refresh must drop just that pick, and say so.
                        var other = bk.BlueOptions.First(o => o.Id != blueId).Id;
                        if (!new PlayerBooking().AddBout(vm.LiveWorld!, evRow.Model, blueId, other, rounds, false, out var why))
                            throw new InvalidOperationException("harness could not book the blue corner: " + why);
                        vm.Contracts.OnChanged?.Invoke();
                        bool dropped = bk.SelectedRed?.Id == redId && bk.SelectedBlue is null && bk.StatusIsError
                                       && bk.Status.Contains("no longer available");
                        Console.WriteLine($"Booking refresh drops a booked-elsewhere corner: {dropped} (red still {bk.SelectedRed?.Display}) status='{bk.Status}'");
                        if (!dropped) throw new InvalidOperationException("a refresh did not drop (or did not report) an invalid pick");
                    }
                }

                // Roster: the picked fighter is held by id, so a refresh (rows rebuilt)
                // keeps the same fighter picked in both the card wall and the table.
                if (vm.MyRoster.Fighters.Skip(3).FirstOrDefault() is { } pick)
                {
                    vm.MyRoster.SelectedRow = pick;
                    vm.Contracts.OnChanged?.Invoke();
                    bool rowKept = vm.MyRoster.SelectedRow?.FighterId == pick.FighterId && !ReferenceEquals(vm.MyRoster.SelectedRow, pick);
                    var picked = vm.MyRoster.Cards.OfType<FighterCardModel>().Where(c => c.IsSelected).ToList();
                    bool cardKept = picked.Count == 1 && picked[0].Id == pick.FighterId;
                    Console.WriteLine($"Roster refresh keeps the pick: table {rowKept}, cards {cardKept} ({pick.Name})");
                    if (!rowKept || !cardKept) throw new InvalidOperationException("a refresh lost the roster's picked fighter");
                }

                // Fighters: search reaches ANY fighter in the world, not just a pre-cut top list.
                {
                    var w = vm.LiveWorld!;
                    var everyone = w.Fighters.Values.Where(f => f.Status != MmaManager.Core.Model.FighterStatus.Retired)
                        .OrderByDescending(f => FighterRow.Scouted(w, f)).ThenBy(f => f.Id.Value).ToList();
                    var deep = everyone.Skip(400).Where(f => everyone.Count(o => o.FirstName == f.FirstName && o.LastName == f.LastName) == 1)
                        .Skip(everyone.Count / 3).First();
                    int rank = everyone.IndexOf(deep) + 1;
                    var fp = vm.FightersPage;
                    _ = fp.Rows;   // open the app (builds the index)
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    fp.Search = $"{deep.FirstName} {deep.LastName}";
                    bool found = fp.Rows.Any(r => r.Id == deep.Id.Value) && fp.Cards.OfType<FighterCardModel>().Any(c => c.Id == deep.Id.Value);
                    Console.WriteLine($"Fighters search finds #{rank} by the scout's read ({deep.FullName}): {found} in {sw.ElapsedMilliseconds} ms — {fp.CountLine}");
                    if (!found) throw new InvalidOperationException("Fighters search did not reach a fighter outside the old top 400");
                    fp.Search = "";
                    bool capped = fp.IsCapped && fp.Rows.Count == FightersViewModel.ShowLimit && fp.MatchCount == everyone.Count;
                    Console.WriteLine($"Fighters unfiltered: {fp.CountLine} (capped and counted honestly: {capped})");
                    if (!capped) throw new InvalidOperationException("Fighters does not say how many are shown of how many match");
                }

                // Inbox: a desk decision is answered from inside its letter, through the desk's own command.
                {
                    LetterModel? FindDecisionLetter() => vm.Mail.Letters.Concat(Enumerable.Empty<LetterModel>())
                        .FirstOrDefault(l => l.Actions.Any(a => a.Parameter is MmaManager.Core.Model.Decision));
                    vm.Mail.ShowFolder(MailFolder.Everything);
                    int weeks = 0;
                    while (FindDecisionLetter() is null && weeks < 60) { vm.AdvanceWeekCommand.Execute(null); weeks++; if (vm.IsFightNight) vm.SimTonightCommand.Execute(null); }
                    var letter = FindDecisionLetter();
                    if (letter is null) throw new InvalidOperationException("no desk decision reached the inbox in 60 weeks");
                    var pass = letter.Actions.First(a => a.Text == "Pass");
                    var decision = (MmaManager.Core.Model.Decision)pass.Parameter!;
                    var message = letter.Model;
                    vm.Mail.Selected = letter;
                    pass.Command.Execute(pass.Parameter);
                    bool resolved = !vm.LiveWorld!.Decisions.Any(d => ReferenceEquals(d, decision) && d.State == MmaManager.Core.Model.DecisionState.Pending);
                    var after = vm.Mail.Letters.FirstOrDefault(l => ReferenceEquals(l.Model, message));
                    bool letterSays = after is not null && !after.HasActions && after.ActionNote.Length > 0;
                    bool keptOpen = ReferenceEquals(vm.Mail.Selected?.Model, message);
                    Console.WriteLine($"Inbox decision '{message.Subject}' (after {weeks} weeks): passed from the letter -> resolved {resolved}, " +
                                      $"letter now says '{after?.ActionNote}', still open {keptOpen}");
                    if (!resolved || !letterSays || !keptOpen) throw new InvalidOperationException("the inbox action did not resolve the desk decision");
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

    // Harness helper: CPU time spent on the calling (UI) thread, in ms — steadier than the wall
    // clock when the machine is busy with other work.
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);
    private static double UiCpuMs() { GetThreadTimes(GetCurrentThread(), out _, out _, out long k, out long u); return (k + u) / 10_000.0; }

    // Harness helper: every element of a type under a visual root.
    // Drawn on screen: it and every parent up to the root are Visible. (IsVisible is always false
    // offscreen, where no PresentationSource exists, so the harness can't use it.)
    private static bool Shown(UIElement e, DependencyObject root)
    {
        for (DependencyObject? n = e; n is not null && !ReferenceEquals(n, root); n = VisualTreeHelper.GetParent(n))
            if (n is UIElement u && u.Visibility != Visibility.Visible) return false;
        return true;
    }

    // Harness only (people-and-brands renders): put one sponsor and one network offer on the player's
    // table, with the letters the sim would post for them, so the offer cards, the briefing items and the
    // offer letter have something to draw. Partners are real ladder / archetype names that have logos.
    private static void PlantOffers(MmaManager.Core.Model.World w)
    {
        var p = w.Get(w.PlayerPromotion)!;
        var today = w.CurrentDate;
        string sponsor = new[] { "Summit Bank", "Meridian Airlines", "Atlas Watches", "Polar Cola" }
            .First(n => p.SponsorDeals.All(d => d.Partner != n));
        p.Offers.Add(new MmaManager.Core.Model.DealOffer
        {
            Partner = sponsor, FeePerEvent = new MmaManager.Core.Model.Money(120_000m), Term = 6,
            OfferedOn = today, ExpiresOn = today.AddDays(40),
        });
        MmaManager.Core.Sim.Living.Mail.Post(w, today, MmaManager.Core.Model.MailCategory.General, "Commercial",
            $"Sponsorship offer: {sponsor}", $"{sponsor} wants on the canvas: $120,000/event for 6 events. No strings. Answer on the Finance page before it lapses.");
        p.Offers.Add(new MmaManager.Core.Model.DealOffer
        {
            Media = MmaManager.Core.Model.MediaKind.SpecialtyCable, Partner = "Octagon TV",
            FeePerEvent = new MmaManager.Core.Model.Money(230_000m), Term = 12,
            OfferedOn = today, ExpiresOn = today.AddDays(60),
        });
        Console.WriteLine($"HARNESS planted offers: {sponsor} (sponsor), Octagon TV (specialty cable)");
    }

    // Harness only (player-platform renders): the player's house runs its own streaming platform with the lane held by
    // nothing else, and a streaming giant's offer sits on his desk. Its star clause is set from his own last year of
    // main events so the record either clears it on every card (keeps) or misses about half, with a cadence clause
    // tighter than his pace (misses). The fee is set so the deal beats the platform on a clean record.
    private static void PlantPlatformTrade(MmaManager.Core.Model.World w, bool misses, bool alone = false)
    {
        var p = w.Get(w.PlayerPromotion)!;
        var lane = MmaManager.Core.Model.MediaLane.Streaming;
        if (MmaManager.Core.Sim.Finance.MediaService.DealIn(p, lane) is { } held) p.MediaDeals.Remove(held);
        p.Stream ??= new MmaManager.Core.Model.StreamingPlatform { Name = $"{p.Abbreviation}+", LaunchedOn = w.CurrentDate.AddYears(-2) };
        p.Stream.Subscribers = 240_000;
        if (alone) p.MediaDeals.Clear();   // no other screen: the platform is the only one airing his cards
        p.Offers.RemoveAll(o => o.Media is { } k && MmaManager.Core.Model.MediaLanes.LaneOf(k) == lane);
        var today = w.CurrentDate;
        MmaManager.Core.Model.DealOffer Offer(decimal fee, params MmaManager.Core.Model.DealRequirement[] terms) => new()
        {
            Media = MmaManager.Core.Model.MediaKind.MajorStream, Partner = "Streamflix",
            FeePerEvent = new MmaManager.Core.Model.Money(fee), Term = 24, Requirements = terms.ToList(),
            OfferedOn = today, ExpiresOn = today.AddDays(75),
        };
        var star = MmaManager.Core.Model.DealRequirementKind.MinStarPower;
        var rec0 = MmaManager.Core.Sim.Finance.MediaService.RecordAgainst(w, p, Offer(1m));
        // keeps: the highest bar every recent main event cleared; misses: the lowest bar about half of them miss.
        int bar = misses
            ? Enumerable.Range(0, 101).FirstOrDefault(b => MmaManager.Core.Sim.Finance.MediaService.RecordAgainst(w, p, Offer(1m, new MmaManager.Core.Model.DealRequirement(star, b))).Cleared * 2 <= rec0.Cards, 100)
            : Enumerable.Range(0, 101).Reverse().FirstOrDefault(b => MmaManager.Core.Sim.Finance.MediaService.RecordAgainst(w, p, Offer(1m, new MmaManager.Core.Model.DealRequirement(star, b))).Cleared == rec0.Cards, 0);
        var terms = new List<MmaManager.Core.Model.DealRequirement> { new(star, bar) };
        int pace = rec0.Cards == 0 ? 60 : rec0.Days / rec0.Cards;
        terms.Add(new(MmaManager.Core.Model.DealRequirementKind.EventCadence, misses ? Math.Max(7, pace - 8) : Math.Max(60, pace + 20)));
        // Priced on a clean record: 1.4x the platform when every card clears; 1.2x when half miss (half pay on those),
        // so the missing record's expected pay falls under what the platform nets.
        decimal perUnit = MmaManager.Core.Sim.Finance.MediaService.OfferPerQuarter(w, p, Offer(1_000m)) / 1_000m;
        decimal platform = MmaManager.Core.Sim.Finance.MediaService.PlatformPerQuarter(p);
        decimal fee = perUnit <= 0 || platform <= 0 ? 1_000_000m : Math.Round(platform * (misses ? 1.2m : 1.4m) / perUnit / 10_000m) * 10_000m;
        var offer = Offer(fee, terms.ToArray());
        p.Offers.Add(offer);
        var t = MmaManager.Core.Sim.Finance.MediaService.TradeFor(w, p, offer);
        Console.WriteLine($"HARNESS platform trade ({(misses ? "misses" : "keeps")}): {p.Stream.Name} {p.Stream.Subscribers:N0} subs, Streamflix {offer.FeePerEvent}/ev, "
            + $"star {bar}+, cadence {terms[1].Threshold} d; record {t?.Record.Cards} cards / {t?.Record.Days} d, cleared {t?.Record.Cleared}, "
            + $"keeps cadence {t?.Record.KeepsCadence}; keeps terms {t?.RecordKeepsTerms}; platform {t?.PlatformPerQuarter:N0}/q vs deal {t?.OfferPerQuarter:N0}/q");
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) yield return hit;
            foreach (var deeper in FindAll<T>(child)) yield return deeper;
        }
    }

    // Every radio under root, through both trees: the logical tree reaches panels that are
    // collapsed (never templated, so absent from the visual tree), the visual tree reaches
    // radios generated from item templates. Order is stable for two copies of one screen.
    private static List<System.Windows.Controls.RadioButton> RadiosIn(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var found = new List<System.Windows.Controls.RadioButton>();
        void Walk(DependencyObject d)
        {
            if (!seen.Add(d)) return;
            if (d is System.Windows.Controls.RadioButton rb) found.Add(rb);
            foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(child);
            if (d is Visual or System.Windows.Media.Media3D.Visual3D)
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return found;
    }

    // The screen a radio belongs to: its nearest UserControl.
    private static System.Windows.Controls.UserControl? HostOf(DependencyObject d)
    {
        for (var p = Parent(d); p is not null; p = Parent(p))
            if (p is System.Windows.Controls.UserControl uc) return uc;
        return null;
        static DependencyObject? Parent(DependencyObject x) =>
            LogicalTreeHelper.GetParent(x) ?? (x is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(x) : null);
    }
}
