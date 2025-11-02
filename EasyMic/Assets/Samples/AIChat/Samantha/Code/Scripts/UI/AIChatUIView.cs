using System.Collections;
using Eitan.EasyMic.Runtime;
using Radishmouse;
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
        [SerializeField] private float Speed = 1;


        [Header("Sound")]

        [SerializeField] private AudioClip loadingCompleteSound;

        private const int HALF_DEGRESS = 180;

        private Coroutine _animCor;

        #region MonoBehaviour
        private void Start()
        {
            ResetStripeGraphic();
        }
        #endregion


        #region PublicMethod

        public void UpdateProgress(float progress)
        {
            if (this.loadingProgress.value != progress && progress >= 1)
            {
                if (_animCor != null)
                {
                    StopCoroutine(_animCor);
                }
                _animCor = StartCoroutine(LoadingCompleteAnim());
            }
            else if (_animCor == null)
            {
                _animCor = StartCoroutine(LoadingProgressAnim());
            }

            this.loadingProgress.value = progress;
        }

        #endregion

        #region  PrivateMethod


        private void ResetStripeGraphic()
        {
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
        #endregion


        #region AnimIEnumeerator
        private IEnumerator LoadingCompleteAnim()
        {
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
                AudioPlayback.PlayClip(loadingCompleteSound);
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

            loadingProgress.gameObject.SetActive(true);
            while (true)
            {

                if (!stripe) { yield return null; }
                RotateHorizontalAxisMobiusStripe(Speed);
                // At runtime we must explicitly request a rebuild after changing public fields.
                stripe.RebuildNow();
                yield return null;
            }
        }
        #endregion

    }
}
