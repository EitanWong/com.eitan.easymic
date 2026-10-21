using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    internal static class AudioTestAccess
    {
        internal const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        internal const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        internal static object Get(object owner, string name) => owner.GetType().GetField(name, Instance).GetValue(owner);
        internal static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Instance).SetValue(owner, value);
        internal static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Instance).Invoke(owner, args);
        internal static void Eventually(Func<bool> predicate, string message)
            => Assert.That(SpinWait.SpinUntil(predicate, 3000), Is.True, message);
    }

    // Records worker posts; the test controls when callbacks run on its own thread.
    internal sealed class QueuedAudioContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> _pending = new ConcurrentQueue<Action>();
        internal int Count => _pending.Count;
        public override void Post(SendOrPostCallback callback, object state) => _pending.Enqueue(() => callback(state));
        internal void Drain() { while (_pending.TryDequeue(out var action)) action(); }
    }

    internal sealed class ManagedPlaybackTestSystem : IDisposable
    {
        private readonly FieldInfo _instance = typeof(AudioSystem).GetField("s_instance", AudioTestAccess.Static);
        private readonly AudioSystem _previous;
        internal AudioSystem System { get; }
        internal AudioMixer Mixer { get; } = new AudioMixer();
        internal ManagedPlaybackTestSystem()
        {
            _previous = (AudioSystem)_instance.GetValue(null);
            System = (AudioSystem)Activator.CreateInstance(typeof(AudioSystem), true);
            System.Configure(48000, 2);
            AudioTestAccess.Set(System, "_masterMixer", Mixer);
            // Only simulate the control state. No device, callback or render thread is created.
            AudioTestAccess.Set(System, "_running", true);
            _instance.SetValue(null, System);
        }
        public void Dispose()
        {
            AudioTestAccess.Set(System, "_running", false);
            System.Dispose();
            Mixer.Dispose();
            _instance.SetValue(null, _previous);
        }
    }
}
