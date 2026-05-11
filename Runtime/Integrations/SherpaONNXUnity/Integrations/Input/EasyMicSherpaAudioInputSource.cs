#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using System.Collections.Generic;
using System.Threading;
using Eitan.EasyMic;
using Eitan.EasyMic.Runtime;
using Eitan.Sherpa.Onnx.Unity.Mono.Inputs;
using UnityEngine;
using UnityEngine.Events;

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.Input
{
    /// <summary>
    /// Bridges EasyMic capture into SherpaONNXUnity's native streaming components.
    /// This component owns one EasyMic recording session and emits stable mono PCM chunks on Unity's main thread.
    /// </summary>
    [AddComponentMenu("SherpaONNX/Audio/EasyMic Audio Input Source")]
    [DisallowMultipleComponent]
    public sealed class EasyMicSherpaAudioInputSource : SherpaAudioInputSource
    {
        private const float MinChunkDurationSeconds = 0.05f;
        private const float MaxChunkDurationSeconds = 0.5f;
        private const int MinPendingChunks = 1;
        private const int MaxPendingChunks = 128;

        [SerializeField]
        [Tooltip("Preferred EasyMic device name. Leave empty to use EasyMic's default device.")]
        private string preferredDeviceName = string.Empty;

        [SerializeField]
        [Tooltip("Output sample rate emitted to Sherpa components.")]
        private SampleRate outputSampleRate = SampleRate.Hz16000;

        [SerializeField]
        [Tooltip("Capture channel requested from EasyMic before the bridge downmixes to mono.")]
        private Channel channel = Channel.Mono;

        [SerializeField]
        [Tooltip("EasyMic latency profile for the capture session.")]
        private EasyMicLatencyProfile latencyProfile = EasyMicLatencyProfile.Balanced;

        [SerializeField]
        [Tooltip("Length of each mono PCM chunk emitted to Sherpa components.")]
        [Range(MinChunkDurationSeconds, MaxChunkDurationSeconds)]
        private float chunkDurationSeconds = 0.2f;

        [SerializeField]
        [Tooltip("Automatically start EasyMic capture when this component becomes enabled in Play Mode.")]
        private bool autoStartOnEnable;

        [SerializeField]
        [Tooltip("Stop the EasyMic capture session when this component becomes disabled.")]
        private bool stopOnDisable = true;

        [SerializeField]
        [Tooltip("Maximum chunks kept for main-thread delivery. When full, the oldest chunk is dropped.")]
        [Range(MinPendingChunks, MaxPendingChunks)]
        private int maxPendingChunks = 8;

        [SerializeField]
        [Tooltip("Maximum chunks delivered to Sherpa components per Unity Update.")]
        [Range(1, 32)]
        private int maxChunksPerUpdate = 4;

        [SerializeField]
        private ChunkReadyUnityEvent onChunkReady = new ChunkReadyUnityEvent();

        [SerializeField]
        private UnityEvent<bool> onCaptureStateChanged = new UnityEvent<bool>();

        private readonly object _queueLock = new object();
        private readonly object _eventLock = new object();
        private readonly Queue<float[]> _pendingChunks = new Queue<float[]>();

        private Action<float[], int> _chunkReadyHandlers;
        private RecordingHandle _recordingHandle;
        private AudioWorkerBlueprint[] _workerBlueprints;
        private int _droppedChunkCount;
        private int _formatMismatchCount;
        private int _listenerCount;
        private int _listenerRemovalVersion;
        private int _lastStopObservedListenerRemovalVersion;
        private int _captureGeneration;
        private string _lastFormatMismatch = string.Empty;
        private int _isStopping;

        public override event Action<float[], int> ChunkReady
        {
            add
            {
                lock (_eventLock)
                {
                    _chunkReadyHandlers += value;
                    _listenerCount = _chunkReadyHandlers?.GetInvocationList().Length ?? 0;
                }
            }
            remove
            {
                lock (_eventLock)
                {
                    _chunkReadyHandlers -= value;
                    _listenerCount = _chunkReadyHandlers?.GetInvocationList().Length ?? 0;
                    Interlocked.Increment(ref _listenerRemovalVersion);
                }
            }
        }

        public override int OutputSampleRate => (int)outputSampleRate;

        public override bool IsCapturing => _recordingHandle.IsValid && SafeIsHandleAlive(_recordingHandle);

        public int DroppedChunkCount => Volatile.Read(ref _droppedChunkCount);

        public int PendingChunkCount
        {
            get
            {
                lock (_queueLock)
                {
                    return _pendingChunks.Count;
                }
            }
        }

        public int FormatMismatchCount => Volatile.Read(ref _formatMismatchCount);

        public int ListenerCount => Volatile.Read(ref _listenerCount);

        public string LastFormatMismatch
        {
            get
            {
                lock (_queueLock)
                {
                    return _lastFormatMismatch;
                }
            }
        }

        public float ChunkDurationSeconds => chunkDurationSeconds;

        public SampleRate OutputSampleRateOption => outputSampleRate;

        public EasyMicLatencyProfile LatencyProfile => latencyProfile;

        [Serializable]
        public sealed class ChunkReadyUnityEvent : UnityEvent<float[], int>
        {
        }

        private void Awake()
        {
            NormalizeSettings();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            NormalizeSettings();
        }
#endif

        private void OnEnable()
        {
            if (Application.isPlaying && autoStartOnEnable)
            {
                TryStartCapture();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            DrainPendingChunks();
        }

        private void OnDisable()
        {
            if (stopOnDisable)
            {
                ForceStopCapture();
            }
        }

        private void OnDestroy()
        {
            ForceStopCapture();
        }

        public override bool TryStartCapture()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[EasyMicSherpaAudioInputSource] Capture is only supported in Play Mode.");
                return false;
            }

            if (IsCapturing)
            {
                return true;
            }

            NormalizeSettings();
            ResetDiagnosticsAndQueue();

            if (!TryResolveDevice(out var device))
            {
                Debug.LogWarning("[EasyMicSherpaAudioInputSource] No EasyMic capture device is available.");
                return false;
            }

            try
            {
                int generation = Interlocked.Increment(ref _captureGeneration);
                _workerBlueprints = CreateWorkerBlueprints(generation);
                _recordingHandle = EasyMicAPI.StartRecording(
                    device,
                    outputSampleRate,
                    channel,
                    _workerBlueprints,
                    latencyProfile);

                bool started = _recordingHandle.IsValid && SafeIsHandleAlive(_recordingHandle);
                if (started)
                {
                    onCaptureStateChanged?.Invoke(true);
                }

                return started;
            }
            catch (Exception ex)
            {
                _recordingHandle = default;
                _workerBlueprints = null;
                Debug.LogError($"[EasyMicSherpaAudioInputSource] Failed to start EasyMic capture: {ex.Message}");
                return false;
            }
        }

        public override void StopCapture()
        {
            StopCaptureInternal(force: false);
        }

        public void ForceStopCapture()
        {
            StopCaptureInternal(force: true);
        }

        private void StopCaptureInternal(bool force)
        {
            if (Interlocked.Exchange(ref _isStopping, 1) != 0)
            {
                return;
            }

            try
            {
                if (!force && ShouldDeferStopCapture())
                {
                    return;
                }

                var handle = _recordingHandle;
                bool hadRecording = handle.IsValid;
                _recordingHandle = default;
                _workerBlueprints = null;
                Interlocked.Increment(ref _captureGeneration);

                if (hadRecording)
                {
                    EasyMicAPI.StopRecording(handle);
                }

                ClearQueue();
                if (hadRecording)
                {
                    onCaptureStateChanged?.Invoke(false);
                }
            }
            finally
            {
                Volatile.Write(ref _isStopping, 0);
            }
        }

        public void SetPreferredDevice(string deviceName)
        {
            preferredDeviceName = deviceName ?? string.Empty;
        }

        public void ApplyOutputFormat(SampleRate sampleRate, float chunkDuration, bool restartIfCapturing = true)
        {
            bool wasCapturing = IsCapturing;
            if (wasCapturing && !restartIfCapturing)
            {
                Debug.LogWarning("[EasyMicSherpaAudioInputSource] Output format changes while capturing require a restart. The request was ignored because restartIfCapturing is false.", this);
                return;
            }

            if (wasCapturing && restartIfCapturing)
            {
                ForceStopCapture();
            }

            outputSampleRate = sampleRate;
            chunkDurationSeconds = chunkDuration;
            NormalizeSettings();

            if (wasCapturing && restartIfCapturing)
            {
                TryStartCapture();
            }
        }

        internal void EnqueueChunkFromWorker(float[] chunk, int sampleRate)
        {
            EnqueueChunkFromWorker(Volatile.Read(ref _captureGeneration), chunk, sampleRate);
        }

        private void EnqueueChunkFromWorker(int generation, float[] chunk, int sampleRate)
        {
            if (generation != Volatile.Read(ref _captureGeneration) ||
                chunk == null ||
                chunk.Length == 0 ||
                sampleRate != OutputSampleRate)
            {
                return;
            }

            lock (_queueLock)
            {
                int capacity = Math.Min(MaxPendingChunks, Math.Max(MinPendingChunks, maxPendingChunks));
                while (_pendingChunks.Count >= capacity)
                {
                    _pendingChunks.Dequeue();
                    Interlocked.Increment(ref _droppedChunkCount);
                }

                _pendingChunks.Enqueue(chunk);
            }
        }

        internal void ReportFormatMismatchFromWorker(int sampleRate, int channelCount, int expectedSampleRate, int expectedChannelCount)
        {
            ReportFormatMismatchFromWorker(
                Volatile.Read(ref _captureGeneration),
                sampleRate,
                channelCount,
                expectedSampleRate,
                expectedChannelCount);
        }

        private void ReportFormatMismatchFromWorker(int generation, int sampleRate, int channelCount, int expectedSampleRate, int expectedChannelCount)
        {
            if (generation != Volatile.Read(ref _captureGeneration))
            {
                return;
            }

            Interlocked.Increment(ref _formatMismatchCount);
            lock (_queueLock)
            {
                _lastFormatMismatch =
                    $"Expected {expectedSampleRate} Hz / {expectedChannelCount} channel(s), got {sampleRate} Hz / {channelCount} channel(s).";
            }
        }

        private AudioWorkerBlueprint[] CreateWorkerBlueprints(int generation)
        {
            int targetSampleRate = OutputSampleRate;
            int chunkFrames = CalculateChunkFrames(targetSampleRate, chunkDurationSeconds);
            string key = $"{nameof(EasyMicSherpaAudioInputSource)}:{GetInstanceID()}:{generation}:{targetSampleRate}:{chunkFrames}";

            return new[]
            {
                new AudioWorkerBlueprint(() =>
                {
                    var pipeline = new AudioPipeline();
                    pipeline.AddWorker(new Downmixer());
                    pipeline.AddWorker(new Resampler(targetSampleRate));
                    pipeline.AddWorker(new EasyMicSherpaChunkReader(
                        targetSampleRate,
                        expectedChannelCount: 1,
                        chunkFrameCount: chunkFrames,
                        onChunkReady: (chunk, sampleRate) => EnqueueChunkFromWorker(generation, chunk, sampleRate),
                        onFormatMismatch: (sampleRate, channelCount, expectedSampleRate, expectedChannelCount) =>
                            ReportFormatMismatchFromWorker(generation, sampleRate, channelCount, expectedSampleRate, expectedChannelCount)));
                    return pipeline;
                }, key)
            };
        }

        private void DrainPendingChunks()
        {
            int limit = Mathf.Max(1, maxChunksPerUpdate);
            int sampleRate = OutputSampleRate;

            for (int i = 0; i < limit; i++)
            {
                float[] chunk;
                lock (_queueLock)
                {
                    if (_pendingChunks.Count == 0)
                    {
                        return;
                    }

                    chunk = _pendingChunks.Dequeue();
                }

                try
                {
                    Action<float[], int> handlers;
                    lock (_eventLock)
                    {
                        handlers = _chunkReadyHandlers;
                    }

                    handlers?.Invoke(chunk, sampleRate);
                    onChunkReady?.Invoke(chunk, sampleRate);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }
            }
        }

        private bool TryResolveDevice(out MicDevice device)
        {
            device = default;

            try
            {
                var devices = EasyMicAPI.Devices;
                if (devices == null || devices.Length == 0)
                {
                    return false;
                }

                string preferred = preferredDeviceName;
                if (!string.IsNullOrWhiteSpace(preferred))
                {
                    for (int i = 0; i < devices.Length; i++)
                    {
                        if (string.Equals(devices[i].Name, preferred, StringComparison.Ordinal) ||
                            string.Equals(devices[i].Name, preferred, StringComparison.OrdinalIgnoreCase))
                        {
                            device = devices[i];
                            return device.HasValidId;
                        }
                    }
                }

                for (int i = 0; i < devices.Length; i++)
                {
                    if (devices[i].IsDefault)
                    {
                        device = devices[i];
                        return device.HasValidId;
                    }
                }

                device = devices[0];
                return device.HasValidId;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EasyMicSherpaAudioInputSource] Failed to resolve EasyMic device: {ex.Message}");
                return false;
            }
        }

        private void NormalizeSettings()
        {
            if (!Enum.IsDefined(typeof(SampleRate), outputSampleRate))
            {
                outputSampleRate = SampleRate.Hz16000;
            }

            if (!Enum.IsDefined(typeof(Channel), channel))
            {
                channel = Channel.Mono;
            }

            chunkDurationSeconds = Mathf.Clamp(chunkDurationSeconds, MinChunkDurationSeconds, MaxChunkDurationSeconds);
            maxPendingChunks = Mathf.Clamp(maxPendingChunks, MinPendingChunks, MaxPendingChunks);
            maxChunksPerUpdate = Mathf.Clamp(maxChunksPerUpdate, 1, 32);
        }

        private void ResetDiagnosticsAndQueue()
        {
            Interlocked.Exchange(ref _droppedChunkCount, 0);
            Interlocked.Exchange(ref _formatMismatchCount, 0);
            lock (_queueLock)
            {
                _lastFormatMismatch = string.Empty;
                _pendingChunks.Clear();
            }
        }

        private void ClearQueue()
        {
            lock (_queueLock)
            {
                _pendingChunks.Clear();
            }
        }

        private bool ShouldDeferStopCapture()
        {
            int listeners = ListenerCount;
            if (listeners <= 0)
            {
                return false;
            }

            int removalVersion = Volatile.Read(ref _listenerRemovalVersion);
            int previousObservedRemovalVersion = Interlocked.Exchange(ref _lastStopObservedListenerRemovalVersion, removalVersion);
            bool followsListenerRemoval = removalVersion != previousObservedRemovalVersion;

            return followsListenerRemoval || listeners > 1;
        }

        private static int CalculateChunkFrames(int sampleRate, float durationSeconds)
        {
            return Mathf.Max(128, Mathf.RoundToInt(Mathf.Max(1, sampleRate) * Mathf.Clamp(durationSeconds, MinChunkDurationSeconds, MaxChunkDurationSeconds)));
        }

        private static bool SafeIsHandleAlive(RecordingHandle handle)
        {
            try
            {
                return handle.IsValid && EasyMicAPI.IsHandleAlive(handle);
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
