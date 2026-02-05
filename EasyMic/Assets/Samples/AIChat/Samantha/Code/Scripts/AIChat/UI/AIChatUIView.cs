using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono.Components;
using Radishmouse;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public class AIChatUIView : MonoBehaviour
    {
        // [SerializeField] private UIMobiusStripe stripe;

        [Header("UI")]
        [SerializeField] private Slider loadingProgress;
        [SerializeField] private UIMobiusStripe stripe;
        [SerializeField] private PlaybackAudioSourceBehaviour speakerAudioSource;
        [SerializeField] private AIChatController chatController;
        [SerializeField] private TMP_Text errorMessageText;
        [SerializeField] private float Speed = 1;

        [Header("Speaker Visualization")]
        [SerializeField] private float baseScale = 1f;
        [SerializeField] private float maxScale = 1.6f;
        [SerializeField] private float noAudioResetDelay = 0.15f;

        [Header("Speech Visualization")]
        [SerializeField] private float speechIntensity = 1f;
        [SerializeField] private float speechExpressiveness = 0.55f;

        [Header("Sound")]

        [SerializeField] private AudioClip loadingCompleteSound;

        private const int HALF_DEGRESS = 180;
        private const float ScaleSmooth = 10f;
        private const float Epsilon = 0.00001f;
        private const float CompleteThreshold = 0.999f;
        private const float ResetThreshold = 0.8f;

        private Coroutine _animCor;
        private PlaybackHandle _loadingCompleteHandle;
        private bool _hasError;
        private LoadingState _loadingState;
        private bool _hasSeenLoadingInProgress;
        private volatile float _audioLevel;
        private volatile float _vowelLevel;
        private volatile float _consonantLevel;
        private volatile float _speechPulse;
        private volatile float _vowelScale;
        private volatile float _consonantScale;
        private volatile float _pulseScale;
        private volatile float _noiseFloor;
        private volatile float _signalPeak;
        private float _prevNorm;
        private float _lastSample;
        private float _currentScale = 1f;
        private long _lastAudioReadTicks;
        private int _hasAudioRead;

        #region MonoBehaviour
        private void Awake()
        {
            if (!chatController)
            {
                chatController = FindObjectOfType<AIChatController>();
            }
        }

        private void Start()
        {
            ResetStripeGraphic();
            SubscribeEvents();
            _currentScale = baseScale;
            InitializeStatus();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
        }

        private void Update()
        {
            if (!stripe)
            {
                return;
            }


            bool isPlaying = speakerAudioSource && speakerAudioSource.IsPlaying;
            bool hasRecentAudio = IsAudioRecent();

            if (!isPlaying || !hasRecentAudio)
            {
                _audioLevel = 0f;
                _vowelLevel = 0f;
                _consonantLevel = 0f;
                _speechPulse = 0f;
                _noiseFloor = 0f;
                _signalPeak = 0f;
                _prevNorm = 0f;
                _lastSample = 0f;
            }

            float vowel = Mathf.Sqrt(Mathf.Max(0f, _vowelLevel));
            float consonant = Mathf.Sqrt(Mathf.Max(0f, _consonantLevel));
            float targetScale = (isPlaying && hasRecentAudio)
                ? Mathf.Clamp(baseScale + (_vowelScale * vowel) + (_consonantScale * consonant) + (_pulseScale * _speechPulse), baseScale, maxScale)
                : baseScale;
            _currentScale = Mathf.Lerp(_currentScale, targetScale, ScaleSmooth * Time.deltaTime);
            SetStripeScale(_currentScale);
        }
        #endregion


        #region PublicMethod

        public void UpdateProgress(float progress)
        {
            if (_hasError || !loadingProgress)
            {
                return;
            }


            progress = Mathf.Clamp01(progress);

            if (_loadingState == LoadingState.Completed)
            {
                if (progress <= ResetThreshold)
                {
                    _loadingState = LoadingState.InProgress;
                    _hasSeenLoadingInProgress = true;
                }
                else
                {
                    loadingProgress.value = progress;
                    return;
                }
            }

            if (progress < CompleteThreshold)
            {
                _loadingState = LoadingState.InProgress;
                _hasSeenLoadingInProgress = true;
                if (_animCor == null)
                {
                    _animCor = StartCoroutine(LoadingProgressAnim());
                }
                loadingProgress.value = progress;
                return;
            }

            if (_loadingState != LoadingState.Completed && _hasSeenLoadingInProgress)
            {
                RestartAnim(LoadingCompleteAnim());
                _loadingState = LoadingState.Completed;
            }

            loadingProgress.value = progress;
        }

        public void SetErrorMessage(string message)
        {
            bool hasError = !string.IsNullOrWhiteSpace(message);
            _hasError = hasError;
            if (hasError)
            {
                _loadingState = LoadingState.None;
                _hasSeenLoadingInProgress = false;
            }

            if (!errorMessageText)
            {
                return;
            }

            if (!hasError)
            {
                SetErrorUI(string.Empty, false);
                return;
            }

            SetErrorUI(message, true);
            StopLoadingEffects();
        }

        #endregion

        #region  PrivateMethod

        private void SubscribeEvents()
        {
            if (speakerAudioSource)
            {
                speakerAudioSource.OnAudioPlaybackRead += SpeakerAudioPlaybackHandler;
            }

            if (chatController)
            {
                chatController.OnChatStateChanged += OnChatStateChangedHandler;
                chatController.OnLoadingCallback += OnLoadingProgressHandler;
            }
        }

        private void UnsubscribeEvents()
        {
            if (speakerAudioSource)
            {
                speakerAudioSource.OnAudioPlaybackRead -= SpeakerAudioPlaybackHandler;
            }

            if (chatController)
            {
                chatController.OnChatStateChanged -= OnChatStateChangedHandler;
                chatController.OnLoadingCallback -= OnLoadingProgressHandler;
            }
        }

        private void ResetStripeGraphic()
        {
            if (!stripe)
            {
                return;
            }


            stripe.loops = 3;
            stripe.orientation = UIMobiusStripe.Orientation.Horizontal;
            stripe.phase = 0;
            stripe.pathOffsetRadians = 0;
            stripe.enablePerspective = true;
            stripe.perspectiveEuler = Vector3.zero;
            stripe.perspectiveDistanceFactor = 8;
            stripe.sizeScale = Vector2.one;
            stripe.sizePadding = Vector2.zero;
            stripe.shrinkToAvoidClipping = true;
            stripe.LineRenderer.thickness = 24;
            stripe.LineRenderer.thinThicknessMultiplier = .5f;
            stripe.LineRenderer.transparencyShift = .5f;
            stripe.LineRenderer.styleRollOffset = 0;

        }

        private void RotateHorizontalAxisMobiusStripe(float speed)
        {
            // Rotate around X (degrees per second). Use a modest speed by default.
            stripe.perspectiveEuler += Vector3.right * speed * HALF_DEGRESS * Time.deltaTime;
            if (stripe.perspectiveEuler.x > 180)
            {
                stripe.perspectiveEuler += Vector3.left * HALF_DEGRESS;
            }
        }

        private void StopLoadingEffects()
        {
            if (_animCor != null)
            {
                StopCoroutine(_animCor);
                _animCor = null;
            }

            if (loadingProgress)
            {
                loadingProgress.gameObject.SetActive(false);
            }

            if (stripe)
            {
                _currentScale = baseScale;
                SetStripeScale(_currentScale);
                stripe.RebuildNow();
            }

            if (_loadingCompleteHandle.IsValid)
            {
                _loadingCompleteHandle.Stop();
                _loadingCompleteHandle.Dispose();
            }
        }
        #endregion

        #region  Private Methods
        private void OnChatStateChangedHandler(AIChatController.ChatState state, string message)
        {
            if (state == AIChatController.ChatState.Failed)
            {
                SetErrorMessage(string.IsNullOrWhiteSpace(message) ? "Unknown error." : message);
                return;
            }

            if (errorMessageText && errorMessageText.gameObject.activeSelf)
            {
                SetErrorMessage(string.Empty);
            }
        }

        private void OnLoadingProgressHandler(float progress)
        {
            UpdateProgress(progress);
        }

        private void SpeakerAudioPlaybackHandler(float[] sample, int channels, int sampleRate)
        {
            if (sample == null || sample.Length == 0 || channels <= 0 || sampleRate <= 0)
            {
                return;
            }

            Interlocked.Exchange(ref _lastAudioReadTicks, Stopwatch.GetTimestamp());
            Interlocked.Exchange(ref _hasAudioRead, 1);

            double sumSquares = 0.0;
            int zeroCross = 0;
            int count = sample.Length;
            float prevSample = _lastSample;
            for (int i = 0; i < count; i++)
            {
                float v = sample[i];
                sumSquares += v * v;

                if (i > 0 && (v > 0f) != (prevSample > 0f))
                {
                    zeroCross++;
                }
                prevSample = v;
            }

            float rms = Mathf.Sqrt((float)(sumSquares / count));

            int frames = Mathf.Max(1, count / channels);
            float dt = (float)frames / sampleRate;

            if (_noiseFloor <= 0f)
            {
                _noiseFloor = rms;
                _signalPeak = rms + Epsilon;
            }

            float noiseRiseSec = dt * 30f;
            float noiseFallSec = dt * 6f;
            float noiseAlpha = 1f - Mathf.Exp(-dt / (rms > _noiseFloor ? noiseRiseSec : noiseFallSec));
            _noiseFloor += (rms - _noiseFloor) * noiseAlpha;

            float peakDecaySec = dt * 12f;
            float peakDecay = Mathf.Exp(-dt / peakDecaySec);
            _signalPeak = Mathf.Max(rms, _signalPeak * peakDecay);

            float denom = Mathf.Max(Epsilon, _signalPeak - _noiseFloor);
            float norm = Mathf.Clamp01((rms - _noiseFloor) / denom);

            float attackSec = dt * 2f;
            float releaseSec = dt * 8f;
            float envAlpha = 1f - Mathf.Exp(-dt / (norm > _audioLevel ? attackSec : releaseSec));
            _audioLevel += (norm - _audioLevel) * envAlpha;

            float zcr = count > 1 ? (float)zeroCross / (count - 1) : 0f;
            float expressiveness = Mathf.Clamp01(speechExpressiveness);
            float intensity = Mathf.Max(0f, speechIntensity);
            float zcrMax = Mathf.Lerp(0.1f, 0.22f, expressiveness);
            float zcrNorm = Mathf.Clamp01(zcr / zcrMax);

            float vowelScale = intensity * Mathf.Lerp(0.55f, 0.35f, expressiveness);
            float consonantScale = intensity * Mathf.Lerp(0.2f, 0.4f, expressiveness);
            float pulseScale = intensity * Mathf.Lerp(0.12f, 0.22f, expressiveness);
            _vowelScale = vowelScale;
            _consonantScale = consonantScale;
            _pulseScale = pulseScale;

            float consonantBias = Mathf.Clamp01(zcrNorm * 1.2f);
            float vowelTarget = norm * (1f - consonantBias);
            float consonantTarget = norm * consonantBias;
            float pulseTarget = Mathf.Clamp01(Mathf.Max(0f, norm - _prevNorm) * Mathf.Lerp(2f, 4f, expressiveness));

            float vowelAttack = Mathf.Lerp(0.1f, 0.03f, expressiveness);
            float vowelRelease = Mathf.Lerp(0.24f, 0.08f, expressiveness);
            float vowelAlpha = 1f - Mathf.Exp(-dt / (vowelTarget > _vowelLevel ? vowelAttack : vowelRelease));
            _vowelLevel += (vowelTarget - _vowelLevel) * vowelAlpha;

            float consonantAttack = Mathf.Lerp(0.08f, 0.02f, expressiveness);
            float consonantRelease = Mathf.Lerp(0.16f, 0.05f, expressiveness);
            float consonantAlpha = 1f - Mathf.Exp(-dt / (consonantTarget > _consonantLevel ? consonantAttack : consonantRelease));
            _consonantLevel += (consonantTarget - _consonantLevel) * consonantAlpha;

            float pulseDecay = Mathf.Exp(-dt / Mathf.Lerp(0.14f, 0.05f, expressiveness));
            _speechPulse = Mathf.Max(pulseTarget, _speechPulse * pulseDecay);
            _speechPulse = Mathf.Min(1f, _speechPulse);

            _prevNorm = norm;
            _lastSample = prevSample;
        }

        private bool IsAudioRecent()
        {
            if (Interlocked.CompareExchange(ref _hasAudioRead, 0, 0) == 0)
            {
                return false;
            }

            long nowTicks = Stopwatch.GetTimestamp();
            long lastTicks = Interlocked.Read(ref _lastAudioReadTicks);
            double elapsedSec = (nowTicks - lastTicks) / (double)Stopwatch.Frequency;
            return elapsedSec <= Math.Max(0.0, noAudioResetDelay);
        }

        #endregion


        #region AnimIEnumeerator
        private IEnumerator LoadingCompleteAnim()
        {
            if (!loadingProgress)
            {
                yield break;
            }

            loadingProgress.gameObject.SetActive(false);

            // Guard & locals
            var s = stripe;
            if (!s)
            {
                yield break;
            }


            float baseSpeed = Mathf.Max(0.0001f, Speed);
            float maxSpeed = baseSpeed * 9f;

            // Use clip length if available; fallback to known length.
            float totalLen = (loadingCompleteSound && loadingCompleteSound.length > 0f)
                ? loadingCompleteSound.length
                : 14.735f;

            // Time anchors scaled from the 14.735s reference
            const float REF = 14.735f;
            float rotStartSec = totalLen * (10f / REF); // ~10s
            float rotEndSec = totalLen * (13f / REF); // ~13s
            float midHoldSec = totalLen * (9f / REF); // ~9s

            if (midHoldSec >= rotStartSec)
            {
                midHoldSec = Mathf.Max(0f, rotStartSec - 0.1f);
            }


            float quickRampSec = Mathf.Max(0.05f, rotStartSec - midHoldSec);

            if (loadingCompleteSound)
            {
                _loadingCompleteHandle = AudioPlayback.PlayClip(loadingCompleteSound);
            }

            // Precompute denominators (avoid per-frame Mathf.Max)


            float denomRot = Mathf.Max(0.0001f, rotEndSec - rotStartSec);
            float denomHold = Mathf.Max(0.0001f, midHoldSec);
            float denomQuick = Mathf.Max(0.05f, quickRampSec);

            // Rotation targets
            float startY = s.perspectiveEuler.y;
            const float targetY = -90f;

            // Cached references to reduce property lookups
            var line = s.LineRenderer;

            float elapsed = 0f;
            float midSpeed = Mathf.Min(maxSpeed * 0.999f, baseSpeed * 6f); // cap before ~9s

            while (elapsed < rotEndSec)
            {
                if (_hasError)
                {
                    yield break;
                }

                // Linear rotation progress in [rotStartSec, rotEndSec]
                float rotEase = elapsed <= rotStartSec ? 0f : Mathf.Clamp01((elapsed - rotStartSec) / denomRot);

                // Three-phase speed profile
                float currentSpeed =
                    (elapsed <= midHoldSec)
                        ? Mathf.Lerp(baseSpeed, midSpeed, Mathf.Clamp01(elapsed / denomHold))
                        : (elapsed <= midHoldSec + quickRampSec)
                            ? Mathf.Lerp(midSpeed, maxSpeed, Mathf.Clamp01((elapsed - midHoldSec) / denomQuick))
                            : maxSpeed;

                // Horizontal rotation (frame-rate independent)
                RotateHorizontalAxisMobiusStripe(currentSpeed);

                // Vertical rotation & visual polish
                var e = s.perspectiveEuler;
                e.y = Mathf.LerpAngle(startY, targetY, rotEase);
                s.perspectiveEuler = e; // Mathf.LerpAngle handles wrap-around correctly.

                s.sizeScale = Vector2.one * Mathf.Lerp(1f, 1.5f, rotEase);
                line.thinThicknessMultiplier = Mathf.Lerp(.5f, 1f, rotEase);
                line.transparencyShift = Mathf.Lerp(.5f, 1f, rotEase);

                s.RebuildNow();

                elapsed += Time.deltaTime;
                yield return null; // resume next frame
            }

            // Final state
            s.perspectiveEuler = Vector3.zero;
            s.loops = 1;
            s.enablePerspective = false;
            s.sizeScale = Vector2.one;
            line.thinThicknessMultiplier = 1f;
            line.transparencyShift = 1f;
            s.RebuildNow();
        }


        private IEnumerator LoadingProgressAnim()
        {

            if (!loadingProgress)
            {
                yield break;
            }

            loadingProgress.gameObject.SetActive(true);
            while (true)
            {

                if (_hasError)
                {
                    yield break;
                }

                if (stripe)
                {
                    RotateHorizontalAxisMobiusStripe(Speed);
                    // At runtime we must explicitly request a rebuild after changing public fields.
                    stripe.RebuildNow();
                }
                yield return null;
            }
        }
        #endregion

        #region Helpers
        private void InitializeStatus()
        {
            if (chatController && !string.IsNullOrWhiteSpace(chatController.LastErrorMessage))
            {
                SetErrorMessage(chatController.LastErrorMessage);
                return;
            }

            SetErrorMessage(string.Empty);
            if (!chatController)
            {
                return;
            }


            float progress = chatController.LastLoadingProgress;
            UpdateProgress(progress);
        }

        private void RestartAnim(IEnumerator routine)
        {
            if (_animCor != null)
            {
                StopCoroutine(_animCor);
            }
            _animCor = StartCoroutine(routine);
        }

        private void SetErrorUI(string message, bool visible)
        {
            errorMessageText.text = message;
            errorMessageText.gameObject.SetActive(visible);
        }

        private void SetStripeScale(float scale)
        {
            stripe.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private enum LoadingState
        {
            None,
            InProgress,
            Completed
        }
        #endregion
    }
}
