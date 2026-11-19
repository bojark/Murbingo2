namespace Murbingo.Core;
/// <summary>Stores a stable ticket identity, unique numbers and repeated-number metadata for evaluation and host warnings.</summary>
public sealed record Ticket(string Name, IReadOnlyList<int> Numbers, string Id)
{
    public IReadOnlyList<int> RepeatedNumbers { get; init; } = Array.Empty<int>();
    public bool HasRepeatedNumbers => RepeatedNumbers.Count > 0;
}

/// <summary>Describes one ticket's current matches and invalid numbers while deriving victory from all unique numbers.</summary>
public sealed record Standing(Ticket Ticket, int[] Hits, int[] Invalid)
{
    public bool Winner => Ticket.Numbers.Count > 0 && Hits.Length == Ticket.Numbers.Count;
}

/// <summary>Returns sorted results and only newly reportable events so the interface does not repeat round announcements.</summary>
public sealed record Evaluation(IReadOnlyList<Standing> Ranking, IReadOnlyList<Standing> NewWinners, IReadOnlyList<Standing> NewProblems, IReadOnlyList<Ticket> NewRepeatedTickets);
/// <summary>Owns round rules and ticket evaluation independently of rendering, audio and physical ball positions.</summary>
public sealed class Game
{
    readonly SortedSet<int> remaining = new();
    readonly SortedSet<int> drawn = new();
    readonly HashSet<string> announced = new();
    readonly HashSet<string> warned = new();
    readonly HashSet<string> repeatedWarned = new();
    IReadOnlyList<Ticket> tickets = Array.Empty<Ticket>();
    public int Count { get; private set; }
    public int? Last { get; private set; }
    public IReadOnlyList<int> Remaining => remaining.ToArray();
    public IReadOnlyList<int> Drawn => drawn.ToArray();
    public IReadOnlyList<Ticket> Tickets => tickets;

    /// <summary>Creates the first round through the same validation and reset path used by later restarts.</summary>
    public Game(int count) => Restart(count);
    /// <summary>Wraps a pending two-digit count through the full zero-to-ninety-nine range.</summary>
    public static int StepCounter(int value, int delta) => ((value + delta) % 100 + 100) % 100;
    /// <summary>Rebuilds the numbered pool and clears all round-specific results and warning flags while retaining tickets.</summary>
    public void Restart(int count)
    {
        if (count is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(count));
        Count = count;
        Last = null;
        drawn.Clear();
        remaining.Clear();
        announced.Clear();
        warned.Clear();
        repeatedWarned.Clear();
        for (int i = 1; i <= count; i++)
            remaining.Add(i);
    }

    /// <summary>Uniformly removes exactly one remaining number using cryptographic randomness, regardless of its visual state.</summary>
    public int? Draw()
    {
        if (remaining.Count == 0)
            return null;
        int number = remaining.ElementAt(System.Security.Cryptography.RandomNumberGenerator.GetInt32(remaining.Count));
        remaining.Remove(number);
        drawn.Add(number);
        Last = number;
        return number;
    }

    /// <summary>Installs a successfully parsed ticket set and recalculates it against the already drawn numbers.</summary>
    public Evaluation ReplaceTickets(IReadOnlyList<Ticket> replacement)
    {
        tickets = replacement;
        return Evaluate();
    }

    /// <summary>Calculates unique hits, stable ranking and one-time winner or problem announcements for the current round.</summary>
    public Evaluation Evaluate()
    {
        var standings = tickets.Select(t => new Standing(t, t.Numbers.Where(drawn.Contains).Order().ToArray(), t.Numbers.Where(n => n < 1 || n > Count).Order().ToArray())).ToArray();
        var winners = standings.Where(s => s.Winner && announced.Add(s.Ticket.Id)).ToArray();
        var problems = standings.Where(s => s.Invalid.Length > 0 && warned.Add(s.Ticket.Id)).ToArray();
        var repeated = tickets.Where(t => t.HasRepeatedNumbers && repeatedWarned.Add(t.Id)).ToArray();
        return new(standings.Where(s => s.Hits.Length > 0).OrderByDescending(s => s.Hits.Length).ToArray(), winners, problems, repeated);
    }
}
