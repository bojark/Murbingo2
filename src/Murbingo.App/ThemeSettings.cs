namespace Murbingo.App;
// Hex-only configuration, independent of WPF so invalid user values can be tested.
/// <summary>Defines semantic interface colors and validates overrides without depending on WPF or a window.</summary>
public static class ThemeSettings
{
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new Dictionary<string, string>
    {
        ["windowBackground"] = "#38291F",
        ["text"] = "#F1DFC2",
        ["panelBackground"] = "#171F20",
        ["panelBorder"] = "#A98752",
        ["bannerBackground"] = "#152D31",
        ["bannerBorder"] = "#BD9559",
        ["counterBackground"] = "#141B19",
        ["counterText"] = "#F6C66E",
        ["lastBallPlaceholder"] = "#F4CA75",
        ["statisticsText"] = "#F1DFC2",
        ["buttonBackground"] = "#CBA46A",
        ["buttonText"] = "#241D17",
        ["buttonBorder"] = "#E4C893",
        ["buttonHoverBackground"] = "#E4C893",
        ["drawButtonBackground"] = "#329653",
        ["drawButtonHoverBackground"] = "#43B766",
        ["drawButtonText"] = "#FFFFFF",
        ["exitButtonBackground"] = "#BB3838",
        ["exitButtonHoverBackground"] = "#DC5050",
        ["exitButtonText"] = "#FFFFFF",
        ["menuBackground"] = "#FFF7E8",
        ["menuText"] = "#241D17",
        ["logText"] = "#D3C8B2",
        ["logWarning"] = "#F0AC7B",
        ["logError"] = "#FF9990",
        ["logDraw"] = "#F4CC80",
        ["winnerText"] = "#BCECB2",
        ["repeatedWinnerText"] = "#D5AEFF",
        ["rankingScoreText"] = "#E8BB70",
        ["problemText"] = "#EBAB80",
        ["ballNumberText"] = "#000000"
    };

    /// <summary>Merges valid user colors individually so one incorrect value cannot discard the rest of a custom theme.</summary>
    public static IReadOnlyDictionary<string, string> Resolve(IReadOnlyDictionary<string, string>? overrides)
    {
        var result = new Dictionary<string, string>(Defaults);
        if (overrides is null)
            return result;
        foreach (var pair in overrides)
            if (result.ContainsKey(pair.Key) && ValidColor(pair.Value))
                result[pair.Key] = pair.Value;
        return result;
    }

    /// <summary>Accepts only RGB or ARGB hexadecimal strings before they reach the WPF color converter.</summary>
    static bool ValidColor(string? color) => color is not null && color.Length is 7 or 9 && color[0] == '#' && color.AsSpan(1).ToArray().All(Uri.IsHexDigit);
}
