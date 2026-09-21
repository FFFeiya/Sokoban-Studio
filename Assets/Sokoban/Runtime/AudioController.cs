using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sokoban
{
    /// <summary>One procedurally synthesized sound effect target.</summary>
    public enum Sfx
    {
        Move = 0,
        Push = 1,
        Undo = 2,
        DoorOpen = 3,
        DoorClose = 4,
        Goal = 5,
        Complete = 6,
        UiClick = 7
    }

    /// <summary>
    /// Strictly additive audio feedback layer. It mirrors <see cref="BoardView"/>'s Build/Refresh
    /// interface so the bootstrap can call it from the exact same hook points, diffs the derived
    /// board state against the last snapshot and plays a short procedurally generated cue for each
    /// change. It never feeds anything back into the model and never gates gameplay: the
    /// <see cref="Board"/> remains the only rule authority.
    /// Every clip is synthesized in memory via <see cref="AudioClip.Create"/> + <c>SetData</c> (no
    /// imported asset, no <c>Resources.Load</c>, no <c>AssetDatabase</c>), and all synthesis and
    /// playback is wrapped defensively so any failure degrades to silence instead of breaking play
    /// or a test.
    /// </summary>
    public class AudioController : MonoBehaviour
    {
        /// <summary>Most recently enabled instance, or null when none is active. Never required.</summary>
        public static AudioController Instance { get; private set; }

        /// <summary>Sample rate of every synthesized clip.</summary>
        private const int SampleRate = 44100;

        /// <summary>Baseline move counter captured by Build/Refresh, diffed against on the next Refresh.</summary>
        private int _lastMoveCount;

        /// <summary>Baseline push counter captured by Build/Refresh, diffed against on the next Refresh.</summary>
        private int _lastPushCount;

        /// <summary>Baseline open/closed state of every door cell, keyed by board coordinate.</summary>
        private readonly Dictionary<(int x, int y), bool> _lastDoorOpen =
            new Dictionary<(int x, int y), bool>();

        /// <summary>Baseline set of box cells that also carry a Goal tile.</summary>
        private readonly HashSet<(int x, int y)> _lastBoxOnGoal = new HashSet<(int x, int y)>();

        /// <summary>Baseline completion flag captured by Build/Refresh.</summary>
        private bool _lastComplete;

        /// <summary>False until the first Build/Refresh captures a baseline, so no cue fires against an empty one.</summary>
        private bool _hasBaseline;

        private readonly Dictionary<Sfx, AudioClip> _clips = new Dictionary<Sfx, AudioClip>();
        private AudioSource _source;
        private bool muted;

        private void Awake()
        {
            // The source is provisioned lazily and defensively: a missing audio device or a stripped
            // audio module must never break the scene, so nothing here may throw.
            try
            {
                _source = GetComponent<AudioSource>();
                if (_source == null)
                {
                    _source = gameObject.AddComponent<AudioSource>();
                }

                if (_source != null)
                {
                    _source.playOnAwake = false;
                    _source.loop = false;
                    _source.spatialBlend = 0f;
                }

                // The gameplay camera carries no AudioListener, so synthesized cues would otherwise be
                // inaudible. Provision exactly one (guarded against a second listener elsewhere).
                if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null)
                {
                    gameObject.AddComponent<AudioListener>();
                }
            }
            catch (Exception)
            {
                _source = null;
            }
        }

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>M toggles the mute; purely a feedback-layer convenience with no gameplay effect.</summary>
        private void Update()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.M))
                {
                    muted = !muted;
                }
            }
            catch (Exception)
            {
                // Input is unavailable in some batch contexts; the toggle simply does nothing.
            }
        }

        /// <summary>Plays one synthesized cue. A no-op when muted or when the clip is unavailable.</summary>
        public void Play(Sfx sfx)
        {
            if (muted)
            {
                return;
            }

            try
            {
                if (_source == null)
                {
                    _source = GetComponent<AudioSource>();
                    if (_source == null)
                    {
                        _source = gameObject.AddComponent<AudioSource>();
                    }

                    if (_source != null)
                    {
                        _source.playOnAwake = false;
                        _source.spatialBlend = 0f;
                    }
                }

                AudioClip clip = GetClip(sfx);
                if (clip == null || _source == null)
                {
                    return;
                }

                _source.PlayOneShot(clip);
            }
            catch (Exception)
            {
                // Audio is strictly additive: any failure is swallowed and play continues silently.
            }
        }

        /// <summary>UI convenience for the button layer; null-safe via the instance check at the call site.</summary>
        public void PlayUiClick()
        {
            Play(Sfx.UiClick);
        }

        /// <summary>
        /// Captures the derived board state as the new baseline without playing anything. Called on
        /// the initial build and on Restart, so the first Refresh afterwards diffs against the board
        /// as it was just built.
        /// </summary>
        public void Build(Board board)
        {
            try
            {
                CaptureBaseline(board);
            }
            catch (Exception)
            {
                _hasBaseline = false;
            }
        }

        /// <summary>
        /// Diffs the current board state against the baseline, plays the matching cue for each
        /// change (push/move/undo, door open/close, box landing on a goal, completion), then captures
        /// the new baseline. Called after each accepted move and after Undo.
        /// </summary>
        public void Refresh(Board board)
        {
            try
            {
                if (board == null)
                {
                    return;
                }

                if (_hasBaseline)
                {
                    Diff(board);
                }

                CaptureBaseline(board);
            }
            catch (Exception)
            {
                // A diff/synthesis failure must never surface into gameplay or a test.
                _hasBaseline = false;
            }
        }

        /// <summary>Plays the cues implied by comparing the live board against the captured baseline.</summary>
        private void Diff(Board board)
        {
            // Movement family: a push outranks a plain move, and a counter that went backwards is an undo.
            if (board.PushCount > _lastPushCount)
            {
                Play(Sfx.Push);
            }
            else if (board.MoveCount > _lastMoveCount)
            {
                Play(Sfx.Move);
            }
            else if (board.MoveCount < _lastMoveCount)
            {
                Play(Sfx.Undo);
            }

            // Door open/close edges. Door state is derived and re-read from the board every time;
            // only coordinates present in both snapshots can produce an edge.
            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    if (board.GetTile(x, y) != TileType.Door)
                    {
                        continue;
                    }

                    bool open = board.IsDoorOpen(x, y);
                    if (_lastDoorOpen.TryGetValue((x, y), out bool wasOpen) && wasOpen != open)
                    {
                        Play(open ? Sfx.DoorOpen : Sfx.DoorClose);
                    }
                }
            }

            // A box cell that is newly covered by a goal cue fires one chime.
            foreach ((int x, int y) in board.BoxPositions)
            {
                if (board.GetTile(x, y) == TileType.Goal && !_lastBoxOnGoal.Contains((x, y)))
                {
                    Play(Sfx.Goal);
                }
            }

            // Completion is a one-shot false -> true edge.
            if (board.IsComplete && !_lastComplete)
            {
                Play(Sfx.Complete);
            }
        }

        /// <summary>Records the current derived board state as the baseline; never plays a sound.</summary>
        private void CaptureBaseline(Board board)
        {
            if (board == null)
            {
                _hasBaseline = false;
                return;
            }

            _lastMoveCount = board.MoveCount;
            _lastPushCount = board.PushCount;

            _lastDoorOpen.Clear();
            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    if (board.GetTile(x, y) == TileType.Door)
                    {
                        _lastDoorOpen[(x, y)] = board.IsDoorOpen(x, y);
                    }
                }
            }

            _lastBoxOnGoal.Clear();
            foreach ((int x, int y) in board.BoxPositions)
            {
                if (board.GetTile(x, y) == TileType.Goal)
                {
                    _lastBoxOnGoal.Add((x, y));
                }
            }

            _lastComplete = board.IsComplete;
            _hasBaseline = true;
        }

        /// <summary>Returns the cached clip for a target, synthesizing it on first use. Null on failure.</summary>
        private AudioClip GetClip(Sfx sfx)
        {
            if (_clips.TryGetValue(sfx, out AudioClip cached) && cached != null)
            {
                return cached;
            }

            AudioClip clip;
            try
            {
                clip = Synthesize(sfx);
            }
            catch (Exception)
            {
                clip = null;
            }

            if (clip != null)
            {
                _clips[sfx] = clip;
            }

            return clip;
        }

        /// <summary>Builds one of the eight clips in memory; all are pure sine/triangle/square tones.</summary>
        private static AudioClip Synthesize(Sfx sfx)
        {
            switch (sfx)
            {
                case Sfx.Move:
                    return FromSamples("SfxMove", Sweep(0.05f, 220f, 220f, 8f, 0.25f, false));
                case Sfx.Push:
                    return FromSamples("SfxPush", Sweep(0.09f, 130f, 130f, 6f, 0.35f, false));
                case Sfx.Undo:
                    return FromSamples("SfxUndo", Sweep(0.14f, 420f, 210f, 5f, 0.28f, false));
                case Sfx.DoorOpen:
                    return FromSamples("SfxDoorOpen", Sweep(0.15f, 300f, 620f, 4f, 0.28f, false));
                case Sfx.DoorClose:
                    return FromSamples("SfxDoorClose", Sweep(0.15f, 620f, 300f, 4f, 0.28f, false));
                case Sfx.Goal:
                    return FromSamples("SfxGoal", Sweep(0.12f, 880f, 880f, 6f, 0.30f, false));
                case Sfx.Complete:
                    return FromSamples("SfxComplete", CompleteMotif());
                case Sfx.UiClick:
                    return FromSamples("SfxUiClick", Sweep(0.005f, 1500f, 1500f, 14f, 0.25f, true));
                default:
                    return null;
            }
        }

        /// <summary>
        /// One tone with a linear frequency sweep and an exponential decay envelope (exp(-decay * t/duration),
        /// matching the project's -6 dB-per-duration convention at decay = 6). Samples are clamped to [-1, 1].
        /// </summary>
        private static float[] Sweep(float duration, float startHz, float endHz, float decay, float amplitude, bool square)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[count];
            double phase = 0.0;

            for (int i = 0; i < count; i++)
            {
                float u = count > 1 ? (float)i / (count - 1) : 0f;
                float frequency = Mathf.Lerp(startHz, endHz, u);
                phase += frequency / SampleRate;

                float wave = square
                    ? Mathf.Sign(Mathf.Sin((float)(phase * 2.0 * Math.PI)))
                    : Mathf.Sin((float)(phase * 2.0 * Math.PI));

                float envelope = Mathf.Exp(-decay * u);
                data[i] = Mathf.Clamp(wave * amplitude * envelope, -1f, 1f);
            }

            return data;
        }

        /// <summary>Three ascending notes (C5/E5/G5) concatenated into a short completion motif.</summary>
        private static float[] CompleteMotif()
        {
            float[][] notes =
            {
                Sweep(0.11f, 523f, 523f, 4f, 0.30f, false),
                Sweep(0.11f, 659f, 659f, 4f, 0.30f, false),
                Sweep(0.11f, 784f, 784f, 4f, 0.30f, false)
            };

            int total = 0;
            foreach (float[] note in notes)
            {
                total += note.Length;
            }

            var data = new float[total];
            int offset = 0;
            foreach (float[] note in notes)
            {
                Array.Copy(note, 0, data, offset, note.Length);
                offset += note.Length;
            }

            return data;
        }

        /// <summary>Wraps a sample buffer into a mono, non-streaming runtime clip.</summary>
        private static AudioClip FromSamples(string name, float[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return null;
            }

            AudioClip clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            if (clip == null)
            {
                return null;
            }

            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>Small, discoverable mute indicator kept clear of the top-left HUD.</summary>
        private void OnGUI()
        {
            try
            {
                GuiPanel.ApplyCjkFont();

                const float width = 150f;
                var rect = new Rect(Screen.width - width - 6f, 4f, width, 24f);
                GuiPanel.DrawBacking(rect, new Color(0f, 0f, 0f, 0.45f));
                GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, width - 12f, 20f), muted ? "M 音效：关" : "M 音效：开", GuiPanel.HudTextStyle);
            }
            catch (Exception)
            {
                // A GUI failure in the indicator must never break gameplay.
            }
        }
    }
}
