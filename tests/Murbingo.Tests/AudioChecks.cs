using Murbingo.App;
using Murbingo.Core;

/// <summary>Checks audio gating and mute behavior without opening a window or producing sound.</summary>
internal static class AudioChecks
{
    /// <summary>Verifies random clip selection, busy-click rejection and independence from round state.</summary>
    public static void Run(Action<bool, string> check)
    {
        var audio = new DrawSoundState();
        check(audio.Enabled && !audio.IsPlaying && audio.TryBegin(0) == null && !audio.IsPlaying, "audio starts enabled and an empty clip list does not reserve playback");
        var chosen = new HashSet<int>();
        bool inRange = true;
        for (int i = 0; i < 600; i++)
        {
            int? index = audio.TryBegin(3);
            inRange &= index is >= 0 and < 3;
            if (index.HasValue)
                chosen.Add(index.Value);
            audio.Complete();
        }

        check(inRange && chosen.SetEquals(new[] { 0, 1, 2 }), "audio selects only the three clips and can choose each one");
        audio.TryBegin(3);
        bool ignored = Enumerable.Range(0, 100).All(_ => audio.TryBegin(3) == null);
        check(ignored && audio.IsPlaying, "all clicks during playback are discarded without interrupting the active sound");
        audio.Complete();
        check(!audio.IsPlaying && audio.TryBegin(3).HasValue, "completion queues nothing and the next explicit click can start sound");
        check(!audio.Toggle() && audio.TryBegin(3) == null, "muting blocks new playback even when a clip was already active");
        audio.Toggle();
        bool stillBusy = audio.TryBegin(3) == null;
        audio.Complete();
        check(stillBusy && !audio.IsPlaying, "quick unmute cannot restart a still-active clip and stop completion does not replay busy clicks");
        var muted = new DrawSoundState(false);
        var game = new Game(5);
        game.Draw();
        game.Restart(5);
        check(!muted.Enabled && muted.TryBegin(3) == null && game.Remaining.Count == 5 && game.Last == null, "configured mute survives an unrelated round restart");
        audio.TryBegin(3);
        audio.Complete();
        check(game.Drawn.Count == 0 && game.Remaining.SequenceEqual(Enumerable.Range(1, 5)), "sound selection never draws or removes a bingo number");
        var background = new DrawSoundState();
        background.RequestBackground(true);
        check(background.CanStartBackground(true) && background.TryBegin(3) == null, "empty-pool music is eligible and excludes draw clips");
        background.Toggle();
        check(!background.CanStartBackground(true), "mute blocks the background melody as well as draw sounds");
        background.Toggle();
        background.RequestBackground(false);
        check(!background.CanStartBackground(true) && background.TryBegin(3).HasValue, "leaving the empty round stops music eligibility and restores draw feedback");
        background.RequestBackground(true);
        bool waiting = !background.CanStartBackground(true);
        background.Complete();
        check(waiting && background.CanStartBackground(true), "background waits for a previous draw worker to finish before taking the shared channel");
        check(!background.CanStartBackground(false), "missing music never attempts native playback");
    }
}
