using System.Security.Cryptography;

namespace Murbingo.App;
/// <summary>Keeps session mute state and chooses an audio index independently of the bingo draw.</summary>
public sealed class DrawSoundState(bool enabled = true)
{
    public bool Enabled { get; private set; } = enabled;
    public bool IsPlaying { get; private set; }
    public bool BackgroundRequested { get; private set; }

    /// <summary>Changes only audio availability so muting never alters the current round.</summary>
    public bool Toggle() => Enabled = !Enabled;
    /// <summary>Starts one random clip only when idle; clicks during playback are discarded rather than queued.</summary>
    public int? TryBegin(int count)
    {
        if (!Enabled || IsPlaying || BackgroundRequested || count <= 0)
            return null;
        IsPlaying = true;
        return RandomNumberGenerator.GetInt32(count);
    }

    /// <summary>Tracks the zero-ball mode separately so draw clips cannot interrupt its background loop.</summary>
    public void RequestBackground(bool requested) => BackgroundRequested = requested;
    /// <summary>Allows music only after the last draw clip finishes and a ready track is requested and unmuted.</summary>
    public bool CanStartBackground(bool trackReady) => Enabled && BackgroundRequested && !IsPlaying && trackReady;
    /// <summary>Releases the playback gate without scheduling another clip; a new click is required to start sound.</summary>
    public void Complete() => IsPlaying = false;
}
