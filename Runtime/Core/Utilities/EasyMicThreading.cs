using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Minimal thread tagging used to prevent control-path work (locks/allocations) from running on
    /// native audio callback threads. This is an internal best-effort guard, not a security boundary.
    /// </summary>
    internal static class EasyMicThreading
    {
        [ThreadStatic] private static int s_audioThreadDepth;
        [ThreadStatic] private static int s_transportThreadDepth;

        internal static bool IsAudioThread => Volatile.Read(ref s_audioThreadDepth) > 0;
        internal static bool IsTransportThread => Volatile.Read(ref s_transportThreadDepth) > 0;
        internal static bool IsRealtimeSensitiveThread => IsAudioThread || IsTransportThread;

        internal static AudioThreadScope EnterAudioThread()
        {
            s_audioThreadDepth++;
            return new AudioThreadScope(entered: true);
        }

        internal static TransportThreadScope EnterTransportThread()
        {
            s_transportThreadDepth++;
            return new TransportThreadScope(entered: true);
        }

        internal readonly struct AudioThreadScope : IDisposable
        {
            private readonly bool _entered;

            internal AudioThreadScope(bool entered)
            {
                _entered = entered;
            }

            public void Dispose()
            {
                if (!_entered)
                    return;

                if (s_audioThreadDepth > 0)
                    s_audioThreadDepth--;
            }
        }

        internal readonly struct TransportThreadScope : IDisposable
        {
            private readonly bool _entered;

            internal TransportThreadScope(bool entered)
            {
                _entered = entered;
            }

            public void Dispose()
            {
                if (!_entered)
                {
                    return;
                }

                if (s_transportThreadDepth > 0)
                {
                    s_transportThreadDepth--;
                }
            }
        }
    }
}
