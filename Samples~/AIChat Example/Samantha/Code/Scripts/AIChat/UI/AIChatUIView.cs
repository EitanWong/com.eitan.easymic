using System;
using System.Collections;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono;
using Eitan.EasyMic.Runtime.Mono.Components;
using Radishmouse;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [AddComponentMenu("Examples/EasyMic/AI Chat/UI/View")]
    public class AIChatUIView : MonoBehaviour
    {
        // [SerializeField] private UIMobiusStripe stripe;

        [Header("UI")]
        [SerializeField] private Slider loadingProgress;
        [SerializeField] private UIMobiusStripe stripe;
        [SerializeField] private PlaybackAudioSourceBehaviour speakerAudioSource;
        [SerializeField] private AIChatController chatController;
        [SerializeField] private EasyMicrophone userMicrophone;
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
        private const float CompleteThreshold = 0.999f;
        private const float ResetThreshold = 0.8f;
        private const float ActiveAudioThreshold = 0.002f;
        private const float UserAudioActivationThreshold = 0.12f;
        private const float ScaleAttackPerSecond = 18f;
        private const float ScaleReleasePerSecond = 9f;

        private Coroutine _animCor;
        private PlaybackHandle _loadingCompleteHandle;
        private bool _hasError;
        private LoadingState _loadingState;
        private bool _hasSeenLoadingInProgress;
        private readonly SpeechVisualizationLevel _assistantAudioLevel = new SpeechVisualizationLevel();
        private readonly SpeechVisualizationLevel _userAudioLevel = new SpeechVisualizationLevel();
        private AudioWorkerBlueprint _userAudioProbeBlueprint;
        private bool _userAudioProbeAttached;
        private bool _isUserSpeaking;
        private float _currentScale = 1f;

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
            ResolveUserMicrophone();
            SubscribeEvents();
            EnsureUserAudioProbeAttached();
            _currentScale = baseScale;
            InitializeStatus();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            DetachUserAudioProbe();
        }

        private void Update()
        {
            if (!stripe)
            {
                return;
            }


            EnsureUserAudioProbeAttached();

            float assistantLevel = _assistantAudioLevel.GetRecentLevel(noAudioResetDelay);
            float userLevel = _userAudioLevel.GetRecentLevel(noAudioResetDelay);
            bool assistantTransportActive =
                (chatController && chatController.IsAssistantSpeaking) ||
                (speakerAudioSource && speakerAudioSource.IsPlaying);
            bool assistantActive = assistantTransportActive && assistantLevel > ActiveAudioThreshold;
            bool userTransportActive =
                (userMicrophone && userMicrophone.IsRecording) &&
                (_isUserSpeaking || (chatController && chatController.IsUserSpeaking) || userLevel > UserAudioActivationThreshold);
            bool userActive = !assistantActive && userTransportActive && userLevel > ActiveAudioThreshold;

            float dominantLevel = assistantActive ? assistantLevel : (userActive ? userLevel : 0f);
            float expressiveness = Mathf.Clamp01(speechExpressiveness);
            float compressedLevel = Mathf.Pow(dominantLevel, Mathf.Lerp(0.75f, 0.4f, expressiveness));
            float scaleRange = Mathf.Max(0f, maxScale - baseScale);
            float targetScale = baseScale + scaleRange * Mathf.Clamp01(compressedLevel * Mathf.Max(0f, speechIntensity));
            float smoothing = targetScale > _currentScale ? ScaleAttackPerSecond : ScaleReleasePerSecond;
            float alpha = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            _currentScale = Mathf.Lerp(_currentScale, targetScale, alpha);
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
            bool reachedCompleteThreshold = progress >= CompleteThreshold;
            bool isControllerInitialized = !chatController || chatController.IsInitialized;

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

            if (!reachedCompleteThreshold || !isControllerInitialized)
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

            bool shouldPlayCompleteAnim = _loadingState != LoadingState.Completed
                && (_hasSeenLoadingInProgress || _loadingState == LoadingState.None);
            if (shouldPlayCompleteAnim)
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
                chatController.OnAssistantAudioQueued += AssistantAudioQueuedHandler;
                chatController.OnUserSpeakingStateChanged += UserSpeakingStateChangedHandler;
            }

            if (userMicrophone)
            {
                userMicrophone.OnRecordingStateChanged += UserRecordingStateChangedHandler;
                userMicrophone.OnMicrophoneInitialized += UserMicrophoneInitializedHandler;
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
                chatController.OnAssistantAudioQueued -= AssistantAudioQueuedHandler;
                chatController.OnUserSpeakingStateChanged -= UserSpeakingStateChangedHandler;
            }

            if (userMicrophone)
            {
                userMicrophone.OnRecordingStateChanged -= UserRecordingStateChangedHandler;
                userMicrophone.OnMicrophoneInitialized -= UserMicrophoneInitializedHandler;
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
            _assistantAudioLevel.Push(sample, sample != null ? sample.Length : 0, channels, sampleRate);
        }

        private void AssistantAudioQueuedHandler(float[] sample, int count, int channels, int sampleRate)
        {
            _assistantAudioLevel.Push(sample, count, channels, sampleRate);
        }

        private void UserSpeakingStateChangedHandler(bool isSpeaking)
        {
            _isUserSpeaking = isSpeaking;
        }

        private void UserRecordingStateChangedHandler(bool isRecording)
        {
            if (isRecording)
            {
                EnsureUserAudioProbeAttached();
                return;
            }

            _userAudioProbeAttached = false;
            _isUserSpeaking = false;
            _userAudioLevel.Reset();
        }

        private void UserMicrophoneInitializedHandler(bool initialized)
        {
            _userAudioProbeAttached = false;
            if (initialized)
            {
                EnsureUserAudioProbeAttached();
            }
        }

        private void ResolveUserMicrophone()
        {
            if (!userMicrophone && chatController)
            {
                userMicrophone = chatController.MicrophoneSource;
            }
        }

        private void EnsureUserAudioProbeAttached()
        {
            ResolveUserMicrophone();
            if (_userAudioProbeAttached || !userMicrophone || !userMicrophone.IsRecording)
            {
                return;
            }

            _userAudioProbeBlueprint ??= new AudioWorkerBlueprint(
                () => new MicrophoneSpeechVisualizationProbe(_userAudioLevel),
                "samantha-speech-visualization-level");
            userMicrophone.AppendProcessor(_userAudioProbeBlueprint);
            _userAudioProbeAttached = true;
        }

        private void DetachUserAudioProbe()
        {
            if (!_userAudioProbeAttached || !userMicrophone)
            {
                return;
            }

            if (userMicrophone.IsRecording && _userAudioProbeBlueprint != null)
            {
                userMicrophone.RemoveProcessor(_userAudioProbeBlueprint);
            }

            _userAudioProbeAttached = false;
        }

        #endregion


        #region AnimIEnumeerator
        private IEnumerator LoadingCompleteAnim()
        {
            try
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

                if (_loadingCompleteHandle.IsValid)
                {
                    _loadingCompleteHandle.Stop();
                    _loadingCompleteHandle.Dispose();
                }

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
            finally
            {
                _animCor = null;
            }
        }


        private IEnumerator LoadingProgressAnim()
        {
            try
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
            finally
            {
                _animCor = null;
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
