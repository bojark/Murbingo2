using System.IO.Compression;
using Murbingo.Core;

int passed = 0;
// Purpose: Reports a passing regression or throws with the failing behavior so the command exits unsuccessfully on errors.
void Check(bool value, string name)
{
    if (!value)
        throw new Exception("FAIL " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}

// Purpose: Builds a ticket through the real parser so game tests use the same normalization and identity rules as imports.
Ticket T(string name, params int[] numbers) => TicketReader.ParseCsv(name + "," + string.Join(",", numbers))[0];
Check(Game.StepCounter(99, 1) == 0 && Game.StepCounter(0, -1) == 99, "counter wraps in both directions");
for (int round = 0; round < 100; round++)
{
    var game = new Game(99);
    var results = new List<int>();
    while (game.Draw()is int number)
        results.Add(number);
    if (results.Count != 99 || results.Distinct().Count() != 99 || !game.Drawn.SequenceEqual(Enumerable.Range(1, 99)) || game.Remaining.Count != 0 || game.Last != results[^1])
        throw new Exception("draw invariant");
}

Check(true, "100 complete rounds of 99 balls, no duplicates, sorted pools, exhaustion");
var empty = new Game(0);
Check(empty.Draw() == null && empty.Last == null && empty.Remaining.Count == 0, "00 empty round");
var g = new Game(5);
var tickets = new[]
{
    T("First", 1, 2),
    T("Second", 1, 2),
    T("SameName", 3),
    T("SameName", 4)
};
g.ReplaceTickets(tickets);
g.Draw();
g.Restart(5);
Check(g.Drawn.Count == 0 && g.Last == null && g.Remaining.Count == 5 && g.Tickets == tickets && g.Evaluate().Ranking.Count == 0, "restart mid-round retains tickets, resets all results");
g = new Game(1);
g.ReplaceTickets([T("Alice", 1), T("Bob", 1)]);
g.Draw();
var evaluation = g.Evaluate();
Check(evaluation.NewWinners.Count == 2, "simultaneous winners");
Check(g.Evaluate().NewWinners.Count == 0, "winner announced once");
Check(g.ReplaceTickets(TicketReader.ParseCsv("Bob,1\nAlice,1")).NewWinners.Count == 0, "same tickets reloaded and reordered do not repeat announcements");
g.Restart(1);
g.Draw();
Check(g.Evaluate().NewWinners.Count == 2, "new round announces winners again");
g = new Game(5);
while (g.Draw() != null)
{
};
evaluation = g.ReplaceTickets([T("Short", 1, 2, 3), T("Long", 1, 2, 3, 4, 5, 6), T("Tie", 1, 2, 3), T("Problem", 1, 65)]);
Check(evaluation.Ranking.Select(s => s.Ticket.Name).SequenceEqual(new[] { "Long", "Short", "Tie", "Problem" }), "ranking by hits, stable ties, 5 of 6 above 3 of 3");
Check(evaluation.NewProblems.Single(s => s.Ticket.Name == "Problem").Invalid.SequenceEqual(new[] { 65 }) && evaluation.Ranking.Last().Ticket.Numbers.Contains(65), "out-of-pool numbers preserved and flagged");
Check(g.Evaluate().NewProblems.Count == 0, "problem warning once per round");
evaluation = g.ReplaceTickets([T("New problem", 1, 99)]);
Check(evaluation.NewProblems.Count == 1 && evaluation.Ranking[0].Hits.SequenceEqual(new[] { 1 }) && !evaluation.Ranking[0].Winner, "new source recalculates existing draws and flags new problems");
var duplicates = TicketReader.ParseCsv("One,1,1,2,2\nOne,1,2\nTwo,1,2");
Check(duplicates[0].Numbers.Count == 2 && duplicates.Select(t => t.Id).Distinct().Count() == 3, "deduplicate numbers, preserve identical separate tickets");
Check(duplicates[0].RepeatedNumbers.SequenceEqual(new[] { 1, 2 }) && !duplicates[1].HasRepeatedNumbers && !duplicates[2].HasRepeatedNumbers, "record repeated numbers inside a ticket without flagging identical clean sets");
var repeatGame = new Game(8);
var repeatEvaluation = repeatGame.ReplaceTickets(TicketReader.ParseCsv("Repeated,2,5,5,8,2,2\nClean,8,5,2"));
Check(repeatEvaluation.NewRepeatedTickets.Count == 1 && repeatEvaluation.NewRepeatedTickets[0].Name == "Repeated" && repeatEvaluation.NewRepeatedTickets[0].RepeatedNumbers.SequenceEqual(new[] { 2, 5 }) && repeatEvaluation.Ranking.Count == 0, "repeated-number warning during initial check before any matches");
Check(repeatGame.Evaluate().NewRepeatedTickets.Count == 0 && repeatGame.ReplaceTickets(TicketReader.ParseCsv("Clean,2,5,8\nRepeated,8,2,5,2,5")).NewRepeatedTickets.Count == 0, "repeated-number warning once per round including reordered reload");
while (repeatGame.Draw() != null)
{
}

repeatEvaluation = repeatGame.Evaluate();
Check(repeatEvaluation.NewWinners.Count == 2 && repeatEvaluation.Ranking.All(s => s.Winner && s.Hits.Length == 3 && s.Ticket.Numbers.Count == 3) && repeatEvaluation.NewWinners.Count(s => s.Ticket.HasRepeatedNumbers) == 1, "repeated numbers count once and winning status retains the marker");
Check(repeatGame.Evaluate().NewWinners.Count == 0 && repeatGame.ReplaceTickets(TicketReader.ParseCsv("Repeated,2,2,5,8\nClean,2,5,8")).NewWinners.Count == 0, "reload does not reannounce marked winners");
repeatGame.Restart(8);
Check(repeatGame.Evaluate().NewRepeatedTickets.Count == 1 && repeatGame.Evaluate().NewRepeatedTickets.Count == 0, "restart resets repeated-number warning flags");
repeatGame = new Game(8);
repeatGame.ReplaceTickets(TicketReader.ParseCsv("Player,2,5,8"));
repeatEvaluation = repeatGame.ReplaceTickets(TicketReader.ParseCsv("Player,2,5,8,5"));
Check(repeatEvaluation.NewRepeatedTickets.Count == 1 && repeatGame.ReplaceTickets(TicketReader.ParseCsv("New player,1,1")).NewRepeatedTickets.Count == 1, "new repeats and new tickets are checked during replacement");
repeatEvaluation = repeatGame.ReplaceTickets(TicketReader.ParseCsv("Outside,1,65,65"));
Check(repeatEvaluation.NewProblems.Count == 1 && repeatEvaluation.NewRepeatedTickets.Count == 1 && repeatEvaluation.NewRepeatedTickets[0].Numbers.Contains(65), "repeated and out-of-pool warnings coexist without losing numbers");
Check(TicketReader.ParseCsv("\uFEFF\"Last, First\",2,1\r\n\"Quoted \"\"name\"\"\",3,4")[1].Name == "Quoted \"name\"", "CSV UTF-8 BOM, quotes, comma and CRLF");
Check(TicketReader.ParseCsv("Name\t2\t1")[0].Numbers.SequenceEqual(new[] { 1, 2 }) && TicketReader.ParseCsv("Name;2;1")[0].Numbers.Count == 2, "TSV and semicolon delimiter");
foreach (string invalid in new[]
{
    "Name,1,wrong",
    "Name",
    ",1",
    "\"Name,1",
    ""
}

)
{
    var previous = g.Tickets;
    try
    {
        g.ReplaceTickets(TicketReader.ParseCsv(invalid));
        throw new Exception("Accepted malformed CSV");
    }
    catch (TicketFormatException)
    {
        if (g.Tickets != previous)
            throw new Exception("Tickets lost");
    }
}

Check(true, "malformed documents fail atomically without losing previous tickets");
var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Murbingo.App/examples/tickets.xlsx"));
using (var stream = File.OpenRead(sample))
    Check(TicketReader.ParseXlsx(stream).Count == 5, "sample XLSX import");
using (var mem = new MemoryStream())
{
    using (var zip = new ZipArchive(mem, ZipArchiveMode.Create, true))
    {
        // Purpose: Writes a small XML ZIP part for dependency-free XLSX parser regression fixtures.
        void Entry(string name, string xml)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(xml);
        }

        Entry("xl/workbook.xml", "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet r:id='id5'/></sheets></workbook>");
        Entry("xl/_rels/workbook.xml.rels", "<Relationships><Relationship Id='id5' Target='/xl/worksheets/custom.xml'/></Relationships>");
        Entry("xl/sharedStrings.xml", "<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><r><t>Player</t></r><r><t> name</t></r></si></sst>");
        Entry("xl/worksheets/custom.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row><c r='A1' t='s'><v>0</v></c><c r='C1'><v>2</v></c><c r='E1'><f>1+2</f><v>3</v></c></row></sheetData></worksheet>");
    }

    mem.Position = 0;
    var t = TicketReader.ParseXlsx(mem)[0];
    Check(t.Name == "Player name" && t.Numbers.SequenceEqual(new[] { 2, 3 }), "XLSX shared strings, rich text, sparse cells, cached formulas, relationship paths");
}

Check(TicketReader.GoogleCsvUri("https://docs.google.com/spreadsheets/d/abc123/edit?gid=123#gid=123").ToString() == "https://docs.google.com/spreadsheets/d/abc123/export?format=csv&gid=123", "Google URL export and sheet gid");
foreach (double width in new[]
{
    120d,
    200,
    320,
    480,
    1024
}

)
    foreach (double height in new[]
    {
        60d,
        90,
        96,
        140,
        216
    }

    )
    {
        var path = Murbingo.App.BallRoll.ForViewport(width, height);
        double radius = (Math.Min(width, height) - 8) / 2;
        if (width / 2 + radius + path.StartX + 2 >= 0)
            throw new Exception("Rolling ball is visible before entering the left edge");
        if (width / 2 - radius < 0 || width / 2 + radius > width)
            throw new Exception("Rolling ball ends outside its window");
        if (!double.IsFinite(path.StartAngle) || path.StartAngle >= 0)
            throw new Exception("Invalid rolling rotation");
    }

Check(true, "rolling entrance starts fully outside and ends inside for 25 viewport sizes");
var defaultTheme = Murbingo.App.ThemeSettings.Resolve(null);
Check(defaultTheme.Count == Murbingo.App.ThemeSettings.Defaults.Count && defaultTheme["exitButtonBackground"] == "#BB3838" && defaultTheme["drawButtonBackground"] == "#329653", "older configuration without colors retains complete theme and red/green actions");
var customTheme = Murbingo.App.ThemeSettings.Resolve(new Dictionary<string, string> { ["text"] = "#123456", ["panelBackground"] = "#80112233", ["exitButtonBackground"] = "invalid", ["logError"] = null!, ["unknownColor"] = "#FFFFFF" });
Check(customTheme["text"] == "#123456" && customTheme["panelBackground"] == "#80112233" && customTheme["exitButtonBackground"] == defaultTheme["exitButtonBackground"] && customTheme["logError"] == defaultTheme["logError"] && !customTheme.ContainsKey("unknownColor"), "partial theme accepts RGB/ARGB and falls back per invalid field");
Check(defaultTheme["text"] == Murbingo.App.ThemeSettings.Defaults["text"] && customTheme["drawButtonBackground"] == defaultTheme["drawButtonBackground"] && customTheme["repeatedWinnerText"] == defaultTheme["repeatedWinnerText"], "customizing a theme preserves unrelated semantic colors and defaults");
PhysicsChecks.Run(Check);
AudioChecks.Run(Check);
var zeroRound = new Game(0);
Check(Murbingo.App.EmptyPoolAnimationState.ShouldShow(zeroRound.Count) && zeroRound.Draw() == null, "zero-ball round enables the easter egg and cannot draw a ball");
var exhaustedRound = new Game(1);
exhaustedRound.Draw();
Check(!Murbingo.App.EmptyPoolAnimationState.ShouldShow(exhaustedRound.Count) && exhaustedRound.Remaining.Count == 0, "exhausted ordinary round does not enable the zero-ball easter egg");
Check(Murbingo.App.EmptyPoolAnimationState.FrameIndex(0, 41, 20) == 0 && Murbingo.App.EmptyPoolAnimationState.FrameIndex(.05, 41, 20) == 1 && Murbingo.App.EmptyPoolAnimationState.FrameIndex(2.05, 41, 20) == 0, "Pepe frame clock advances and loops at the intended duration");
Check(Murbingo.App.EmptyPoolAnimationState.FrameIndex(double.NaN, 41, 20) == 0 && Murbingo.App.EmptyPoolAnimationState.FrameIndex(-1, 41, 20) == 0 && Murbingo.App.EmptyPoolAnimationState.FrameIndex(1, 0, 20) == 0, "invalid animation clock values safely select the first frame");
var spriteDefinition = new Murbingo.App.SpriteSheetDefinition
{
    Sheet = "empty-pool-pepe.png",
    FrameWidth = 192,
    FrameHeight = 260,
    Columns = 7,
    FrameCount = 41,
    Fps = 20
};
Check(spriteDefinition.Fits(1344, 1560) && !(spriteDefinition with { FrameCount = 43 }).Fits(1344, 1560) && !(spriteDefinition with { Fps = 0 }).Fits(1344, 1560), "sprite metadata must fit the atlas and contain a valid playback rate");
Console.WriteLine($"ALL {passed} CHECKS PASSED");
