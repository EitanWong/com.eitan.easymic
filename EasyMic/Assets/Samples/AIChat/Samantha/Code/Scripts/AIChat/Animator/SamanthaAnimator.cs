using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public class SamanthaAnimator : AIChatAnimator
    {

        [SerializeField] private AIChatUIView uIView;
        [SerializeField] private AIChatController chatController;


        #region MonoBehaviour


        private void Awake()
        {
            RegisterEvents();
        }
        private void OnDestroy()
        {
            UnregisterEvents();
        }


        #endregion

        #region  EventHandler
        private void OnChatControllerLoadingHandler(float progress)
        {
            uIView.UpdateProgress(progress);
        }
        #endregion

        #region PrivateMethods
        private void RegisterEvents()
        {
            if (chatController)
            {
                chatController.OnLoadingCallback += OnChatControllerLoadingHandler;
            }
        }

        private void UnregisterEvents()
        {
            if (chatController)
            {
                chatController.OnLoadingCallback -= OnChatControllerLoadingHandler;
            }
        }
        #endregion
        #region  PublicMethods

        public void PlayingLoadingProgressAnim()
        {

        }

        public void PlayLoadingCompleteAnim()
        {

        }
        #endregion

    }
}
