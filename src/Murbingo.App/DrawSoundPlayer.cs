using System.Media;

namespace Murbingo.App;
/// <summary>Owns the shared Windows audio channel for draw feedback and the mutually exclusive zero-ball music loop.</summary>
public sealed class DrawSoundPlayer(bool enabled, Action? backgroundPlaybackError = null) : IDisposable
{
    readonly List<SoundPlayer> players = new();
    SoundPlayer? current;
    SoundPlayer? music;
    bool musicPlaying;
    bool musicFailed;
    bool disposed;
    public DrawSoundState State { get; } = new(enabled);

    /// <summary>Loads a PCM clip without playing it; a bad resource is skipped instead of triggering a default system sound.</summary>
    static SoundPlayer? LoadClip(string path)
    {
        SoundPlayer? player = null;
        try
        {
            player = new SoundPlayer(Config.Resolve(path));
            player.Load();
            return player;
        }
        catch
        {
            player?.Dispose();
            return null;
        }
    }

    /// <summary>Preloads draw clips and optional music on workers, then honors the current mode on the UI context.</summary>
    public async Task LoadAsync(IEnumerable<string> paths, Action<string> unavailable, string? musicPath = null)
    {
        var loaded = await Task.Run(() => paths.Select(path => (Path: path, Player: LoadClip(path))).ToArray());
        foreach (var clip in loaded)
        {
            if (disposed)
            {
                clip.Player?.Dispose();
                continue;
            }

            if (clip.Player is null)
                unavailable(clip.Path);
            else
                players.Add(clip.Player);
        }

        if (!string.IsNullOrWhiteSpace(musicPath))
        {
            var track = await Task.Run(() => LoadClip(musicPath));
            if (disposed)
                track?.Dispose();
            else if (track is null)
                unavailable(musicPath);
            else
                music = track;
        }

        UpdateBackgroundMusic();
    }

    /// <summary>Plays one draw clip to completion on a worker; busy clicks are discarded with no delayed replay.</summary>
    public async Task PlayDrawAsync()
    {
        if (disposed || State.TryBegin(players.Count)is not int index)
            return;
        current = players[index];
        var clip = current;
        try
        {
            await Task.Run(() =>
            {
                if (!disposed && State.Enabled && !State.BackgroundRequested)
                    clip.PlaySync();
            });
        }
        finally
        {
            State.Complete();
            UpdateBackgroundMusic();
        }
    }

    /// <summary>Requests music for the actual empty round; repeated view refreshes do not restart a running loop.</summary>
    public void SetEmptyPoolActive(bool active)
    {
        if (State.BackgroundRequested == active)
            return;
        State.RequestBackground(active);
        UpdateBackgroundMusic();
    }

    /// <summary>Starts native looping only when permitted, or stops it when leaving the easter egg or muting.</summary>
    void UpdateBackgroundMusic()
    {
        if (disposed)
            return;
        if (!State.Enabled || !State.BackgroundRequested)
        {
            if (musicPlaying)
                music?.Stop();
            musicPlaying = false;
            return;
        }

        if (musicPlaying || musicFailed || music is null)
            return;
        // PlaySound is a shared channel. Wait until a draw's worker has exited
        // before starting music so an old scheduled clip cannot replace the loop.
        if (State.IsPlaying)
        {
            current?.Stop();
            return;
        }

        if (!State.CanStartBackground(true))
            return;
        try
        {
            music.PlayLooping();
            musicPlaying = true;
        }
        catch
        {
            musicFailed = true;
            backgroundPlaybackError?.Invoke();
        }
    }

    /// <summary>Mutes both audio modes immediately; unmuting restarts music only while the zero-ball mode remains active.</summary>
    public bool Toggle()
    {
        bool enabled = State.Toggle();
        if (!enabled)
            current?.Stop();
        else
            musicFailed = false;
        UpdateBackgroundMusic();
        return enabled;
    }

    /// <summary>Stops the native audio channel and releases every loaded clip as the host closes.</summary>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        current?.Stop();
        music?.Stop();
        foreach (var player in players)
            player.Dispose();
        music?.Dispose();
        players.Clear();
    }
}
