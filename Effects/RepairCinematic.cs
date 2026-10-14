using System;
using System.Reflection;
using DooDesch.Transition;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Audio;

namespace RVRepairVan.Effects
{
    /// <summary>
    /// Short "repair" cinematic for the Marco RV repair: a deliberate hard CUT (the game's BlackOverlay, no eyelids -
    /// it should read as a jump-cut in time, not the player blinking), holding ~2.5s while a bundled repair sound
    /// plays AND the RV is swapped (hidden behind the black), then fade back and run a completion line. Movement +
    /// camera are locked during the black. The fade / hold / input-lock / guaranteed-restore all come from the shared
    /// <see cref="DooDesch.Transition.ScreenTransition"/> primitive; only the bundled sound is RV-specific.
    /// </summary>
    internal static class RepairCinematic
    {
        private const float FadeTime = 0.6f;
        private const float HoldTime = 2.5f;       // OnMidHold fires at the midpoint (~1.25s) - inside the WAV's quiet gap (~1.05-1.70s)
        private const float UpgradeHoldTime = 1.6f;   // build-outs are a smaller job, and the player sees them repeatedly
        private const float SoundVolume = 0.2f;    // repair clanks sit UNDER Marco's voice (tune to taste)

        private const string Key = "RV.Repair";

        private static UnityEngine.AudioClip _clip;
        private static bool _clipTried;

        /// <summary>
        /// Run the cinematic. <paramref name="onMidHold"/> fires partway through the black hold (e.g. Marco
        /// grunting), <paramref name="doWhileBlack"/> at full black (swap the RV, hidden), <paramref name="onDone"/>
        /// after fade-back (Marco's completion line).
        /// </summary>
        internal static void Play(Action doWhileBlack, Action onDone, Action onMidHold = null)
        {
            ScreenTransition.Play(new TransitionRequest
            {
                Mechanism = VeilMode.Black,        // deliberate hard cut - no eyelids
                CloseSeconds = FadeTime,
                OpenSeconds = FadeTime,
                HoldSeconds = HoldTime,
                LockMovement = true,               // the overlay is purely visual; freeze the player so they can't walk/look blind
                LockCamera = true,
                DuringBlack = () => { PlaySound(); Safe(doWhileBlack); },  // repair clanks + RV swap, hidden behind the black
                OnMidHold = onMidHold,             // Marco grunts / hurts himself mid-repair (~1.25s)
                OnDone = onDone,                   // Marco's completion line
                Key = Key,
            });
        }

        /// <summary>
        /// Shorter variant for Marco's RV build-outs. Same hard cut and the same working sound, but a briefer hold:
        /// bolting a floor in is a smaller job than resurrecting a burnt-out shell, and the player will see this
        /// one up to four times. Passing null for <paramref name="doWhileBlack"/> makes it a pure visual - that is
        /// the co-op client case, where the host's broadcast applies the actual change.
        /// </summary>
        internal static void PlayUpgrade(Action doWhileBlack, Action onDone)
        {
            ScreenTransition.Play(new TransitionRequest
            {
                Mechanism = VeilMode.Black,
                CloseSeconds = FadeTime,
                OpenSeconds = FadeTime,
                HoldSeconds = UpgradeHoldTime,
                LockMovement = true,
                LockCamera = true,
                DuringBlack = () => { PlaySound(); Safe(doWhileBlack); },
                OnDone = onDone,
                Key = Key,
            });
        }

        /// <summary>Defensive un-black + input restore (call on scene load so an interrupted cinematic can't strand the player).</summary>
        internal static void ForceReset() => ScreenTransition.ForceReset(Key);

        // --- bundled repair sound (RV-specific; not part of the shared primitive) ---
        private static void PlaySound()
        {
            try
            {
                UnityEngine.AudioClip clip = EnsureClip();
                if (clip == null) return;

                // Own 2D AudioSource routed through the game's MainGameMixer, at a level under Marco's voice. The
                // game's Master and Sound Effects sliders are not mixer levels: each of its own sources multiplies
                // them into its volume, so this one does the same. 2D (spatialBlend 0) keeps it consistent + audible.
                var go = new UnityEngine.GameObject("RVRepairSound");
                go.transform.position = PlayerPos();
                var src = go.AddComponent<UnityEngine.AudioSource>();
                src.clip = clip;
                float settings = 1f;
                try
                {
                    var am = AudioManager.Instance;
                    if (am != null) settings = am.GetVolume(Il2CppScheduleOne.Core.Audio.EAudioType.FX, true);
                    else Core.Log.Warning("[Cinematic] no AudioManager - the repair sound plays at its own level.");
                }
                catch (Exception e) { Core.Log.Warning("[Cinematic] reading the volume settings failed: " + e.Message); }
                src.volume = SoundVolume * settings;
                Core.LogDebug("[Cinematic] repair sound at " + src.volume.ToString("0.000") + " (settings " + settings.ToString("0.00") + ").");
                src.spatialBlend = 0f;
                try
                {
                    var am = AudioManager.Instance;
                    if (am != null && am.MainGameMixer != null) src.outputAudioMixerGroup = am.MainGameMixer;
                }
                catch (Exception e) { Core.Swallowed("RepairCinematic.PlaySound", e); }
                src.Play();
                UnityEngine.Object.Destroy(go, clip.length + 0.3f);
            }
            catch (Exception e) { Core.Log.Warning("[Cinematic] sound failed: " + e.Message); }
        }

        private static UnityEngine.Vector3 PlayerPos()
        {
            try { var pm = PlayerSingleton<PlayerMovement>.Instance; if (pm != null) return pm.transform.position; } catch (Exception e) { Core.Swallowed("RepairCinematic.PlayerPos", e); }
            return UnityEngine.Vector3.zero;
        }

        private static UnityEngine.AudioClip EnsureClip()
        {
            if (_clipTried) return _clip;
            _clipTried = true;
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("RVRepairVan.repair.wav"))
                {
                    if (s == null) { Core.Log.Warning("[Cinematic] repair.wav resource missing"); return null; }
                    byte[] bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = s.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    _clip = WavToClip(bytes);
                }
            }
            catch (Exception e) { Core.Log.Warning("[Cinematic] clip load failed: " + e.Message); }
            return _clip;
        }

        // Minimal RIFF/WAVE parser for 16-bit PCM (what we bundle). Walks the chunks for "fmt " + "data".
        private static UnityEngine.AudioClip WavToClip(byte[] wav)
        {
            if (wav == null || wav.Length < 44) return null;
            int channels = 1, sampleRate = 22050, bits = 16, dataOffset = -1, dataLen = 0;
            int i = 12; // skip "RIFF"<size>"WAVE"
            while (i + 8 <= wav.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(wav, i, 4);
                int sz = BitConverter.ToInt32(wav, i + 4);
                int body = i + 8;
                if (id == "fmt " && body + 16 <= wav.Length)
                {
                    channels = BitConverter.ToInt16(wav, body + 2);
                    sampleRate = BitConverter.ToInt32(wav, body + 4);
                    bits = BitConverter.ToInt16(wav, body + 14);
                }
                else if (id == "data") { dataOffset = body; dataLen = sz; }
                i = body + sz + (sz & 1);   // chunks are word-aligned
            }
            if (dataOffset < 0 || bits != 16 || channels < 1) { Core.Log.Warning("[Cinematic] unsupported WAV (need 16-bit PCM)"); return null; }
            if (dataOffset + dataLen > wav.Length) dataLen = wav.Length - dataOffset;

            int total = dataLen / 2;   // 16-bit samples
            float[] samples = new float[total];
            for (int s = 0; s < total; s++)
                samples[s] = BitConverter.ToInt16(wav, dataOffset + s * 2) / 32768f;

            UnityEngine.AudioClip clip = UnityEngine.AudioClip.Create("rv_repair", total / channels, channels, sampleRate, false);
            clip.SetData((Il2CppStructArray<float>)samples, 0);
            Core.Log.Msg("[Cinematic] repair clip loaded (" + (total / channels) + " frames, " + channels + "ch, " + sampleRate + "Hz).");
            return clip;
        }

        private static void Safe(Action a)
        {
            if (a == null) return;
            try { a(); } catch (Exception e) { Core.Log.Warning("[Cinematic] callback failed: " + e.Message); }
        }
    }
}
