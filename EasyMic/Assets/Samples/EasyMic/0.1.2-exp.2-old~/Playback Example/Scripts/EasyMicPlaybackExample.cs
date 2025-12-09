using System;
using System.Collections;
using Eitan.EasyMic.Runtime.Mono;
using UnityEngine;
using UnityEngine.UI;

namespace Eitan.EasyMic.Samples.Playback
{
    public class EasyMicPlaybackExample : MonoBehaviour
    {
        [SerializeField] private Button clipPlayButton;
        [SerializeField] private Button streamPlayButton;
        [SerializeField] private AudioClip clip;
        [SerializeField] private PlaybackAudioSourceBehaviour audioSource;

        private const int StreamChunkFrames = 2048; // frames per chunk when streaming
        private Coroutine _streamRoutine;
        private bool _isStreaming;
        private Text _streamText;

        private enum PlaybackMode { None, Clip, Stream }
        private PlaybackMode _mode = PlaybackMode.None;

        private void UpdateButtonStates()
        {
            if (clipPlayButton)
            {
                // Disable Clip button while streaming
                clipPlayButton.interactable = _mode != PlaybackMode.Stream;
            }
            if (streamPlayButton)
            {
                // Disable Stream button while clip playback is active
                streamPlayButton.interactable = _mode != PlaybackMode.Clip;
            }
        }

        private void Start()
        {
            if (clipPlayButton)
            {
                clipPlayButton.onClick.AddListener(HandleClipPlayButtonClick);
                var textComponent = clipPlayButton.GetComponentInChildren<Text>();
                if (textComponent)
                {
                    textComponent.text = "Clip Play";
                }
            }
            if (streamPlayButton)
            {
                streamPlayButton.onClick.AddListener(HandleStreamPlayButtonClick);
                _streamText = streamPlayButton.GetComponentInChildren<Text>();
                if (_streamText)
                {
                    _streamText.text = "Stream Play";
                }
            }

            if (audioSource)
            {
                audioSource.PlayOnAwake = false;
                audioSource.Loop = false;
                // Subscribe to end-of-stream so we can reset UI
                audioSource.OnPlaybackCompleted += HandlePlaybackCompleted;
            }
            _mode = PlaybackMode.None;
            UpdateButtonStates();
        }

        private void OnDestroy()
        {
            if (clipPlayButton)
            {
                clipPlayButton.onClick.RemoveListener(HandleClipPlayButtonClick);
            }
            if (streamPlayButton)
            {
                streamPlayButton.onClick.RemoveListener(HandleStreamPlayButtonClick);
            }
            if (audioSource)
            {
                audioSource.OnPlaybackCompleted -= HandlePlaybackCompleted;
            }
        }

        private void HandleStreamPlayButtonClick()
        {
            if (_mode == PlaybackMode.Clip)
            {
                Debug.Log("EasyMicPlaybackExample: Stream is disabled while clip playback is active. Stop the clip first.");
                return;
            }
            // Toggle behavior: if currently streaming, stop; otherwise start streaming
            if (_isStreaming)
            {
                // Stop streaming
                if (_streamRoutine != null)
                {
                    StopCoroutine(_streamRoutine);
                    _streamRoutine = null;
                }
                if (audioSource)
                {
                    audioSource.Stop();
                }
                _isStreaming = false;
                _mode = PlaybackMode.None;
                UpdateButtonStates();
                if (_streamText)
                {
                    _streamText.text = "Stream Play";
                }

                return;
            }

            // Start streaming
            if (!clip)
            {
                Debug.LogWarning("EasyMicPlaybackExample: No clip assigned on example script.");
                return;
            }

            // Stop any existing streaming coroutine (safety)
            if (_streamRoutine != null)
            {
                StopCoroutine(_streamRoutine);
                _streamRoutine = null;
            }

            // Detach the clip so the feeder thread won't push clip-backed audio while we stream manually
            audioSource.Clip = null;
            audioSource.Loop = false;
            audioSource.Stop();

            // Start streaming the clip data manually
            _streamRoutine = StartCoroutine(StreamClipRoutine(clip));
            _isStreaming = true;
            if (_streamText)
            {
                _streamText.text = "Stream Stop";
            }
            _mode = PlaybackMode.Stream;
            UpdateButtonStates();

        }

        private IEnumerator StreamClipRoutine(AudioClip clip)
        {
            if (clip == null)
            {
                yield break;
            }


            int channels = Mathf.Max(1, clip.channels);
            int sampleRate = Mathf.Max(8000, clip.frequency);
            int totalFrames = clip.samples;
            if (totalFrames <= 0)
            {

                yield break;
            }


            int totalSamples = totalFrames * channels;

            // Read the entire clip into a buffer once for simplicity
            float[] sampleBuffer = new float[totalSamples];
            if (!clip.GetData(sampleBuffer, 0))
            {
                Debug.LogWarning("EasyMicPlaybackExample: Failed to read clip data.");
                yield break;
            }

            int chunkFrames = Mathf.Max(256, StreamChunkFrames);
            int maxChunkSamples = chunkFrames * channels;
            float[] reusableChunk = new float[maxChunkSamples];

            int offset = 0;
            while (offset < totalSamples)
            {
                int remaining = totalSamples - offset;
                int toCopy = Mathf.Min(maxChunkSamples, remaining);

                float[] bufferToSend = reusableChunk;
                if (toCopy != reusableChunk.Length)
                {
                    // Last chunk may be smaller; ensure Enqueue gets an array sized exactly to 'toCopy'
                    bufferToSend = new float[toCopy];
                }

                Array.Copy(sampleBuffer, offset, bufferToSend, 0, toCopy);

                bool isLast = (offset + toCopy) >= totalSamples;

                // Throttle to keep the queued audio around ~0.2s to avoid overfilling
                while (audioSource && audioSource.BufferedSeconds > 0.15f)
                {
                    yield return null;
                }

                // Stream enqueue; mark the final chunk as end-of-stream
                audioSource.Enqueue(bufferToSend, toCopy, channels, sampleRate, isLast);

                offset += toCopy;

                if (!isLast)
                {
                    // Give the audio thread a frame to consume
                    yield return null;
                }
            }

            _streamRoutine = null;
        }
        private void HandleClipPlayButtonClick()
        {
            if (_mode == PlaybackMode.Stream)
            {
                Debug.Log("EasyMicPlaybackExample: Clip playback is disabled while streaming is active. Stop streaming first.");
                return;
            }
            // Start streaming
            if (!clip)
            {
                Debug.LogWarning("EasyMicPlaybackExample: No clip assigned on example script.");
                return;
            }
            audioSource.Clip = clip;
            var textComponent = clipPlayButton.GetComponentInChildren<Text>();

            if (audioSource.IsPlaying)
            {
                audioSource.Stop();
                _mode = PlaybackMode.None;
                UpdateButtonStates();
                if (textComponent)
                {
                    textComponent.text = "Clip Play";
                }
            }
            else
            {
                audioSource.Play();
                _mode = PlaybackMode.Clip;
                UpdateButtonStates();
                if (textComponent)
                {
                    textComponent.text = "Clip Stop";
                }
            }
        }
        private void HandlePlaybackCompleted(PlaybackAudioSourceBehaviour src)
        {
            // Reset state and UI when playback drains due to end-of-stream
            _isStreaming = false;
            if (_streamText)
            {
                _streamText.text = "Stream Play";
            }

            // Reset Clip button label as well
            if (clipPlayButton)
            {
                var clipText = clipPlayButton.GetComponentInChildren<Text>();
                if (clipText)
                {
                    clipText.text = "Clip Play";
                }
            }

            _mode = PlaybackMode.None;
            UpdateButtonStates();
        }
    }
}

