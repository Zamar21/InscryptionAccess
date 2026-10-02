// VideoNarrator.cs
using System.Reflection;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// The live-action footage. Speaks the game's own subtitles as they
    /// appear, and is the hook where Zamar's recorded audio descriptions
    /// will play (see docs/VIDEO_AD.md; playback of the recordings is the
    /// next build, this one is the timing and the seam).
    /// </summary>
    /// <remarks>
    /// 0.7.210. Registered through Plugin.TryPatch.
    ///
    /// HOW THE GAME PLAYS FOOTAGE. `VideoCameraRig` (Singleton) owns one
    /// `FootageController`, which owns the Unity `VideoPlayer`. A clip is a
    /// `VideoClipData` (clip, thumbnail, `date`, `subtitles`); the footage
    /// menu (`VideoFootageMenu.OnFootageSelected`) calls
    /// `VideoCameraRig.PlayVideo(clipData, withControls, withCC)`, which
    /// calls `FootageController.PrepareClip(clipData)` then
    /// `PlayWhenPrepared`. Pause, rewind (−2.5 s), fast-forward and stop are
    /// the controller's own buttons and move `player.time`; subtitles are
    /// `SubtitleData.GetLineAtTime(player.time)`, so IKMA asks the same
    /// question every frame and speaks a line when it changes. Seeking
    /// therefore works for free: the line at the new time is spoken.
    ///
    /// 17 of the 25 clips have subtitle data (`data\videosubtitles`). The
    /// three glitch clips, the two Long-Night sequels and a few others do
    /// not; those are exactly the ones the audio description is for.
    ///
    /// WHAT IS SPOKEN. The clip's `date` (the game's own string, what the
    /// menu shows) on prepare; each subtitle line at its start time; nothing
    /// on the clip ending — the game's controls remain and the footage
    /// reader will say so. Subtitle text is the game's, passed through.
    /// The clip's asset name ("Buy-FloppyDrive") is an id, never spoken.
    /// </remarks>
    public static class VideoNarrator
    {
        private static FootageController _controller;
        private static SubtitleData _subs;
        private static SubtitleData.SubtitleLine _lastLine;
        private static string _clipDate;

        // ------------------------------------------------------------------
        // THE LIVE CLOCK, PUBLISHED FOR THE TIMECODE OVERLAY. (0.7.269.)
        //
        // Zamar: "can you give me visually displayed timecode for the
        // currently playing video? That will give me a solid number to latch
        // onto for when things should fire. I suspect numbers in code will
        // always be a bit off, like the Devolver Digital thing we had to
        // tweak."
        //
        // He is right about the Devolver case and it is the precedent: the
        // boot parade's two logo lines sit at times IKMA GUESSED, because
        // where each logo lands inside the animation is in a clip rather than
        // in code. Every audio description cue will have the same problem —
        // the recordings are written against what is on screen, and only a
        // number read off the screen can be trusted for that.
        //
        // Written once per frame in Tick, read by VideoTimecode. Nothing here
        // is spoken and nothing changes what a player hears.
        // ------------------------------------------------------------------
        internal static bool   ClipPlaying;
        internal static float  ClipTime;
        internal static string ClipName;

        internal static void OnPrepareClip(FootageController controller, VideoClipData clipData)
        {
            try
            {
                _controller = controller;
                _subs = clipData?.subtitles;
                _lastLine = null;
                _clipDate = clipData?.date;
                ClipName  = clipData?.name;
                ClipTime  = 0f;
                Plugin.Log?.LogInfo(
                    $"IKMA VIDEO: clip prepared, date='{_clipDate}', subtitles={(_subs != null ? _subs.lines?.Count ?? 0 : 0)} lines.");
                if (!string.IsNullOrEmpty(_clipDate))
                    Speech.Result(Vocabulary.FootagePlaying(_clipDate));
                AudioDescription.OnClipPrepared(clipData);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA VIDEO: {e.GetType().Name}: {e.Message}");
            }
        }

        // The VideoPlayer type lives in UnityEngine.VideoModule, which the
        // csproj does not reference. Read it by reflection so the build stays
        // exactly as it is; the two members needed are `isPlaying` and `time`.
        private static PropertyInfo _playerProp, _isPlayingProp, _timeProp;

        /// <summary>Called once per frame from HotkeyManager.Update.</summary>
        internal static void Tick()
        {
            // The overlay needs the clock even for the nine clips that carry no
            // subtitle data at all — those are exactly the ones the audio
            // description matters most for — so the clock is read before the
            // subtitle work and its early return.
            TickClock();

            if (_controller == null || _subs == null) return;
            try
            {
                if (_playerProp == null)
                    _playerProp = typeof(FootageController).GetProperty("VideoPlayer",
                        BindingFlags.Public | BindingFlags.Instance);
                object player = _playerProp?.GetValue(_controller, null);
                if (player == null) return;
                if (_isPlayingProp == null)
                {
                    _isPlayingProp = player.GetType().GetProperty("isPlaying");
                    _timeProp      = player.GetType().GetProperty("time");
                }
                if (_isPlayingProp == null || _timeProp == null) return;
                if (!(bool)_isPlayingProp.GetValue(player, null)) return;
                float time = (float)(double)_timeProp.GetValue(player, null);

                var line = _subs.GetLineAtTime(time);

                if (line == _lastLine) return;
                _lastLine = line;
                if (line == null || string.IsNullOrEmpty(line.line)) return;

                // A subtitle replaces the previous subtitle, as on screen.
                Speech.Browse(line.line);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA VIDEO: {e.GetType().Name}: {e.Message}");
                _controller = null;
            }
        }

        /// <summary>
        /// Reads player.time every frame so the overlay has a number, whether
        /// or not this clip has subtitles.
        /// </summary>
        private static void TickClock()
        {
            if (_controller == null) { ClipPlaying = false; return; }
            try
            {
                if (_playerProp == null)
                    _playerProp = typeof(FootageController).GetProperty("VideoPlayer",
                        BindingFlags.Public | BindingFlags.Instance);
                object player = _playerProp?.GetValue(_controller, null);
                if (player == null) { ClipPlaying = false; return; }

                if (_isPlayingProp == null)
                {
                    _isPlayingProp = player.GetType().GetProperty("isPlaying");
                    _timeProp      = player.GetType().GetProperty("time");
                }
                if (_isPlayingProp == null || _timeProp == null) { ClipPlaying = false; return; }

                // PAUSED IS NOT GONE. isPlaying goes false on pause, and a
                // paused frame is the most useful one there is for writing a
                // cue against — so the readout keeps showing the last time
                // rather than blanking. It clears when a clip is prepared or
                // the controller goes away.
                ClipTime    = (float)(double)_timeProp.GetValue(player, null);
                ClipPlaying = true;
            }
            catch
            {
                ClipPlaying = false;
            }
        }
    }

    /// <summary>
    /// Zamar's recorded audio descriptions, one file per clip. THIS BUILD:
    /// finds the file and logs it. Playback needs a reference to
    /// UnityEngine.AudioModule in the csproj (AudioClip.Create +
    /// AudioSource) and is the next step; the seam and the file convention
    /// are settled here so recordings can start now.
    /// </summary>
    /// <remarks>
    /// FILE CONVENTION: BepInEx\plugins\IKMAccess_AD\{clip asset name}.wav
    /// — e.g. Buy-FloppyDrive.wav. 16-bit PCM WAV, mono or stereo, any
    /// rate. The asset names are the 25 in docs/VIDEO_AD.md. Extended AD
    /// (pausing the video for a description longer than the gap) is a
    /// design choice for Zamar; the simplest first version is a track that
    /// plays alongside the clip from time 0 and follows pause/seek.
    /// </remarks>
    public static class AudioDescription
    {
        internal static string PendingFile;

        internal static void OnClipPrepared(VideoClipData clipData)
        {
            PendingFile = null;
            string name = clipData?.name;
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                string dir = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "IKMAccess_AD");
                string path = System.IO.Path.Combine(dir, name + ".wav");
                if (System.IO.File.Exists(path))
                {
                    PendingFile = path;
                    Plugin.Log?.LogInfo($"IKMA AD: description found for '{name}' (playback not built yet).");
                }
                else
                {
                    Plugin.Log?.LogInfo($"IKMA AD: no description file for '{name}' — expected {path}");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA AD: {e.GetType().Name}: {e.Message}");
            }
        }

    }

    // Registered by Plugin.TryPatch. No [HarmonyPatch] attribute (check_source CHECK 1).
    public static class FootageController_PrepareClip_Patch
    {
        public static void Prefix(FootageController __instance, VideoClipData clipData)
            => VideoNarrator.OnPrepareClip(__instance, clipData);
    }
}
