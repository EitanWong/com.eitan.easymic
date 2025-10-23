
namespace Eitan.EasyMic.Samples.Playback
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using UnityEngine;
    using UnityEngine.UI;
    using Eitan.EasyMic.Runtime;
    using System.Collections;

    public class EasyMicAudioPlaybackAPIExample : MonoBehaviour
    {
        [Header("Clip Playback UI")]
        [SerializeField] private Button playClipButton;
        [SerializeField] private Button pauseClipButton;
        [SerializeField] private Button resumeClipButton;
        [SerializeField] private Button stopClipButton;
        [SerializeField] private Slider clipVolumeSlider;

        [Header("Stream Playback UI")]
        [SerializeField] private Button startStreamButton;
        [SerializeField] private Button enqueueStreamButton;
        [SerializeField] private Button completeStreamButton;
        [SerializeField] private Button stopStreamButton;
        [SerializeField] private Slider streamVolumeSlider;

        [Header("Audio Settings")]
        [SerializeField] private AudioClip clipToPlay;  // 原静态播放用
        [SerializeField] private float initialClipVolume = 1f;

        [Header("Stream from Clip List Settings")]
        [SerializeField] private AudioClip[] clipList;  // 新增：可选多个 clip


        // Handle for clip playback
        private PlaybackHandle _clipHandle;

        // Handle for stream playback
        private PlaybackHandle _streamHandle;
        // Coroutine-based streaming
        private Coroutine _streamCoroutine;
        private bool _streamLoopActive = false;

        // For stream-from-clip mode
        private AudioClip _currentStreamClip;
        private float[] _streamClipSamples;
        private int _streamClipChannels;
        private int _streamClipSampleRate;
        private int _streamClipTotalSamples;

        private readonly Dictionary<AudioClip, float[]> _clipDataCache = new Dictionary<AudioClip, float[]>(32);
        private static readonly StringComparer _nameComparer = StringComparer.OrdinalIgnoreCase;

        private void Awake()
        {
            SortClipListByName();
            // Clip playback buttons
            if (playClipButton)
            {
                playClipButton.onClick.AddListener(OnPlayClip);
            }

            if (pauseClipButton)
            {
                pauseClipButton.onClick.AddListener(OnPauseClip);
            }

            if (resumeClipButton)
            {
                resumeClipButton.onClick.AddListener(OnResumeClip);
            }

            if (stopClipButton)
            {
                stopClipButton.onClick.AddListener(OnStopClip);
            }


            if (clipVolumeSlider)
            {
                clipVolumeSlider.value = initialClipVolume;
                clipVolumeSlider.onValueChanged.AddListener(OnClipVolumeChanged);
            }

            // Stream playback buttons
            if (startStreamButton)
            {
                startStreamButton.onClick.AddListener(OnStartRandomStream);
            }

            if (enqueueStreamButton)
            {
                enqueueStreamButton.onClick.AddListener(OnEnqueueChunkFromClip);
            }

            if (completeStreamButton)
            {
                completeStreamButton.onClick.AddListener(OnCompleteStream);
            }

            if (stopStreamButton)
            {
                stopStreamButton.onClick.AddListener(OnStopStream);
            }


            if (streamVolumeSlider)
            {
                streamVolumeSlider.value = initialClipVolume;
                streamVolumeSlider.onValueChanged.AddListener(OnStreamVolumeChanged);
            }
            UpdateUIInteractable();
        }


        private void OnDestroy()
        {
            if (_streamCoroutine != null)
            {
                _streamLoopActive = false;
                StopCoroutine(_streamCoroutine);
                _streamCoroutine = null;
            }
            if (playClipButton)
            {
                playClipButton.onClick.RemoveListener(OnPlayClip);
            }


            if (pauseClipButton)
            {
                pauseClipButton.onClick.RemoveListener(OnPauseClip);
            }


            if (resumeClipButton)
            {
                resumeClipButton.onClick.RemoveListener(OnResumeClip);
            }

            if (stopClipButton)
            {
                stopClipButton.onClick.RemoveListener(OnStopClip);
            }


            if (clipVolumeSlider)
            {
                clipVolumeSlider.onValueChanged.RemoveListener(OnClipVolumeChanged);
            }

            if (startStreamButton)
            {
                startStreamButton.onClick.RemoveListener(OnStartRandomStream);
            }

            if (enqueueStreamButton)
            {
                enqueueStreamButton.onClick.RemoveListener(OnEnqueueChunkFromClip);
            }

            if (completeStreamButton)
            {
                completeStreamButton.onClick.RemoveListener(OnCompleteStream);
            }

            if (stopStreamButton)
            {
                stopStreamButton.onClick.RemoveListener(OnStopStream);
            }

            if (streamVolumeSlider)
            {
                streamVolumeSlider.onValueChanged.RemoveListener(OnStreamVolumeChanged);
            }

        }

        private void UpdateUIInteractable()
        {
            // --- Clip-side state ---
            bool clipAssigned = (clipToPlay != null);
            bool clipValid = _clipHandle.IsValid;
            bool clipPlaying = clipValid && _clipHandle.IsPlaying;

            // --- Stream-side state ---
            bool listReady = false;
            if (clipList != null)
            {
                for (int i = 0; i < clipList.Length; i++)
                {
                    if (clipList[i] != null)
                    {
                        listReady = true;
                        break;
                    }
                }
            }
            bool streamLoopRunning = (_streamCoroutine != null);
            bool streamValid = _streamHandle.IsValid;

            // --- Base: Clip controls ---
            if (playClipButton)
            {
                playClipButton.interactable = clipAssigned && !clipValid;   // 只有没有活动句柄时允许重新播放
            }


            if (pauseClipButton)
            {
                pauseClipButton.interactable = clipValid && clipPlaying;     // 仅在播放中可暂停
            }


            if (resumeClipButton)
            {
                resumeClipButton.interactable = clipValid && !clipPlaying;  // 仅在暂停时可恢复
            }

            if (stopClipButton)
            {
                stopClipButton.interactable = clipValid;                    // 只要有句柄就可停止
            }


            if (clipVolumeSlider)
            {
                clipVolumeSlider.interactable = clipAssigned;               // 有可播放的 Clip 才允许调节（否则调整没有意义）
            }

            // --- Base: Stream controls ---
            if (startStreamButton)
            {
                startStreamButton.interactable = listReady && !streamLoopRunning && !streamValid; // 仅在未启动协程且没有现有句柄时可单独初始化
            }

            if (enqueueStreamButton)
            {
                enqueueStreamButton.interactable = listReady && !streamLoopRunning;                 // 仅在未运行协程时可启动连续推流
            }

            if (completeStreamButton)
            {
                completeStreamButton.interactable = streamValid && !streamLoopRunning;             // 连续推流时禁用 Complete，避免冲突
            }

            if (stopStreamButton)
            {
                stopStreamButton.interactable = streamValid || streamLoopRunning;                // 只要有活动就允许停止
            }

            if (streamVolumeSlider)
            {
                streamVolumeSlider.interactable = listReady;                                       // 有可用列表才允许调节音量
            }

            // --- Cross-mode conflict guard ---
            // 当正在做“连续流式播放”时，禁用大多数剪辑播放按钮，避免模式冲突（但保留停止剪辑的能力）

            if (streamLoopRunning)
            {
                if (playClipButton)
                {
                    playClipButton.interactable = false;
                }

                if (pauseClipButton)
                {
                    pauseClipButton.interactable = false;
                }

                if (resumeClipButton)
                {
                    resumeClipButton.interactable = false;
                }
                // 允许 stopClipButton 在 clip 有效时用于紧急停止
                if (clipVolumeSlider)
                {
                    clipVolumeSlider.interactable = clipValid; // 仅当确有活动剪辑时允许调节
                }

            }

            // 当有活动剪辑正在播放或暂停时，禁用“Start/Enqueue/Complete”以避免和剪辑播放并行冲突
            if (clipValid)
            {
                if (startStreamButton)
                {
                    startStreamButton.interactable = false;
                }

                if (enqueueStreamButton)
                {
                    enqueueStreamButton.interactable = false;
                }

                if (completeStreamButton)
                {
                    completeStreamButton.interactable = false;
                }
                // 仍然允许 stopStreamButton 用于紧急停止流式播放（若有）

            }
        }

        #region Clip Playback Methods

        private void OnPlayClip()
        {
            if (clipToPlay == null)
            {
                Debug.LogWarning("Clip to play is not assigned.");
                return;
            }

            if (_clipHandle.IsValid)
            {
                _clipHandle.Dispose();
            }

            _clipHandle = AudioPlayback.PlayClip(clipToPlay, loop: false, volume: initialClipVolume, autoDisposeOnComplete: true);
            Debug.Log("Clip playback started. Handle valid: " + _clipHandle.IsValid);

            _clipHandle.RegisterCompletedCallback(() =>
            {
                Debug.Log("Clip playback completed via callback.");
                UpdateUIInteractable();
            }, invokeIfCompleted: true);
            UpdateUIInteractable();
        }

        private void OnPauseClip()
        {
            if (_clipHandle.IsValid && _clipHandle.IsPlaying)
            {
                _clipHandle.Pause();
                Debug.Log("Clip playback paused.");
            }
            else
            {
                Debug.LogWarning("Clip playback cannot be paused (invalid handle or not playing).");
            }
            UpdateUIInteractable();
        }

        private void OnResumeClip()
        {
            if (_clipHandle.IsValid && !_clipHandle.IsPlaying)
            {
                _clipHandle.Play();
                Debug.Log("Clip playback resumed.");
            }
            else
            {
                Debug.LogWarning("Clip playback cannot be resumed (invalid handle or already playing).");
            }
            UpdateUIInteractable();
        }

        private void OnStopClip()
        {
            if (_clipHandle.IsValid)
            {
                _clipHandle.Stop();
                _clipHandle.Dispose();
                Debug.Log("Clip playback stopped and handle disposed.");
            }
            else
            {
                Debug.LogWarning("Clip playback stop request invalid (handle not valid).");
            }
            UpdateUIInteractable();
        }

        private void OnClipVolumeChanged(float value)
        {
            initialClipVolume = value;
            if (_clipHandle.IsValid)
            {
                _clipHandle.Volume = value;
            }
        }

        #endregion

        #region Stream Playback Methods (From Clip List)

        private void OnStartRandomStream()
        {
            if (clipList == null || clipList.Length == 0)
            {
                Debug.LogWarning("Clip list is empty — assign at least one AudioClip for streaming demo.");
                return;
            }

            // Pick a random clip from the list
            int idx = UnityEngine.Random.Range(0, clipList.Length);
            _currentStreamClip = clipList[idx];
            if (_currentStreamClip == null)
            {
                Debug.LogWarning("Selected stream AudioClip is null.");
                return;
            }

            // Load sample data from the clip
            _streamClipChannels = Mathf.Max(1, _currentStreamClip.channels);
            _streamClipSampleRate = Mathf.Max(8000, _currentStreamClip.frequency);
            int frames = _currentStreamClip.samples;
            _streamClipTotalSamples = frames * _streamClipChannels;
            _streamClipSamples = new float[_streamClipTotalSamples];
            bool ok = _currentStreamClip.GetData(_streamClipSamples, 0);
            if (!ok)
            {
                Debug.LogWarning("Failed to GetData from stream clip: " + _currentStreamClip.name);
                return;
            }

            // Dispose any existing handle
            if (_streamHandle.IsValid)
            {
                _streamHandle.Dispose();
            }

            // Create stream handle
            _streamHandle = AudioPlayback.CreateStream(preferredChannels: _streamClipChannels, preferredSampleRate: _streamClipSampleRate, volume: streamVolumeSlider.value, autoDisposeOnComplete: true);
            // _streamRunning = true;

            Debug.Log($"[StreamDemo] Starting stream for clip '{_currentStreamClip.name}' Channels={_streamClipChannels}, Rate={_streamClipSampleRate}, TotalSamples={_streamClipTotalSamples}.");
            UpdateUIInteractable();
        }

        private void OnEnqueueChunkFromClip()
        {
            // Start continuous streaming via coroutine when Enqueue button is clicked
            if (_streamCoroutine != null)
            {
                Debug.Log("[StreamDemo] Streaming coroutine already running.");
                return;
            }

            if (clipList == null || clipList.Length == 0)
            {
                Debug.LogWarning("Clip list is empty — assign at least one AudioClip for streaming demo.");
                return;
            }

            _streamLoopActive = true;
            _streamCoroutine = StartCoroutine(StreamClipsContinuously());
            UpdateUIInteractable();
        }
        private void OnCompleteStream()
        {
            if (!_streamHandle.IsValid)
            {
                Debug.LogWarning("Stream handle invalid.");
                return;
            }

            _streamHandle.CompleteStream();
            Debug.Log("[StreamDemo] Stream explicitly marked complete (no more data expected).");
            UpdateUIInteractable();
        }

        private void OnStopStream()
        {
            // Stop the continuous streaming coroutine if running
            if (_streamCoroutine != null)
            {
                _streamLoopActive = false;
                StopCoroutine(_streamCoroutine);
                _streamCoroutine = null;
            }

            if (_streamHandle.IsValid)
            {
                _streamHandle.Stop();
                _streamHandle.Dispose();
                // _streamRunning = false;
                Debug.Log("[StreamDemo] Stream playback stopped and handle disposed.");
            }
            else
            {
                Debug.LogWarning("Stop requested but stream handle invalid.");
            }
            UpdateUIInteractable();
        }
        private void OnStreamVolumeChanged(float value)
        {
            if (_streamHandle.IsValid)
            {
                _streamHandle.Volume = value;
                Debug.Log($"[StreamDemo] Stream volume set to {value:F2}");
            }
        }

        #endregion

        private static int MsToSamples(float ms, int sampleRate)
        {
            return Mathf.Max(1, Mathf.RoundToInt(ms * 0.001f * sampleRate));
        }

        private static float[] CopyHeadWithFadeOut(float[] src, int channels, int sampleRate, float headMs, float fadeOutMs)
        {
            int headFrames = Mathf.Max(1, MsToSamples(headMs, sampleRate));
            int totalFrames = src.Length / Mathf.Max(1, channels);
            headFrames = Mathf.Clamp(headFrames, 1, totalFrames);
            int headSamples = headFrames * channels;

            var dst = new float[headSamples];
            Array.Copy(src, 0, dst, 0, headSamples);

            // Apply a tiny linear fade-out at the tail to avoid clicks
            int fadeFrames = Mathf.Clamp(MsToSamples(fadeOutMs, sampleRate), 1, headFrames);
            int fadeSamples = fadeFrames * channels;
            for (int s = 0; s < fadeSamples; s++)
            {
                int idx = headSamples - 1 - s;
                if (idx < 0)
                {
                    break;
                }


                float t = (float)s / fadeSamples; // 0..1
                dst[idx] *= (1f - t);             // 1..0
            }
            return dst;
        }

        private IEnumerator StreamClipsContinuously()
        {
            UpdateUIInteractable();

            // —— Hardcoded musical choices for demo (no Inspector exposure)
            const bool USE_PENTATONIC = true; // constrain to major pentatonic if names parse
            const int ROOT_MIDI = 60;         // C4
            const double BUFFER_HIGH = 0.15;  // keep queue responsive

            // 0) Gather usable clips (name-sorted in Awake)
            var usable = new List<int>();
            if (clipList != null)
            {
                for (int i = 0; i < clipList.Length; i++)
                {
                    if (clipList[i] != null)
                    {
                        usable.Add(i);
                    }

                }
            }
            if (usable.Count == 0)
            {
                Debug.LogWarning("No valid clips to stream.");
                _streamCoroutine = null;
                UpdateUIInteractable();
                yield break;
            }

            // 1) Try parse MIDI from names and filter to major pentatonic (if possible)
            var midiNums = new List<int>(usable.Count);
            bool anyMidi = false;
            for (int k = 0; k < usable.Count; k++)
            {
                var c = clipList[usable[k]];
                if (TryParseMidiFromName(c.name, out int midi)) { midiNums.Add(midi); anyMidi = true; }
                else { midiNums.Add(int.MinValue); }
            }

            var allowed = new List<int>();
            if (USE_PENTATONIC && anyMidi)
            {
                for (int k = 0; k < usable.Count; k++)
                {
                    int midi = midiNums[k];
                    if (midi != int.MinValue && InMajorPentatonic(midi, ROOT_MIDI))
                    {
                        allowed.Add(usable[k]);
                    }
                }
            }
            if (allowed.Count == 0)
            {
                allowed = new List<int>(usable); // fallback
            }

            // 2) Sort by pitch when available; otherwise by name (already normalized)
            allowed.Sort((i, j) =>
            {
                if (anyMidi)
                {
                    int mi = midiNums[usable.IndexOf(i)];
                    int mj = midiNums[usable.IndexOf(j)];
                    if (mi != int.MinValue && mj != int.MinValue)
                    {
                        return mi.CompareTo(mj);
                    }
                }
                return _nameComparer.Compare(clipList[i].name, clipList[j].name);
            });

            int N = allowed.Count;
            var rng = new System.Random();

            // 3) Simple Markov matrix favoring repeat + neighbors
            var P = new float[N, N];
            for (int i = 0; i < N; i++)
            {
                for (int j = 0; j < N; j++)
                {
                    if (i == j)
                    {
                        P[i, j] = 0.35f; // repetition
                    }
                    else { int di = Mathf.Abs(j - i); P[i, j] = (di == 1) ? 0.30f : (di == 2 ? 0.20f : 0.05f); }
                }
                float s = 0f; for (int j = 0; j < N; j++)
                {
                    s += P[i, j];
                }

                if (s > 0f)
                {
                    for (int j = 0; j < N; j++)
                    {
                        P[i, j] /= s; // normalize
                    }
                }

            }

            // 4) Ensure/Reuse stream handle (lock format to the first allowed clip)
            if (!_streamHandle.IsValid)
            {
                var first = clipList[allowed[0]];
                int ch = Mathf.Max(1, first.channels);
                int sr = Mathf.Max(8000, first.frequency);
                _streamHandle = AudioPlayback.CreateStream(ch, sr, streamVolumeSlider ? streamVolumeSlider.value : 1f, autoDisposeOnComplete: true);
            }

            int cur = rng.Next(0, N);
            int swingParity = 0;   // even/odd subdivision for swing
            int onsetCounter = 0;  // to inject occasional longer gaps (phrase breaks)

            // —— Dynamic timing design ——
            // Much slower, groovier: tempo drifts with Perlin noise; swing stronger at slow tempos.
            const float BPM_MIN = 48f;   // slower bound (more swing, longer IOI)
            const float BPM_MAX = 72f;   // faster bound (less swing, shorter IOI)
            const float SWING_SLOW = 0.68f; // stronger long:short at slow tempo
            const float SWING_FAST = 0.58f; // still some swing at faster bound
            const float JITTER_MS = 7f;     // gentle micro-jitter (+/-)

            // Variation: sometimes play the WHOLE clip instead of a short head
            const float FULL_NOTE_PROB = 0.25f;   // 25% of onsets use full sample

            while (_streamLoopActive)
            {
                // Keep buffer small for responsiveness
                while (_streamHandle.IsValid && _streamHandle.BufferedSeconds > BUFFER_HIGH)
                {
                    yield return null;
                }

                // Choose next note (Markov)
                int next = WeightedNextIndex(cur, P, rng);
                cur = next;

                int idx = allowed[cur];
                var clip = clipList[idx];
                if (clip == null) { yield return null; continue; }

                // Cache full clip samples once per clip
                if (!_clipDataCache.TryGetValue(clip, out var full))
                {
                    int ch = Mathf.Max(1, clip.channels);
                    int sr = Mathf.Max(8000, clip.frequency);

                    // If format differs and buffer is almost empty, rebuild handle to match
                    if (_streamHandle.IsValid && _streamHandle.BufferedSeconds < 0.02)
                    {
                        _streamHandle.Stop();
                        _streamHandle.Dispose();
                        _streamHandle = AudioPlayback.CreateStream(ch, sr, streamVolumeSlider ? streamVolumeSlider.value : 1f, autoDisposeOnComplete: true);
                    }

                    int frames = clip.samples; int total = frames * ch;
                    if (total <= 0 || !clip.GetData(full = new float[total], 0)) { yield return null; continue; }
                    _clipDataCache[clip] = full;
                }

                // —— Compute dynamic IOI (inter-onset interval) ——
                float t = Time.time; // global phase for smooth noise
                float tempoNoise = Mathf.PerlinNoise(t * 0.07f, 0f); // very low frequency drift
                float tempoBpm = Mathf.Lerp(BPM_MIN, BPM_MAX, tempoNoise);
                float ioiBase = 60f / Mathf.Max(1f, tempoBpm) / 4f; // 16th-note IOI (seconds)

                // Tempo-dependent swing within an 8th-note pair
                float tempo01 = Mathf.InverseLerp(BPM_MIN, BPM_MAX, tempoBpm);
                float swing = Mathf.Lerp(SWING_SLOW, SWING_FAST, tempo01);
                float ioiThis = (swingParity % 2 == 0) ? ioiBase * (2f * swing) : ioiBase * (2f * (1f - swing));

                // Add micro-jitter and clamp to a slower minimum
                float jitterSec = (Mathf.PerlinNoise(t * 0.9f, 10f) - 0.5f) * 2f * (JITTER_MS / 1000f);
                ioiThis = Mathf.Max(0.42f, ioiThis + jitterSec); // never too short; encourage slower pulse

                // Split into a short staccato head + rest (default), but occasionally play FULL sample for variation
                float headRatio = Mathf.Lerp(0.50f, 0.62f, Mathf.PerlinNoise(t * 0.19f, 4.2f));
                float headSec = ioiThis * headRatio;
                float restSec = Mathf.Max(0.01f, ioiThis - headSec);

                // Phrase break: every ~8 onsets, leave a longer gap
                if ((onsetCounter % 8) == 7)
                {
                    restSec *= UnityEngine.Random.Range(1.35f, 1.85f);
                }

                // Enqueue note: either full clip or short head with tiny fade-out; do NOT mark EOS
                int channels = Mathf.Max(1, clip.channels);
                int sampleRate = Mathf.Max(8000, clip.frequency);
                bool playFull = rng.NextDouble() < FULL_NOTE_PROB;
                if (playFull)
                {
                    _streamHandle.Enqueue(full, full.Length, channels, sampleRate, false);
                }
                else
                {
                    var chunk = CopyHeadWithFadeOut(full, channels, sampleRate, headSec * 1000f, 8f);
                    _streamHandle.Enqueue(chunk, chunk.Length, channels, sampleRate, false);
                }

                // Wait dynamically before scheduling the next onset
                yield return new WaitForSeconds(restSec);
                swingParity++;
                onsetCounter++;
            }

            _streamCoroutine = null;
            UpdateUIInteractable();
            Debug.Log("[StreamDemo] Dynamic staccato/full-note streaming coroutine finished.");
        }

        private void SortClipListByName()
        {
            if (clipList == null || clipList.Length == 0)
            {
                return;
            }
            // Move nulls to the end and sort non-nulls by name (case-insensitive)

            Array.Sort(clipList, (a, b) =>
            {
                if (a == b)
                {
                    return 0;
                }

                if (a == null)
                {
                    return 1;
                }


                if (b == null)
                {
                    return -1;
                }


                return _nameComparer.Compare(a.name, b.name);
            });
        }

        private static bool TryParseMidiFromName(string name, out int midi)
        {
            // Accept patterns like C4, C#4, Db3, A0, etc. (piano range A0..C8 ~ 21..108)
            midi = 0;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var m = Regex.Match(name, @"(?i)([A-G])([#b]?)(-?\d{1,2})");
            if (!m.Success)
            {
                return false;
            }


            int pc;
            switch (char.ToUpperInvariant(m.Groups[1].Value[0]))
            {
                case 'C': pc = 0; break;
                case 'D': pc = 2; break;
                case 'E': pc = 4; break;
                case 'F': pc = 5; break;
                case 'G': pc = 7; break;
                case 'A': pc = 9; break;
                case 'B': pc = 11; break;
                default: return false;
            }
            var acc = m.Groups[2].Value;
            if (acc == "#")
            {
                pc += 1;
            }
            else if (acc == "b" || acc == "B")
            {
                pc -= 1;
            }

            if (pc < 0)
            {
                pc += 12;
            }

            if (pc >= 12)
            {
                pc -= 12;
            }


            if (!int.TryParse(m.Groups[3].Value, out int octave))
            {
                return false;
            }
            // MIDI: C4 = 60 => 12*(octave+1) + pc

            midi = (octave + 1) * 12 + pc;
            return midi >= 0 && midi <= 127;
        }

        private static bool InMajorPentatonic(int midi, int rootMidi)
        {
            int[] degrees = { 0, 2, 4, 7, 9 }; // relative to root (pitch class)
            int pc = (midi - rootMidi) % 12; if (pc < 0)
            {
                pc += 12;
            }

            for (int i = 0; i < degrees.Length; i++)
            {
                if (pc == degrees[i])
                {
                    return true;
                }
            }


            return false;
        }

        private static List<bool> BjorklundPattern(int pulses, int steps)
        {
            // Minimal Bjorklund implementation -> boolean trigger pattern
            pulses = Mathf.Clamp(pulses, 1, Mathf.Max(1, steps));
            steps = Mathf.Max(1, steps);
            int divisor = steps - pulses;
            var counts = new List<int>();
            var remainders = new List<int> { pulses };
            int level = 0;
            while (true)
            {
                counts.Add(divisor / remainders[level]);
                remainders.Add(divisor % remainders[level]);
                divisor = remainders[level];
                level++;
                if (remainders[level] <= 1)
                {
                    counts.Add(divisor);
                    break;
                }
            }
            var pattern = new List<int>();
            void Build(int lvl)
            {
                if (lvl == -1)
                {
                    pattern.Add(0);
                }

                else if (lvl == -2)
                {
                    pattern.Add(1);
                }

                else
                {
                    for (int i = 0; i < counts[lvl]; i++)
                    {
                        Build(lvl - 1);
                    }

                    if (remainders[lvl] > 0)
                    {
                        Build(lvl - 2);
                    }
                }
            }
            Build(level);
            var outp = new List<bool>(steps);
            for (int i = 0; i < pattern.Count; i++)
            {
                outp.Add(pattern[i] == 1);
            }


            return outp;
        }

        private static int WeightedNextIndex(int cur, float[,] P, System.Random rng)
        {
            float r = (float)rng.NextDouble();
            float acc = 0f;
            int n = P.GetLength(1);
            for (int j = 0; j < n; j++)
            {
                acc += P[cur, j]; if (r <= acc)
                {
                    return j;
                }
            }
            return cur;
        }
    }
}