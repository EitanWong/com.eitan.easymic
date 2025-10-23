using Radishmouse;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public class SamanthaAnimator : AIChatAnimator
    {
        [SerializeField] private UIMobiusStripe stripe;
        [SerializeField] private float Speed = 1;

        private const int HALF_DEGRESS = 180;

        private void Update()
        {
            if (!stripe) { return; }

            // Rotate around X (degrees per second). Use a modest speed by default.
            stripe.perspectiveEuler += Vector3.right * Speed * HALF_DEGRESS * Time.deltaTime;
            if (stripe.perspectiveEuler.x > 180)
            {
                stripe.perspectiveEuler += Vector3.left * HALF_DEGRESS;
            }
            // At runtime we must explicitly request a rebuild after changing public fields.
            stripe.RebuildNow();
        }

    }
}
