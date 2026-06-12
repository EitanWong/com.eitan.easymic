namespace Eitan.EasyMic.Runtime.Mono
{
    using System;
    using Eitan.EasyMic.Runtime;

    public interface IEasyMicApmWorkerBridge : IAudioWorker
    {
        void SetProcessingOptions(bool enableAEC, bool enableANS, bool enableAGC);
        void GetProcessingOptions(out bool enableAEC, out bool enableANS, out bool enableAGC);
        bool TryGetDiagnostics(out object diagnostics);
    }

    public static class EasyMicApmBridgeRegistry
    {
        private static readonly object Sync = new object();
        private static Func<IEasyMicApmWorkerBridge> s_factory;
        private static Func<bool> s_isAuthorized;
        private static Func<bool> s_hasConfiguredLicenseToken;
        private static Func<string> s_lastError;
        private static Func<string> s_lastTokenSource;
        private static Func<AuthorizationResult> s_authorize;

        public static bool IsAvailable
        {
            get
            {
                lock (Sync)
                {
                    return s_factory != null;
                }
            }
        }

        public static void Register(
            Func<IEasyMicApmWorkerBridge> factory,
            Func<bool> isAuthorized,
            Func<bool> hasConfiguredLicenseToken,
            Func<AuthorizationResult> authorize,
            Func<string> lastError,
            Func<string> lastTokenSource)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            lock (Sync)
            {
                s_factory = factory;
                s_isAuthorized = isAuthorized;
                s_hasConfiguredLicenseToken = hasConfiguredLicenseToken;
                s_authorize = authorize;
                s_lastError = lastError;
                s_lastTokenSource = lastTokenSource;
            }
        }

        public static IEasyMicApmWorkerBridge CreateWorker()
        {
            lock (Sync)
            {
                return s_factory != null ? s_factory() : null;
            }
        }

        public static bool IsAuthorized()
        {
            lock (Sync)
            {
                return s_isAuthorized != null && s_isAuthorized();
            }
        }

        public static bool HasConfiguredLicenseToken()
        {
            lock (Sync)
            {
                return s_hasConfiguredLicenseToken != null && s_hasConfiguredLicenseToken();
            }
        }

        public static AuthorizationResult Authorize()
        {
            lock (Sync)
            {
                return s_authorize != null
                    ? s_authorize()
                    : new AuthorizationResult(false, "EasyMic APM package is not installed or has not registered its runtime bridge.");
            }
        }

        public static string LastError()
        {
            lock (Sync)
            {
                return s_lastError != null ? s_lastError() : string.Empty;
            }
        }

        public static string LastTokenSource()
        {
            lock (Sync)
            {
                return s_lastTokenSource != null ? s_lastTokenSource() : string.Empty;
            }
        }

        public static void SubscribeMixedFrameRaw(AudioSystem.MixedFrameHandler handler)
        {
            if (handler == null)
            {
                return;
            }

            AudioSystem.Instance.OnMixedFrameRaw += handler;
        }

        public static void UnsubscribeMixedFrameRaw(AudioSystem.MixedFrameHandler handler)
        {
            if (handler == null)
            {
                return;
            }

            AudioSystem.Instance.OnMixedFrameRaw -= handler;
        }

        public readonly struct AuthorizationResult
        {
            public AuthorizationResult(bool authorized, string error)
            {
                Authorized = authorized;
                Error = error ?? string.Empty;
            }

            public bool Authorized { get; }
            public string Error { get; }
        }
    }
}
