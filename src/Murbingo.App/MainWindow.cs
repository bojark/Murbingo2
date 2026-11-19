using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Murbingo.Core;

namespace Murbingo.App;
/// <summary>Coordinates host controls, localization, artwork, audio and rendering while the game module owns round rules.</summary>
public sealed class MainWindow : Window
{
    readonly Locale locale = new();
    Config config = new();
    Game game = new(60);
    int counter = 60, pose;
    readonly List<(DateTime Time, string Key, object[] Args, string Kind)> entries = new();
    readonly List<Action> translations = new();
    readonly TextBlock counterText = new(), lastText = new(), status = new(), upperTitle = new(), lowerTitle = new(), ratingEmpty = new();
    readonly TextBlock counterOnes = new();
    readonly LastBall lastBall = new();
    readonly EmptyPoolAnimation emptyPoolAnimation = new()
    {
        Visibility = Visibility.Collapsed
    };
    readonly System.Windows.Threading.DispatcherTimer poseReturn = new()
    {
        Interval = TimeSpan.FromMilliseconds(600)
    };
    readonly RotateTransform raptorRotation = new(-4);
    readonly StackPanel logs = new(), rankings = new();
    readonly ScrollViewer logScroll = new();
    readonly BallPanel upper = new()
    {
        Chaotic = true
    }, lower = new();
    readonly Button draw = new(), load = new();
    readonly Image raptor = new();
    readonly Grid shell = new();
    readonly Canvas bannerCanvas = new();
    readonly Image bannerA = new(), bannerB = new();
    readonly TextBlock bannerFallback = new();
    readonly Image bannerOverlay = new()
    {
        Stretch = Stretch.Uniform,
        Margin = new(12, 8, 12, 8),
        IsHitTestVisible = false
    };
    BitmapSource[] raptorPoses = [];
    bool loading;
    readonly Dictionary<string, SolidColorBrush> theme = new();
    readonly Button soundToggle = new();
    readonly Image soundIcon = new()
    {
        Width = 32,
        Height = 32,
        Stretch = Stretch.Uniform
    };
    BitmapImage? soundOnIcon, soundOffIcon;
    readonly DrawSoundPlayer audio;
    Task soundReady = Task.CompletedTask;
    /// <summary>Loads configuration, builds the themed window, connects lifetime events and schedules independent asset imports.</summary>
    public MainWindow()
    {
        Width = 1360;
        Height = 860;
        MinWidth = 1060;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        Background = Brush("#38291F");
        Foreground = Brush("#F1DFC2");
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/assets/app.ico"));
        string? configError = null;
        try
        {
            config = Config.Load();
            if (config.InitialBallCount is < 0 or > 99)
            {
                config.InitialBallCount = Math.Clamp(config.InitialBallCount, 0, 99);
                configError = "configRange";
            }
        }
        catch
        {
            configError = "configFallback";
        }

        audio = new DrawSoundPlayer(config.SoundEnabled, () => Log("soundPlaybackError", "warn"));
        foreach (var color in ThemeSettings.Resolve(config.Colors))
        {
            var brush = Brush(color.Value);
            brush.Freeze();
            theme[color.Key] = brush;
            Resources[color.Key] = brush;
        }

        Background = Color("windowBackground");
        Foreground = Color("text");
        upper.NumberBrush = lower.NumberBrush = lastBall.NumberBrush = Color("ballNumberText");
        bool localeOk = locale.Change(config.Language);
        game = new(config.InitialBallCount);
        counter = game.Count;
        Build();
        upper.ConfigurePhysics(config.BallPhysics);
        upper.Reset(game.Count);
        lower.Reset(game.Count);
        Translate();
        RefreshRound();
        poseReturn.Tick += (_, _) =>
        {
            poseReturn.Stop();
            pose = 0;
            SetPose();
        };
        Closed += (_, _) =>
        {
            poseReturn.Stop();
            upper.ConfigurePhysics(new() { Enabled = false });
            audio.Dispose();
            emptyPoolAnimation.Dispose();
        };
        Log("started", "info");
        LogEmptyPool();
        if (configError != null)
            Log(configError, "warn");
        if (!localeOk)
            Log("localeFallback", "warn");
        Loaded += async (_, _) =>
        {
            if (Environment.GetCommandLineArgs().Contains("--self-test"))
            {
                await LoadImages();
                await UiVerification.Run(this);
            }
            else
            {
                soundReady = audio.LoadAsync(config.DrawSounds ?? [], path => Log("soundUnavailable", "warn", path), config.EmptyPoolMusic);
                await Task.WhenAll(LoadImages(), LoadTickets(config.TicketSource), soundReady);
            }
        };
    }

    /// <summary>Converts a validated hexadecimal color into a WPF brush for configured colors and fallback artwork.</summary>
    static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    /// <summary>Returns a cached semantic theme brush so visual elements consistently use the configured palette.</summary>
    SolidColorBrush Color(string key) => theme[key];
    /// <summary>Creates a localizable label and records its update action so switching languages preserves round state.</summary>
    TextBlock Label(string key, double size = 14)
    {
        var text = new TextBlock
        {
            FontSize = size,
            TextWrapping = TextWrapping.Wrap
        };
        translations.Add(() => text.Text = locale[key]);
        return text;
    }

    /// <summary>Connects a host action, localized tooltip, automation identifier and special draw or exit colors.</summary>
    void BindButton(Button b, string key, string tip, Action action)
    {
        if (key is "draw" or "exit")
        {
            b.Background = Color(key + "ButtonBackground");
            b.Foreground = Color(key + "ButtonText");
            b.Resources["buttonHoverBackground"] = Color(key + "ButtonHoverBackground");
        }

        translations.Add(() =>
        {
            b.Content = locale[key];
            b.ToolTip = locale[tip];
        });
        b.Click += (_, _) => action();
        System.Windows.Automation.AutomationProperties.SetAutomationId(b, key);
    }

    /// <summary>Creates a consistently configured action button through the shared binding helper.</summary>
    Button Button(string key, string tip, Action action)
    {
        var b = new Button();
        BindButton(b, key, tip, action);
        return b;
    }

    /// <summary>Places a child into a grid cell to keep programmatic layout construction concise and consistent.</summary>
    static void At(Grid grid, UIElement child, int row, int column = 0)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    /// <summary>Wraps content in the themed opaque panel used to keep dynamic elements readable over the background texture.</summary>
    Border Panel(UIElement child) => new()
    {
        Child = child,
        Background = Color("panelBackground"),
        BorderBrush = Color("panelBorder"),
        BorderThickness = new(1),
        CornerRadius = new(8),
        Padding = new(10),
        Margin = new(5)
    };
    /// <summary>Constructs the resizable host layout, including controls, independent lists, ball viewports and the footer.</summary>
    void Build()
    {
        Content = shell;
        shell.RowDefinitions.Add(new() { Height = new(104) });
        shell.RowDefinitions.Add(new());
        shell.RowDefinitions.Add(new() { Height = new(30) });
        var banner = new Border
        {
            ClipToBounds = true,
            Background = Color("bannerBackground"),
            BorderBrush = Color("bannerBorder"),
            BorderThickness = new(0, 0, 0, 3)
        };
        var bannerGrid = new Grid();
        bannerGrid.Children.Add(bannerCanvas);
        bannerCanvas.Children.Add(bannerA);
        bannerCanvas.Children.Add(bannerB);
        bannerFallback.FontSize = 33;
        bannerFallback.FontFamily = new("Georgia");
        bannerFallback.HorizontalAlignment = HorizontalAlignment.Center;
        bannerFallback.VerticalAlignment = VerticalAlignment.Center;
        bannerGrid.Children.Add(bannerFallback);
        // A sibling of the moving canvas stays fixed above every scrolling tile.
        bannerGrid.Children.Add(bannerOverlay);
        translations.Add(() => bannerFallback.Text = locale["title"] + "  •  " + locale["onAir"]);
        banner.Child = bannerGrid;
        At(shell, banner, 0);
        banner.SizeChanged += (_, _) => AnimateBanner();
        var body = new Grid
        {
            Margin = new(14, 10, 14, 2)
        };
        body.ColumnDefinitions.Add(new() { Width = new(.95, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = new(1.22, GridUnitType.Star) });
        At(shell, body, 1);
        var left = new Grid();
        left.RowDefinitions.Add(new() { Height = new(121) });
        left.RowDefinitions.Add(new());
        left.RowDefinitions.Add(new() { Height = GridLength.Auto });
        At(body, left, 0);
        var controls = new Grid
        {
            Margin = new(2, 4, 2, 8)
        };
        controls.ColumnDefinitions.Add(new() { Width = new(155) });
        controls.ColumnDefinitions.Add(new());
        controls.ColumnDefinitions.Add(new());
        controls.RowDefinitions.Add(new());
        controls.RowDefinitions.Add(new() { Height = new(26) });
        At(left, controls, 0);
        var dial = new Grid
        {
            Margin = new(3)
        };
        dial.ColumnDefinitions.Add(new() { Width = new(35) });
        dial.ColumnDefinitions.Add(new());
        dial.ColumnDefinitions.Add(new());
        var arrows = new Grid();
        arrows.RowDefinitions.Add(new());
        arrows.RowDefinitions.Add(new());
        var up = Button("up", "counterTip", () => Step(1));
        var down = Button("down", "counterTip", () => Step(-1));
        up.Padding = down.Padding = new(3);
        At(arrows, up, 0);
        At(arrows, down, 1);
        At(dial, arrows, 0);
        int digitColumn = 1;
        foreach (var digit in new[]
        {
            counterText,
            counterOnes
        }

        )
        {
            digit.FontFamily = new("Consolas");
            digit.FontSize = 45;
            digit.Foreground = Color("counterText");
            digit.VerticalAlignment = VerticalAlignment.Center;
            digit.HorizontalAlignment = HorizontalAlignment.Center;
            At(dial, new Border { Child = digit, Background = Color("counterBackground"), BorderBrush = Color("panelBorder"), BorderThickness = new(2), CornerRadius = new(5), Margin = new(2, 0, 2, 0) }, 0, digitColumn++);
        }

        At(controls, dial, 0);
        var countLabel = Label("ballCount", 12);
        countLabel.TextAlignment = TextAlignment.Center;
        At(controls, countLabel, 1);
        At(controls, Button("restart", "restartTip", Restart), 0, 1);
        var restartHint = Label("restartHint", 11);
        restartHint.TextAlignment = TextAlignment.Center;
        At(controls, restartHint, 1, 1);
        BindButton(draw, "draw", "drawTip", Draw);
        At(controls, draw, 0, 2);
        var drawHint = Label("drawHint", 11);
        drawHint.TextAlignment = TextAlignment.Center;
        At(controls, drawHint, 1, 2);
        var lists = new Grid();
        lists.ColumnDefinitions.Add(new());
        lists.ColumnDefinitions.Add(new());
        At(left, lists, 1);
        var logPanel = new DockPanel();
        var logHeading = Label("log", 17);
        logHeading.Margin = new(0, 0, 0, 10);
        DockPanel.SetDock(logHeading, Dock.Top);
        logPanel.Children.Add(logHeading);
        logScroll.Content = logs;
        logScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        logPanel.Children.Add(logScroll);
        At(lists, Panel(logPanel), 0);
        var ratingPanel = new DockPanel();
        var heading = Label("rating", 17);
        heading.Margin = new(0, 0, 0, 10);
        DockPanel.SetDock(heading, Dock.Top);
        ratingPanel.Children.Add(heading);
        ratingPanel.Children.Add(new ScrollViewer { Content = rankings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        At(lists, Panel(ratingPanel), 0, 1);
        var bottom = new Grid();
        bottom.ColumnDefinitions.Add(new());
        bottom.ColumnDefinitions.Add(new() { Width = new(86) });
        bottom.ColumnDefinitions.Add(new() { Width = new(46) });
        bottom.ColumnDefinitions.Add(new());
        At(left, bottom, 2);
        At(bottom, Button("clear", "clearTip", () =>
        {
            entries.Clear();
            RenderLogs();
        }), 0);
        At(bottom, Button("exit", "exitTip", Close), 0, 1);
        soundToggle.Content = soundIcon;
        soundToggle.Padding = new(3);
        soundToggle.Background = Color("panelBackground");
        // Keep the supplied transparent icon free of the generic button and focus outlines.
        soundToggle.Template = (ControlTemplate)FindResource("SoundToggleTemplate");
        soundToggle.BorderThickness = new(0);
        soundToggle.FocusVisualStyle = null;
        soundToggle.Click += (_, _) =>
        {
            bool enabled = audio.Toggle();
            UpdateSoundButton();
            Log(enabled ? "soundEnabledLog" : "soundMutedLog", "info");
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(soundToggle, "soundToggle");
        translations.Add(UpdateSoundButton);
        At(bottom, soundToggle, 0, 2);
        BindButton(load, "load", "loadTip", ShowLoadMenu);
        At(bottom, load, 0, 3);
        var footer = new Grid
        {
            Margin = new(12, 0, 12, 0)
        };
        footer.ColumnDefinitions.Add(new());
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var footerFrame = new Border
        {
            Child = footer,
            Background = Color("panelBackground"),
            BorderBrush = Color("panelBorder"),
            BorderThickness = new(1, 1, 1, 0),
            CornerRadius = new(5, 5, 0, 0),
            Margin = new(19, 0, 19, 0)
        };
        At(shell, footerFrame, 2);
        var language = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var languageLabel = Label("language", 12);
        languageLabel.VerticalAlignment = VerticalAlignment.Center;
        language.Children.Add(languageLabel);
        var lang = new ComboBox
        {
            Width = 130,
            Margin = new(8, 1, 3, 1),
            ItemsSource = new[]
            {
                "Русский",
                "English"
            },
            SelectedIndex = locale.Language == "en" ? 1 : 0
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(lang, "language");
        lang.SelectionChanged += (_, _) => ChangeLanguage(lang.SelectedIndex == 1 ? "en" : "ru");
        language.Children.Add(lang);
        At(footer, language, 0);
        var right = new Grid
        {
            Margin = new(7, 0, 0, 0)
        };
        right.RowDefinitions.Add(new());
        right.RowDefinitions.Add(new() { Height = new(108) });
        right.RowDefinitions.Add(new());
        At(body, right, 0, 1);
        var upperContainer = new DockPanel();
        upperTitle.FontSize = 15;
        DockPanel.SetDock(upperTitle, Dock.Top);
        upperContainer.Children.Add(upperTitle);
        var upperView = new Grid
        {
            ClipToBounds = true
        };
        upperView.Children.Add(upper);
        upperView.Children.Add(emptyPoolAnimation);
        upperContainer.Children.Add(upperView);
        At(right, Panel(upperContainer), 0);
        var display = new Grid();
        display.ColumnDefinitions.Add(new() { Width = new(1.7, GridUnitType.Star) });
        display.ColumnDefinitions.Add(new());
        var ballDisplay = new Grid
        {
            ClipToBounds = true,
            Margin = new(3)
        };
        lastText.FontFamily = new("Consolas");
        lastText.FontSize = 64;
        lastText.FontWeight = FontWeights.Bold;
        lastText.TextAlignment = TextAlignment.Center;
        lastText.VerticalAlignment = VerticalAlignment.Center;
        lastText.Foreground = Color("lastBallPlaceholder");
        ballDisplay.Children.Add(lastText);
        ballDisplay.Children.Add(lastBall);
        var ballFrame = Panel(ballDisplay);
        ballFrame.Padding = new(0);
        ballFrame.ClipToBounds = true;
        At(display, ballFrame, 0);
        status.FontSize = double.IsFinite(config.StatisticsFontSize) ? Math.Clamp(config.StatisticsFontSize, 12, 64) : 28;
        status.Foreground = Color("statisticsText");
        status.FontWeight = FontWeights.SemiBold;
        status.TextWrapping = TextWrapping.NoWrap;
        var statisticsFrame = Panel(new Viewbox { Child = status, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
        statisticsFrame.Padding = new(8);
        At(display, statisticsFrame, 0, 1);
        At(right, display, 1);
        var lowerContainer = new DockPanel();
        lowerTitle.FontSize = 15;
        DockPanel.SetDock(lowerTitle, Dock.Top);
        lowerContainer.Children.Add(lowerTitle);
        var lowerGrid = new Grid();
        lowerGrid.ColumnDefinitions.Add(new());
        lowerGrid.ColumnDefinitions.Add(new() { Width = new(120) });
        At(lowerGrid, lower, 0);
        raptor.Stretch = Stretch.Uniform;
        raptor.VerticalAlignment = VerticalAlignment.Bottom;
        raptor.Margin = new(6);
        At(lowerGrid, raptor, 0, 1);
        lowerContainer.Children.Add(lowerGrid);
        At(right, Panel(lowerContainer), 2);
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!;
        At(footer, new TextBlock { Text = $"v{version.Major}.{version.Minor}.{version.Build}   •   Ivan \"komurka\" K.", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }, 0, 1);
    }

    /// <summary>Splits the pending ball count into separate tens and units displays without applying a restart.</summary>
    void UpdateCounter()
    {
        counterText.Text = (counter / 10).ToString();
        counterOnes.Text = (counter % 10).ToString();
    }

    /// <summary>Wraps the pending count through zero and ninety-nine and logs the host change without altering the active round.</summary>
    void Step(int delta)
    {
        counter = Game.StepCounter(counter, delta);
        UpdateCounter();
        Log("counterChanged", "info", counter.ToString("D2"));
    }

    /// <summary>Resets round and visual state while retaining imported tickets, session mute state and the event log.</summary>
    void Restart()
    {
        game.Restart(counter);
        poseReturn.Stop();
        pose = 0;
        SetPose();
        upper.Reset(game.Count);
        lower.Reset(game.Count);
        rankings.Children.Clear();
        Log("restarted", "info", game.Count);
        LogEmptyPool();
        RefreshRound();
    }

    /// <summary>Performs one valid game draw, then starts raptor and sound feedback and refreshes ranking and ball animation.</summary>
    void Draw()
    {
        var number = game.Draw();
        if (number == null)
            return;
        Log("drawn", "draw", number);
        poseReturn.Stop();
        pose = 1;
        SetPose();
        poseReturn.Start();
        _ = PlayDrawSound();
        RenderEvaluation(game.Evaluate());
        RefreshRound();
        AnimateLastBall();
    }

    /// <summary>Records the zero-ball easter egg once at round creation, never on language or view refreshes.</summary>
    void LogEmptyPool()
    {
        if (EmptyPoolAnimationState.ShouldShow(game.Count))
            Log("emptyPoolLog", "info");
    }

    /// <summary>Updates the supplied icon and localized action hint without changing game state.</summary>
    void UpdateSoundButton()
    {
        soundIcon.Source = audio.State.Enabled ? soundOnIcon : soundOffIcon;
        string hint = locale[audio.State.Enabled ? "muteSound" : "unmuteSound"];
        soundToggle.ToolTip = hint;
        System.Windows.Automation.AutomationProperties.SetName(soundToggle, hint);
    }

    /// <summary>Waits for preloading and reports playback failures instead of interrupting a draw.</summary>
    async Task PlayDrawSound()
    {
        try
        {
            await soundReady;
            await audio.PlayDrawAsync();
        }
        catch
        {
            Log("soundPlaybackError", "warn");
        }
    }

    /// <summary>Copies authoritative round values into controls and pool views without making another draw or restarting physics.</summary>
    void RefreshRound()
    {
        emptyPoolAnimation.Visibility = EmptyPoolAnimationState.ShouldShow(game.Count) ? Visibility.Visible : Visibility.Collapsed;
        audio.SetEmptyPoolActive(EmptyPoolAnimationState.ShouldShow(game.Count));
        UpdateCounter();
        lastText.Text = "—";
        lastText.Visibility = game.Last.HasValue ? Visibility.Collapsed : Visibility.Visible;
        lastBall.SetNumber(game.Last);
        draw.IsEnabled = game.Remaining.Count > 0;
        upper.SetNumbers(game.Remaining);
        lower.SetNumbers(game.Drawn);
        upperTitle.Text = locale.Format("upper", game.Remaining.Count);
        lowerTitle.Text = locale.Format("lower", game.Drawn.Count);
        status.Text = locale.Format("status", game.Drawn.Count, game.Count, game.Tickets.Count);
        if (rankings.Children.Count == 0)
        {
            ratingEmpty.Text = locale["ratingEmpty"];
            ratingEmpty.TextWrapping = TextWrapping.Wrap;
            ratingEmpty.Opacity = .6;
            rankings.Children.Add(ratingEmpty);
        }
    }

    /// <summary>Logs newly detected conditions and rebuilds ranking with distinct markers for winners and repeated-number tickets.</summary>
    void RenderEvaluation(Evaluation evaluation)
    {
        foreach (var s in evaluation.NewProblems)
            Log("problemLog", "warn", s.Ticket.Name, string.Join(", ", s.Invalid));
        foreach (var ticket in evaluation.NewRepeatedTickets)
            Log("repeatedLog", "warn", ticket.Name, string.Join(", ", ticket.RepeatedNumbers));
        foreach (var s in evaluation.NewWinners)
            Log(s.Ticket.HasRepeatedNumbers ? "winnerRepeatedLog" : "winnerLog", s.Ticket.HasRepeatedNumbers ? "repeatWin" : "win", s.Ticket.Name);
        rankings.Children.Clear();
        foreach (var s in evaluation.Ranking)
        {
            var row = new StackPanel
            {
                Margin = new(2, 6, 2, 8)
            };
            bool repeatedWinner = s.Winner && s.Ticket.HasRepeatedNumbers;
            string winnerColor = repeatedWinner ? "repeatedWinnerText" : "winnerText";
            var name = new TextBlock
            {
                Text = (repeatedWinner ? "* " : "") + s.Ticket.Name,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.Bold,
                Foreground = Color(s.Winner ? winnerColor : "text")
            };
            if (s.Winner)
                name.BeginAnimation(OpacityProperty, new DoubleAnimation(.5, 1, TimeSpan.FromSeconds(1.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            row.Children.Add(name);
            row.Children.Add(new TextBlock { Text = locale.Format("score", s.Hits.Length, s.Ticket.Numbers.Count), Foreground = Color(repeatedWinner ? winnerColor : "rankingScoreText"), FontSize = 18 });
            row.Children.Add(new TextBlock { Text = string.Join(" · ", s.Hits), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            if (s.Ticket.HasRepeatedNumbers)
                row.Children.Add(new TextBlock { Text = locale.Format("repeated", string.Join(", ", s.Ticket.RepeatedNumbers)), TextWrapping = TextWrapping.Wrap, Foreground = Color(repeatedWinner ? winnerColor : "problemText"), FontSize = 11 });
            if (s.Invalid.Length > 0)
                row.Children.Add(new TextBlock { Text = locale.Format("problem", string.Join(", ", s.Invalid)), TextWrapping = TextWrapping.Wrap, Foreground = Color("problemText"), FontSize = 11 });
            if (s.Winner)
                row.Children.Add(new TextBlock { Text = locale[repeatedWinner ? "winnerRepeated" : "winner"], Foreground = Color(winnerColor), FontSize = 12 });
            rankings.Children.Add(row);
        }
    }

    /// <summary>Reloads localized text and redraws current results while preserving all round and announcement state.</summary>
    void ChangeLanguage(string language)
    {
        bool ok = locale.Change(language);
        Translate();
        RenderEvaluation(game.Evaluate());
        RefreshRound();
        Log("languageChanged", "info", language);
        if (!ok)
            Log("localeFallback", "warn");
    }

    /// <summary>Reapplies registered label translations and regenerates existing log text in the selected language.</summary>
    void Translate()
    {
        Title = locale["title"];
        foreach (var action in translations)
            action();
        RenderLogs();
        RefreshRound();
    }

    /// <summary>Stores a timestamped event template and arguments so past entries can be translated later.</summary>
    void Log(string key, string kind, params object[] args)
    {
        entries.Add((DateTime.Now, key, args, kind));
        AppendLog(entries[^1]);
        logScroll.ScrollToEnd();
    }

    /// <summary>Carries nested localization keys for error details so old log messages also change language correctly.</summary>
    sealed record LocalText(string Key, object[] Args);
    /// <summary>Formats one stored event and applies its semantic color without changing any game data.</summary>
    void AppendLog((DateTime Time, string Key, object[] Args, string Kind) e)
    {
        logs.Children.Add(new TextBlock { Text = $"{e.Time:HH:mm:ss}  {locale.Format(e.Key, e.Args.Select(a => a is LocalText t ? (object)locale.Format(t.Key, t.Args) : a).ToArray())}", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 6, 9), FontSize = 12, Foreground = Color(e.Kind switch
        {
            "warn" => "logWarning",
            "error" => "logError",
            "win" => "winnerText",
            "repeatWin" => "repeatedWinnerText",
            "draw" => "logDraw",
            _ => "logText"
        }) });
    }

    /// <summary>Rebuilds the visible log from stored events after clearing or changing the interface language.</summary>
    void RenderLogs()
    {
        logs.Children.Clear();
        foreach (var e in entries)
            AppendLog(e);
        logScroll.ScrollToEnd();
    }

    /// <summary>Offers the configured source and a local file picker while keeping the ongoing round intact.</summary>
    void ShowLoadMenu()
    {
        var menu = new ContextMenu
        {
            Foreground = Color("menuText"),
            Background = Color("menuBackground")
        };
        var google = new MenuItem
        {
            Header = locale["reloadSource"]
        };
        google.Click += async (_, _) => await LoadTickets(config.TicketSource);
        menu.Items.Add(google);
        var local = new MenuItem
        {
            Header = locale["localFile"]
        };
        local.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = locale["load"],
                Filter = locale["fileFilter"],
                InitialDirectory = Config.Resolve("examples")
            };
            if (dialog.ShowDialog(this) == true)
                await LoadTickets(dialog.FileName);
        };
        menu.Items.Add(local);
        load.ContextMenu = menu;
        menu.PlacementTarget = load;
        menu.IsOpen = true;
    }

    /// <summary>Imports a complete replacement before updating the game, retaining old tickets and explaining failures in the log.</summary>
    async Task<bool> LoadTickets(string source)
    {
        if (loading)
            return false;
        loading = true;
        load.IsEnabled = false;
        Log("loading", "info", source);
        try
        {
            var replacement = await Sources.ReadTickets(source);
            RenderEvaluation(game.ReplaceTickets(replacement));
            Log("loaded", "info", replacement.Count);
            RefreshRound();
            return true;
        }
        catch (Exception e)
        {
            var detail = e switch
            {
                TicketFormatException f => new LocalText(f.Code, [f.Row, f.Detail]),
                FileNotFoundException or DirectoryNotFoundException => new("notFound", []),
                UnauthorizedAccessException => new("accessDenied", []),
                HttpRequestException h when h.StatusCode != null => new("httpError", [(int)h.StatusCode]),
                HttpRequestException => new("networkError", []),
                TaskCanceledException => new("timeout", []),
                System.IO.InvalidDataException or System.Xml.XmlException => new("badXlsx", []),
                IOException => new("fileError", []),
                _ => new("unknownError", [])};
            Log("loadFailed", "error", detail);
            return false;
        }
        finally
        {
            loading = false;
            load.IsEnabled = true;
        }
    }

    /// <summary>Loads replaceable user artwork and sound icons, falling back safely when individual images are unavailable.</summary>
    async Task LoadImages()
    {
        // Purpose: Loads one optional image and logs a localized fallback warning rather than aborting all asset loading.
        async Task<BitmapImage?> Try(string path)
        {
            try
            {
                return await Sources.Image(path);
            }
            catch
            {
                Log("imageFallback", "warn", path);
                return null;
            }
        }

        try
        {
            await emptyPoolAnimation.LoadAsync(config.EmptyPoolAnimation);
        }
        catch
        {
            Log("emptyPoolAnimationUnavailable", "warn");
        }

        soundOnIcon = await Try(config.SoundOnIcon);
        soundOffIcon = await Try(config.SoundOffIcon);
        UpdateSoundButton();
        var background = await Try(config.Background);
        if (background != null)
            shell.Background = new ImageBrush(background)
            {
                Stretch = Stretch.UniformToFill,
                Opacity = .34
            };
        var banner = await Try(config.Banner);
        if (banner != null)
        {
            bannerA.Source = bannerB.Source = banner;
            bannerFallback.Visibility = Visibility.Collapsed;
            AnimateBanner();
        }

        if (!string.IsNullOrWhiteSpace(config.BallSprite))
        {
            var ball = await Try(config.BallSprite);
            if (ball != null)
                upper.Sprite = lower.Sprite = lastBall.Sprite = TrimTransparentMargins(ball);
            upper.InvalidateVisual();
            lower.InvalidateVisual();
            lastBall.InvalidateVisual();
        }

        if (!string.IsNullOrWhiteSpace(config.BannerOverlay))
        {
            var overlay = await Try(config.BannerOverlay);
            if (overlay != null)
            {
                bannerOverlay.Source = overlay;
                bannerFallback.Visibility = Visibility.Collapsed;
            }
        }

        var poses = new List<BitmapSource>();
        foreach (var path in config.RaptorImages ?? [])
        {
            var image = await Try(path);
            if (image != null)
                poses.Add(TrimTransparentMargins(image));
        }

        raptorPoses = poses.ToArray();
        if (raptorPoses.Length == 0)
        {
            raptor.Source = MakeFallbackRaptor();
        }
        else
            SetPose();
    }

    /// <summary>Builds a small in-memory mascot so missing external images never leave the corner dependent on a file.</summary>
    static BitmapSource MakeFallbackRaptor()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(Brush("#7EAA82"), null, new(60, 65), 48, 35);
            dc.DrawEllipse(Brush("#FFF0CA"), null, new(40, 48), 9, 9);
            dc.DrawEllipse(Brush("#172624"), null, new(38, 48), 4, 5);
            dc.DrawLine(new Pen(Brush("#172624"), 4), new(25, 74), new(80, 82));
        }

        var image = new RenderTargetBitmap(120, 120, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        return image;
    }

    // Trim only the displayed bitmap. The supplied PNG files remain byte-for-byte intact.
    /// <summary>Crops only the displayed bitmap to its visible bounds while leaving the supplied source PNG unchanged.</summary>
    static BitmapSource TrimTransparentMargins(BitmapSource source)
    {
        var rgba = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = rgba.PixelWidth * 4;
        var pixels = new byte[stride * rgba.PixelHeight];
        rgba.CopyPixels(pixels, stride, 0);
        int left = rgba.PixelWidth, top = rgba.PixelHeight, right = -1, bottom = -1;
        for (int y = 0; y < rgba.PixelHeight; y++)
            for (int x = 0; x < rgba.PixelWidth; x++)
            {
                if (pixels[y * stride + x * 4 + 3] <= 8)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }

        if (right < left)
            return source;
        var crop = new CroppedBitmap(source, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
        crop.Freeze();
        return crop;
    }

    /// <summary>Selects the raptor pose and eases its head angle; the return timer restores the resting pose after feedback.</summary>
    void SetPose()
    {
        if (raptorPoses.Length > 0)
            raptor.Source = raptorPoses[pose % raptorPoses.Length];
        raptor.RenderTransformOrigin = new(.5, .7);
        raptor.RenderTransform = raptorRotation;
        raptorRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(pose == 0 ? -4 : 4, TimeSpan.FromMilliseconds(240)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
    }

    /// <summary>Rolls an opaque ball through the clipped left edge using a radius-based rotation instead of fading into view.</summary>
    void AnimateLastBall()
    {
        lastBall.UpdateLayout();
        var path = BallRoll.ForViewport(lastBall.ActualWidth, lastBall.ActualHeight);
        var movement = new TranslateTransform(path.StartX, 0);
        var rotation = new RotateTransform(path.StartAngle);
        var transforms = new TransformGroup();
        transforms.Children.Add(rotation);
        transforms.Children.Add(movement);
        lastBall.RenderTransformOrigin = new(.5, .5);
        lastBall.RenderTransform = transforms;
        var easing = new CubicEase
        {
            EasingMode = EasingMode.EaseOut
        };
        movement.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(path.StartX, 0, TimeSpan.FromMilliseconds(800)) { EasingFunction = easing });
        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(path.StartAngle, 0, TimeSpan.FromMilliseconds(800)) { EasingFunction = easing });
        // No fade-in: the opaque ball enters through the physical edge of its window.
        lastBall.BeginAnimation(OpacityProperty, null);
        lastBall.Opacity = 1;
    }

    /// <summary>Moves overlapping repeated image tiles leftward to avoid exposing a dark seam between antialiased edges.</summary>
    void AnimateBanner()
    {
        if (bannerA.Source is not BitmapSource bitmap || ActualWidth <= 0)
            return;
        double h = 101, width = Math.Max(ActualWidth, bitmap.PixelWidth * h / bitmap.PixelHeight);
        // Moving bitmap edges can fall between device pixels. Overlap the images
        // so their antialiased outer edges never expose the dark canvas below.
        // Keep the repeat distance independent of the added overlap.
        double overlap = Math.Max(2, 2 / VisualTreeHelper.GetDpi(bannerCanvas).DpiScaleX);
        bannerA.Width = bannerB.Width = width + overlap;
        bannerA.Height = bannerB.Height = h;
        bannerA.Stretch = bannerB.Stretch = Stretch.Fill;
        Canvas.SetLeft(bannerB, width);
        var transform = new TranslateTransform();
        bannerCanvas.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -width, TimeSpan.FromSeconds(width / 34)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    /// <summary>Provides the explicit legacy window-check entry point without enabling it during normal application startup.</summary>
    internal static class UiVerification
    {
        /// <summary>Forwards an explicitly requested window check to the verification routine.</summary>
        public static async Task Run(MainWindow w) => await Verification.Run(w);
    }

    /// <summary>Contains optional static-window regression checks, separate from the normal host workflow and console physics tests.</summary>
    internal static class Verification
    {
        /// <summary>Exercises a disposable static test session and writes evidence only when the self-test argument is explicitly supplied.</summary>
        public static async Task Run(MainWindow w)
        {
            // This legacy window check inspects the immediate static layout.
            // The independent console suite exercises the time-dependent physics.
            w.upper.ConfigurePhysics(new() { Enabled = false });
            string folder = Environment.GetEnvironmentVariable("MURBINGO_TEST_OUTPUT") ?? Config.Resolve("verification");
            Directory.CreateDirectory(folder);
            var checks = new List<string>();
            // Purpose: Records a successful window invariant or aborts the test session with a precise failure description.
            void Check(bool condition, string name)
            {
                if (!condition)
                    throw new Exception(name);
                checks.Add("PASS " + name);
            }

            // Purpose: Renders the test window to a PNG at the requested DPI to preserve layout evidence for an explicit self-test.
            void Snapshot(string file, double dpi = 96)
            {
                w.UpdateLayout();
                var image = new RenderTargetBitmap((int)(w.ActualWidth * dpi / 96), (int)(w.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                image.Render(w);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var s = File.Create(Path.Combine(folder, file));
                encoder.Save(s);
            }

            try
            {
                w.counter = 99;
                w.Step(1);
                Check(w.counter == 0, "counter 99 to 00");
                w.Step(-1);
                Check(w.counter == 99, "counter 00 to 99");
                w.Restart();
                w.Width = 1060;
                w.Height = 700;
                await w.Dispatcher.InvokeAsync(() =>
                {
                }, System.Windows.Threading.DispatcherPriority.Render);
                w.UpdateLayout();
                Snapshot("01-upper-99-minimum.png");
                Check(w.upper.LastBounds.Count == 99 && w.upper.LastBounds.All(r => r.Left >= 0 && r.Top >= 0 && r.Right <= w.upper.ActualWidth && r.Bottom <= w.upper.ActualHeight), "99 upper balls fit at minimum size");
                Check(!w.upper.LastBounds.SelectMany((r, i) => w.upper.LastBounds.Skip(i + 1).Select(other => (new Point(r.X + r.Width / 2, r.Y + r.Height / 2) - new Point(other.X + other.Width / 2, other.Y + other.Height / 2)).Length < (r.Width + other.Width) / 2)).Any(overlap => overlap), "upper balls do not overlap");
                var originalPositions = w.upper.Numbers.Zip(w.upper.LastBounds).ToDictionary(p => p.First, p => p.Second);
                w.Draw();
                int drawn = w.game.Last!.Value;
                Snapshot("01b-after-draw.png");
                Check(w.upper.Numbers.Zip(w.upper.LastBounds).All(p => originalPositions[p.First] == p.Second), "remaining balls keep their positions after draw");
                Check(await w.LoadTickets("examples/tickets.csv"), "CSV import through application");
                Check(w.game.Drawn.Count == 1 && w.game.Last == drawn, "import preserves round");
                var savedTickets = w.game.Tickets;
                Check(!await w.LoadTickets("missing-file.xlsx") && ReferenceEquals(savedTickets, w.game.Tickets), "failed import retains tickets");
                string replacementFile = Path.Combine(folder, "replacement.csv");
                File.WriteAllText(replacementFile, $"New player,{drawn}\nAnother player,{drawn},99");
                Check(await w.LoadTickets(replacementFile) && w.game.Tickets.Count == 2 && w.game.Last == drawn && w.rankings.Children.Count == 2, "different document recalculates existing matches");
                Check(await w.LoadTickets("examples/tickets.xlsx"), "XLSX import through application");
                w.ChangeLanguage("en");
                Check(w.Title == w.locale["title"] && w.game.Last == drawn, "language change preserves round");
                Check(w.logs.Children.OfType<TextBlock>().Any(t => t.Text.Contains("File not found.")), "existing error reasons relocalize");
                Snapshot("02-english.png");
                w.ChangeLanguage("ru");
                while (w.game.Remaining.Count > 0)
                    w.Draw();
                await w.Dispatcher.InvokeAsync(() =>
                {
                }, System.Windows.Threading.DispatcherPriority.Render);
                Snapshot("03-lower-99-minimum.png", 144);
                Check(w.game.Drawn.Count == 99 && !w.draw.IsEnabled, "99 draws disable draw button");
                Check(w.lower.LastBounds.Count == 99 && w.lower.LastBounds.All(r => r.Left >= 0 && r.Top >= 0 && r.Right <= w.lower.ActualWidth && r.Bottom <= w.lower.ActualHeight), "99 lower balls fit at minimum size, 150 percent render");
                w.Width = 1540;
                w.Height = 940;
                await w.Dispatcher.InvokeAsync(() =>
                {
                }, System.Windows.Threading.DispatcherPriority.Render);
                Snapshot("04-large.png");
                w.Restart();
                w.Draw();
                w.Restart();
                Check(w.game.Drawn.Count == 0 && w.game.Last == null && w.game.Tickets.Count == 5, "restart mid-round preserves tickets");
                Snapshot("05-restart.png");
                w.counter = 1;
                w.Restart();
                var singleTickets = TicketReader.ParseCsv("Winner A,1\nWinner B,1");
                w.RenderEvaluation(w.game.ReplaceTickets(singleTickets));
                w.Draw();
                Check(w.entries.Count(e => e.Key == "winnerLog" && (string)e.Args[0] == "Winner A") == 1 && w.entries.Count(e => e.Key == "winnerLog" && (string)e.Args[0] == "Winner B") == 1, "simultaneous winners are logged in application");
                w.RenderEvaluation(w.game.ReplaceTickets(TicketReader.ParseCsv("Winner A,1\nWinner B,1")));
                Check(w.entries.Count(e => e.Key == "winnerLog" && (string)e.Args[0] == "Winner A") == 1, "reload does not repeat victory log");
                int last = w.game.Last!.Value;
                w.entries.Clear();
                w.RenderLogs();
                Check(w.logs.Children.Count == 0 && w.game.Last == last && w.game.Drawn.Count == 1 && w.game.Evaluate().NewWinners.Count == 0, "clear log preserves results and announcement flags");
                w.counter = 0;
                w.Restart();
                Check(!w.draw.IsEnabled && w.lastText.Text == "—", "empty pool disables UI draw and clears display");
                w.counter = 60;
                w.Restart();
                await w.LoadTickets("examples/tickets.csv");
                w.Width = 1360;
                w.Height = 860;
                await w.Dispatcher.InvokeAsync(() =>
                {
                }, System.Windows.Threading.DispatcherPriority.Render);
                Snapshot("06-ready.png");
                File.WriteAllLines(Path.Combine(folder, "ui-checks.txt"), checks);
                w.Close();
            }
            catch (Exception e)
            {
                File.WriteAllLines(Path.Combine(folder, "ui-checks.txt"), checks.Append("FAIL " + e));
                w.Close();
                Environment.ExitCode = 1;
            }
        }
    }
}
