// VideoTimecode.cs

// ============================================================================
// DEVELOPMENT TOOL. NEVER SHIPS. (0.7.269.)
//
// Zamar: "this visual thing is exclusively for debugging and development and
// should not ever ship with the mod."
//
// So it is not a runtime flag. The whole file is behind IKMA_DEV, which the
// csproj defines from a single property, IKMADevTools. Set that to false and
// this code is not compiled into the DLL at all — not disabled, absent, along
// with the UnityEngine.IMGUIModule reference it needs. A bool could ship true
// by accident; a symbol that is not defined cannot.
//
// The release checklist in docs/MASTER_PLAN.md (M8) names this switch.
// ============================================================================
#if IKMA_DEV

using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// An on-screen timecode readout for whatever video is playing. An
    /// AUTHORING TOOL for writing audio-description cues against, not a player
    /// feature — it draws pixels and says nothing.
    /// </summary>
    /// <remarks>
    /// 0.7.269. Zamar: "for when a video is playing, for debug purposes, can
    /// you give me visually displayed timecode for the currently playing
    /// video? That will give me a solid number to latch onto for when things
    /// should fire. I suspect numbers in code will always be a bit off, like
    /// the Devolver Digital thing we had to tweak."
    ///
    /// WHY A NUMBER OFF THE SCREEN BEATS A NUMBER OUT OF THE CODE. The boot
    /// parade is the worked example and it is already in
    /// BootScreenReader: every WaitForSeconds in
    /// FirstPlaySceneController.IntroSequence is a constant, so the WINDOW is
    /// known exactly — but where each logo actually lands inside that window
    /// lives in an animation clip, not in code, and both logo lines are still
    /// marked PROVISIONAL because they were placed by reading rather than by
    /// measuring. Every AD cue has that shape. He can see the screen; this
    /// gives him the clock that goes with it.
    ///
    /// TWO CLOCKS, BECAUSE THERE ARE TWO KINDS OF VIDEO:
    ///
    ///   FOOTAGE — VideoNarrator.ClipTime, read from the Unity VideoPlayer's
    ///   own `time` every frame. This is the clip's own clock, so it is
    ///   correct through pause, rewind and fast-forward, and a recording's
    ///   time 0 lines up with it. Shown while a clip is loaded, including
    ///   while paused, which is the frame most worth writing a cue against.
    ///
    ///   BOOT INTRO — BootScreenReader's elapsed time since the play button
    ///   was pressed. Not a video player at all: it is the fixed timeline the
    ///   game runs, and it is the clock the Devolver line was tweaked against.
    ///
    /// GATED, AND IT SHIPS OFF. Enabled is a plain field rather than a key
    /// binding on purpose: the boot screen is the one place this is most
    /// useful and Zamar's rule there is "no other buttons should exist" — a
    /// debug toggle would be exactly the kind of key that screen must not
    /// have. Flip the field, rebuild. M8 turns it off for release.
    ///
    /// CHEAP BY CONSTRUCTION. OnGUI runs several times a frame, so the string
    /// is built in Update and OnGUI only draws it. Nothing here is on the path
    /// of anything a player hears.
    /// </remarks>
    internal static class VideoTimecode
    {
        /// <summary>Set false to remove the readout. Release builds ship false.</summary>
        internal static bool Enabled = true;

        private static string _text;
        private static GUIStyle _style;
        private static GUIStyle _shadow;

        /// <summary>Called once per frame from HotkeyManager.Update.</summary>
        internal static void Tick()
        {
            if (!Enabled) { _text = null; return; }

            try
            {
                if (VideoNarrator.ClipPlaying)
                {
                    float t = VideoNarrator.ClipTime;
                    _text = $"{Format(t)}   {VideoNarrator.ClipName ?? "clip"}";
                    return;
                }

                float boot = BootScreenReader.IntroElapsed;
                if (boot > 0f)
                {
                    _text = $"{Format(boot)}   boot intro{BeatNote()}";
                    return;
                }

                _text = null;
            }
            catch { _text = null; }
        }

        // WHAT IKMA LAST SPOKE, AND AT WHAT TIME. (0.7.271.)
        //
        // Held for a few seconds so it is still on screen when the ear catches
        // up with the eye. The point is the pair: the clock says where the
        // picture is now, this says where the line went in. The Devolver
        // revert happened because only the first of those was visible.
        private const float BEAT_HOLD_SECONDS = 5f;

        private static string BeatNote()
        {
            try
            {
                if (string.IsNullOrEmpty(BootScreenReader.LastBeatName)) return "";
                float age = Time.unscaledTime - BootScreenReader.LastBeatWallTime;
                if (age < 0f || age > BEAT_HOLD_SECONDS) return "";
                return $"   <- {BootScreenReader.LastBeatName} @ {BootScreenReader.LastBeatAt:0.00}s";
            }
            catch { return ""; }
        }

        // Seconds with two decimals AND minutes:seconds, because a cue sheet
        // wants one and scrubbing wants the other. The seconds figure is the
        // one that matches every number already in the log, which is printed
        // as 0.00 throughout — so a time read off the screen can be compared
        // with a time read out of the log without converting anything.
        private static string Format(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int m = (int)(seconds / 60f);
            float s = seconds - (m * 60);
            return $"{m}:{s:00.00}  ({seconds:0.00}s)";
        }

        /// <summary>Called from HotkeyManager.OnGUI.</summary>
        internal static void Draw()
        {
            if (!Enabled || string.IsNullOrEmpty(_text)) return;

            try
            {
                if (_style == null)
                {
                    // fontSize only. FontStyle and TextAnchor live in
                    // UnityEngine.TextRenderingModule, and a debug readout is
                    // not worth a third reference on a DLL that ships to
                    // players — upper-left is the default anyway, and the
                    // shadow below does the work bold would have.
                    _style = new GUIStyle { fontSize = 34 };
                    _style.normal.textColor = Color.white;

                    // A plain drop shadow rather than a background box: the
                    // footage is dark in some clips and bright in others, and a
                    // box would cover picture he is trying to describe.
                    _shadow = new GUIStyle(_style);
                    _shadow.normal.textColor = Color.black;
                }

                GUI.Label(new Rect(22f, 20f, 900f, 60f), _text, _shadow);
                GUI.Label(new Rect(20f, 18f, 900f, 60f), _text, _style);
            }
            catch
            {
                // A readout that throws must never take the mod with it.
                Enabled = false;
            }
        }
    }
}

#endif // IKMA_DEV

// VideoTimecode.cs
