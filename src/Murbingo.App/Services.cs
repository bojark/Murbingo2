using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Murbingo.Core;
using Murbingo.Physics;

namespace Murbingo.App;
/// <summary>Represents editable deployment settings with defaults so portable builds can resolve external resources consistently.</summary>
public sealed class Config
{
    public int InitialBallCount { get; set; } = 60;
    public string TicketSource { get; set; } = "examples/tickets.csv";
    public string Language { get; set; } = "ru";
    public string Background { get; set; } = "assets/cabinet.png";
    public string Banner { get; set; } = "assets/banner2.png";
    public string BannerOverlay { get; set; } = "assets/banner-title.png";
    public string BallSprite { get; set; } = "";
    public string EmptyPoolAnimation { get; set; } = "assets/empty-pool-pepe.json";
    public string EmptyPoolMusic { get; set; } = "assets/empty-pool-music.wav";
    public bool SoundEnabled { get; set; } = true;
    public string[] DrawSounds { get; set; } = ["assets/draw-1.wav", "assets/draw-2.wav", "assets/draw-3.wav"];
    public string SoundOnIcon { get; set; } = "assets/sound-on.png";
    public string SoundOffIcon { get; set; } = "assets/sound-off.png";
    public Dictionary<string, string>? Colors { get; set; }
    public double StatisticsFontSize { get; set; } = 28;
    public BallPhysicsSettings? BallPhysics { get; set; } = new();
    public string[] RaptorImages { get; set; } = ["assets/raptor-1.png", "assets/raptor-2.png"];
    public static string Base => AppContext.BaseDirectory;

    /// <summary>Resolves relative paths from the executable and configuration folder rather than the process working directory.</summary>
    public static string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Base, path));
    /// <summary>Deserializes the external configuration while allowing the window to handle an unreadable file with defaults.</summary>
    public static Config Load() => JsonSerializer.Deserialize<Config>(File.ReadAllText(Resolve("config.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new JsonException();
}

/// <summary>Loads external dictionaries and retains embedded Russian text as a reliable per-key fallback.</summary>
public sealed class Locale
{
    readonly Dictionary<string, string> fallback;
    Dictionary<string, string> active = new();
    public string Language { get; private set; } = "ru";

    /// <summary>Reads the embedded Russian dictionary before any external language file is required.</summary>
    public Locale()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Murbingo.App.locales.ru.json")!;
        fallback = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    /// <summary>Loads the chosen language dictionary and falls back without clearing the current round when the file is unavailable.</summary>
    public bool Change(string language)
    {
        Language = language == "en" ? "en" : "ru";
        try
        {
            active = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Config.Resolve($"locales/{Language}.json"))) ?? new();
            return true;
        }
        catch
        {
            active = new();
            return false;
        }
    }

    public string this[string key] => active.GetValueOrDefault(key, fallback.GetValueOrDefault(key, key));
    /// <summary>Formats translated event templates, recovering from malformed external placeholders with the embedded template.</summary>
    public string Format(string key, params object[] args)
    {
        try
        {
            return string.Format(this[key], args);
        }
        catch (FormatException)
        {
            return string.Format(fallback.GetValueOrDefault(key, key), args);
        }
    }
}

/// <summary>Centralizes asynchronous ticket and image input so file and network errors can be handled consistently by the window.</summary>
public static class Sources
{
    static readonly HttpClient client = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = 25 * 1024 * 1024
    };
    /// <summary>Reads a public Google export or supported local document and returns tickets only after complete parsing succeeds.</summary>
    public static async Task<IReadOnlyList<Ticket>> ReadTickets(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        {
            using var response = await client.GetAsync(TicketReader.GoogleCsvUri(source));
            response.EnsureSuccessStatusCode();
            string csv = await response.Content.ReadAsStringAsync();
            if (csv.TrimStart().StartsWith('<'))
                throw new TicketFormatException("notPublic");
            return TicketReader.ParseCsv(csv);
        }

        string file = Config.Resolve(source);
        if (new FileInfo(file).Length > 25 * 1024 * 1024)
            throw new TicketFormatException("tooLarge");
        if (Path.GetExtension(file).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var s = File.OpenRead(file);
            return TicketReader.ParseXlsx(s);
        }

        if (Path.GetExtension(file).Equals(".csv", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(file).Equals(".tsv", StringComparison.OrdinalIgnoreCase))
            return TicketReader.ParseCsv(await File.ReadAllTextAsync(file));
        throw new TicketFormatException("badExtension");
    }

    /// <summary>Loads an image into a frozen memory-backed bitmap so it remains usable without holding the source file open.</summary>
    public static async Task<BitmapImage> Image(string path)
    {
        byte[] data = Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? await client.GetByteArrayAsync(uri) : await File.ReadAllBytesAsync(Config.Resolve(path));
        using var stream = new MemoryStream(data);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
