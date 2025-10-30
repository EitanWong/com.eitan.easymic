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



        #region PublicMethod


        public void UpdateProgress(float progress)
        {
            this.loadingProgress.value = progress;
            if (progress >= 1)
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
        }

        #endregion

        #region  PrivateMethod
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

            var currentSpeed = Speed;
            var MaxSpeed = Speed * 3;
            var animDuration = loadingCompleteSound.length;
            //TODO: play loading complete sound
            while (true)
            {

                if (!stripe) { yield return null; }

                RotateHorizontalAxisMobiusStripe(currentSpeed);

                //TODO: Make VerticalAxis Rotate to -90 degress turning the graphic shape info circle anim

                stripe.RebuildNow();
                yield return null;
            }

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
