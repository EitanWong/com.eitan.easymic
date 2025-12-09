
namespace Eitan.EasyMic.Samples.Playback
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UI;
    using UnityEngine.EventSystems;
    using Eitan.EasyMic.Runtime;

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

        [Header("Hold Enqueue")]
        [SerializeField] private float holdTargetBufferSeconds = 0.35f; // target buffer while holding
        [SerializeField] private float holdPollIntervalSeconds = 0.02f;  // poll cadence while holding

        [Header("Audio Settings")]
        [SerializeField] private AudioClip clipToPlay;  // 原静态播放用
        [SerializeField] private float initialClipVolume = 1f;

        [Header("Stream from Clip List Settings")]
        [SerializeField] private AudioClip[] clipList;  // 可选多个 clip

        // --- Constants for simplified streaming ---
        private const int DEFAULT_CHUNK_MS = 250;  // 每次点击 Enqueue 发送 ~250ms 片段
        private const int DEFAULT_FADE_MS = 8;     // 简短淡出以避免点击音

        // Clip playback handle
        private PlaybackHandle _clipHandle;

        // Stream playback handle + current stream format
        private PlaybackHandle _streamHandle;
        // private int _streamChannels;
        // private int _streamSampleRate;
        private bool _isHoldingEnqueue;
        private Coroutine _holdCoroutine;
        private bool _streamMarkedComplete;

        // Cache clip sample data to avoid repeated allocations/copies
        private readonly Dictionary<AudioClip, float[]> _clipDataCache = new Dictionary<AudioClip, float[]>(32);
        private static readonly StringComparer _nameComparer = StringComparer.OrdinalIgnoreCase;
        private readonly System.Random _rng = new System.Random();

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

            // Enable long-press hold to continuously enqueue
            if (enqueueStreamButton)
            {
                var trigger = enqueueStreamButton.GetComponent<EventTrigger>();
                if (trigger == null)
                {
                    trigger = enqueueStreamButton.gameObject.AddComponent<EventTrigger>();
                }
                AddEventTrigger(trigger, EventTriggerType.PointerDown, OnEnqueuePointerDown);
                AddEventTrigger(trigger, EventTriggerType.PointerUp, OnEnqueuePointerUp);
                AddEventTrigger(trigger, EventTriggerType.PointerExit, OnEnqueuePointerUp);
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

            // stop hold loop if active
            _isHoldingEnqueue = false;
            if (_holdCoroutine != null)
            {
                StopCoroutine(_holdCoroutine);
                _holdCoroutine = null;
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
                    if (clipList[i] != null) { listReady = true; break; }
                }
            }
            bool streamValid = _streamHandle.IsValid;

            // --- Clip controls ---
            if (playClipButton)
            {
                playClipButton.interactable = clipAssigned && !clipValid;
            }


            if (pauseClipButton)
            {
                pauseClipButton.interactable = clipValid && clipPlaying;
            }


            if (resumeClipButton)
            {
                resumeClipButton.interactable = clipValid && !clipPlaying;
            }

            if (stopClipButton)
            {
                stopClipButton.interactable = clipValid;
            }


            if (clipVolumeSlider)
            {
                clipVolumeSlider.interactable = clipAssigned;
            }

            // --- Stream controls (simplified) ---
            if (startStreamButton)
            {
                startStreamButton.interactable = listReady && !streamValid; // 初始化流（可选）
            }

            if (enqueueStreamButton)
            {
                enqueueStreamButton.interactable = listReady;              // 每次点击追加一次
            }

            if (completeStreamButton)
            {
                completeStreamButton.interactable = streamValid;         // 有流才允许 Complete
            }

            if (stopStreamButton)
            {
                stopStreamButton.interactable = streamValid;                 // 有流才允许 Stop
            }

            if (streamVolumeSlider)
            {
                streamVolumeSlider.interactable = listReady;
            }

            // 当有活动剪辑时，禁用流式的初始化/完成按钮，避免冲突（允许停止流）

            if (clipValid)
            {
                if (startStreamButton)
                {
                    startStreamButton.interactable = false;
                }

                if (completeStreamButton)
                {
                    completeStreamButton.interactable = false;
                }

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


            _clipHandle = AudioPlayback.PlayClip(clipToPlay, loop: false, volume: initialClipVolume);
            Debug.Log("Clip playback started. Handle valid: " + _clipHandle.IsValid);

            _clipHandle.RegisterCompletedCallback(() =>
            {
                Debug.Log("Clip playback completed via callback.");
                UpdateUIInteractable();
            });
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

        #region Stream Playback Methods (Simplified)

        // 可选：预先根据一个可用的 clip 初始化流（如果已有流则重置）
        private void OnStartRandomStream()
        {
            if (clipList == null || clipList.Length == 0)
            {
                Debug.LogWarning("Clip list is empty — assign at least one AudioClip for streaming demo.");
                return;
            }

            AudioClip first = null;
            for (int i = 0; i < clipList.Length; i++)
            {
                if (clipList[i] != null) { first = clipList[i]; break; }
            }
            if (first == null)
            {
                Debug.LogWarning("No valid AudioClip found in list.");
                return;
            }

            if (_streamHandle.IsValid)
            {
                _streamHandle.Stop();
                _streamHandle.Dispose();
            }

            var _streamChannels = Mathf.Max(1, first.channels);
            var _streamSampleRate = Mathf.Max(8000, first.frequency);
            _streamHandle = AudioPlayback.CreateStream(streamVolumeSlider ? streamVolumeSlider.value : 1f);
            _streamMarkedComplete = false;

            Debug.Log($"[StreamDemo] Stream initialized. Channels={_streamChannels}, Rate={_streamSampleRate}.");
            UpdateUIInteractable();
        }

        // 点击一次，仅追加一次来自随机 clip 的随机短片段
        private void OnEnqueueChunkFromClip()
        {
            if (clipList == null || clipList.Length == 0)
            {
                Debug.LogWarning("Clip list is empty — assign at least one AudioClip for streaming demo.");
                return;
            }

            // 1) 选择一个非空 clip
            AudioClip clip = null;
            for (int safety = 0; safety < 16 && clip == null; safety++)
            {
                int idx = UnityEngine.Random.Range(0, clipList.Length);
                clip = clipList[idx];
            }
            if (clip == null)
            {
                Debug.LogWarning("No valid AudioClip found to enqueue.");
                return;
            }

            int ch = Mathf.Max(1, clip.channels);
            int sr = Mathf.Max(8000, clip.frequency);

            int totalFrames = clip.samples;
            // If previously marked complete, start a fresh stream before enqueuing again
            if (_streamMarkedComplete)
            {
                if (_streamHandle.IsValid)
                {
                    _streamHandle.Stop();
                    _streamHandle.Dispose();
                }
                // _streamChannels = ch;
                // _streamSampleRate = sr;
                _streamHandle = AudioPlayback.CreateStream(streamVolumeSlider ? streamVolumeSlider.value : 1f);
                _streamMarkedComplete = false;
            }

            // 2) 确保有与所选 clip 格式一致的 stream 句柄；若格式不同且缓冲足够小则重建
            if (!_streamHandle.IsValid)
            {
                // _streamChannels = ch;
                // _streamSampleRate = sr;
                _streamHandle = AudioPlayback.CreateStream(streamVolumeSlider ? streamVolumeSlider.value : 1f);
            }
            // else if (ch != _streamChannels || sr != _streamSampleRate)
            // {
            //     if (_streamHandle.BufferedSeconds < 0.02f)
            //     {
            //         _streamHandle.Stop();
            //         _streamHandle.Dispose();
            //         // _streamChannels = ch;
            //         // _streamSampleRate = sr;
            //         _streamHandle = AudioPlayback.CreateStream( streamVolumeSlider ? streamVolumeSlider.value : 1f);
            //     }
            //     else
            //     {
            //         Debug.LogWarning($"[StreamDemo] Stream format mismatch (cur={_streamChannels}/{_streamSampleRate}, clip={ch}/{sr}). Wait for buffer to drain or press Stop.");
            //         return;
            //     }
            // }

            // 3) 获取/缓存该 clip 的全量采样
            if (!_clipDataCache.TryGetValue(clip, out var full))
            {
                if (totalFrames <= 0)
                {
                    Debug.LogWarning($"[StreamDemo] Clip '{clip.name}' has no samples.");
                    return;
                }
                full = new float[totalFrames];
                bool ok = clip.GetData(full, 0);
                if (!ok)
                {
                    Debug.LogWarning($"[StreamDemo] GetData failed for clip '{clip.name}'. Ensure 'Decompress On Load'.");
                    return;
                }
                _clipDataCache[clip] = full;
            }

            // 4) 从随机位置截取一个固定时长的片段
            // int chunkFrames = MsToSamples(DEFAULT_CHUNK_MS, sr);
            // int totalFramesInClip = Mathf.Max(1, full.Length / ch);
            // chunkFrames = Mathf.Clamp(chunkFrames, 1, totalFramesInClip);
            // int maxStartFrame = Mathf.Max(0, totalFramesInClip - chunkFrames);
            // int startFrame = (maxStartFrame > 0) ? _rng.Next(0, maxStartFrame + 1) : 0;

            // int startSamples = startFrame * ch;
            // int chunkSamples = Mathf.Min(chunkFrames * ch, full.Length - startSamples);

            // int fadeSamples = Mathf.Clamp(MsToSamples(DEFAULT_FADE_MS, sr) * ch, 0, chunkSamples);

            _streamHandle.Enqueue(full, totalFrames, ch, sr, false);

            Debug.Log($"[StreamDemo] Enqueued {totalFrames} frames from '{clip.name}' @ {ch}ch/{sr}Hz .");
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
            _streamMarkedComplete = true;
            Debug.Log("[StreamDemo] Stream explicitly marked complete (no more data expected).");
            UpdateUIInteractable();
        }

        private void OnStopStream()
        {
            if (_streamHandle.IsValid)
            {
                _streamHandle.Stop();
                _streamHandle.Dispose();
                Debug.Log("[StreamDemo] Stream playback stopped and handle disposed.");
                _streamMarkedComplete = false;
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

        // --- Helpers ---
        private static int MsToSamples(float ms, int sampleRate)
        {
            return Mathf.Max(1, Mathf.RoundToInt(ms * 0.001f * sampleRate));
        }

        private void SortClipListByName()
        {
            if (clipList == null || clipList.Length == 0)
            {
                return;
            }


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

        // --- Hold Enqueue EventTrigger helpers and coroutine ---
        private static void AddEventTrigger(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            trigger.triggers.Add(entry);
        }

        private void OnEnqueuePointerDown(BaseEventData _)
        {
            _isHoldingEnqueue = true;
            if (_holdCoroutine == null)
            {
                _holdCoroutine = StartCoroutine(HoldEnqueueLoop());
            }
        }

        private void OnEnqueuePointerUp(BaseEventData _)
        {
            _isHoldingEnqueue = false;
            if (_holdCoroutine != null)
            {
                StopCoroutine(_holdCoroutine);
                _holdCoroutine = null;
            }
        }

        private IEnumerator HoldEnqueueLoop()
        {
            while (_isHoldingEnqueue)
            {
                float buffered = (_streamHandle.IsValid) ? (float)_streamHandle.BufferedSeconds : 0f;
                if (buffered < holdTargetBufferSeconds)
                {
                    OnEnqueueChunkFromClip();
                }
                yield return new WaitForSeconds(holdPollIntervalSeconds);
            }
        }
    }
}
