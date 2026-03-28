using System;
using System.Threading;
using UnityEngine;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Stores Unity's main-thread identity/context for APIs that must avoid worker-thread JNI calls on Android.
    /// </summary>
    internal static class EasyMicUnityThread
    {
        private static int s_mainThreadId;
        private static SynchronizationContext s_mainContext;

        internal static SynchronizationContext MainContext => Volatile.Read(ref s_mainContext);

        internal static bool IsMainThread
        {
            get
            {
                int threadId = Volatile.Read(ref s_mainThreadId);
                return threadId != 0 && Thread.CurrentThread.ManagedThreadId == threadId;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CaptureOnStartup()
        {
            var context = SynchronizationContext.Current;
            if (context == null)
            {
                return;
            }

            Volatile.Write(ref s_mainThreadId, Thread.CurrentThread.ManagedThreadId);
            Volatile.Write(ref s_mainContext, context);
        }

        internal static void TryCaptureFromCurrentThread()
        {
            var context = SynchronizationContext.Current;
            if (context == null)
            {
                return;
            }

            // Ignore generic/default contexts from worker threads.
            // We only accept Unity's main-thread synchronization context.
            if (!IsUnitySynchronizationContext(context))
            {
                return;
            }

            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            int knownThreadId = Volatile.Read(ref s_mainThreadId);

            if (knownThreadId == 0)
            {
                Volatile.Write(ref s_mainThreadId, currentThreadId);
                Volatile.Write(ref s_mainContext, context);
                return;
            }

            if (knownThreadId == currentThreadId)
            {
                Volatile.Write(ref s_mainContext, context);
            }
        }

        private static bool IsUnitySynchronizationContext(SynchronizationContext context)
        {
            var typeName = context.GetType().FullName;
            return !string.IsNullOrEmpty(typeName) && typeName.IndexOf("UnitySynchronizationContext", System.StringComparison.Ordinal) >= 0;
        }
    }
}
