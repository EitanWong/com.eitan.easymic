#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using System.Collections.Generic;
using System.Threading;
using Eitan.SherpaONNXUnity.Runtime;

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.Services
{
    /// <summary>
    /// Reference-counted Sherpa module registry for EasyMic-owned workers, facades, and sessions.
    /// Do not call this registry from audio transport callbacks or reader hot paths.
    /// </summary>
    public sealed class SherpaModelServiceRegistry
    {
        private readonly object _syncRoot = new object();
        private readonly Dictionary<SherpaModelServiceKey, Entry> _entries = new Dictionary<SherpaModelServiceKey, Entry>();
        private int _shutdown;

        /// <summary>
        /// Gets whether <see cref="ReleaseAll"/> has shut down this registry.
        /// A shut down registry rejects new acquisitions and invalidates outstanding leases.
        /// </summary>
        public bool IsShutdown => Volatile.Read(ref _shutdown) != 0;

        /// <summary>
        /// Acquires a reference-counted module lease.
        /// This API may construct or dispose native-backed Sherpa modules; never call it from audio worker hot paths.
        /// </summary>
        public SherpaModuleLease<TModule> Acquire<TModule>(SherpaModelServiceKey key, Func<TModule> factory)
            where TModule : class, IDisposable
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            key = key.Normalized();

            lock (_syncRoot)
            {
                ThrowIfShutdown();

                if (_entries.TryGetValue(key, out var existing))
                {
                    if (!(existing.Module is TModule typed))
                    {
                        throw new InvalidOperationException(
                            $"Sherpa registry key '{key}' is already bound to module type '{existing.ModuleType.FullName}', not '{typeof(TModule).FullName}'.");
                    }

                    existing.RefCount++;
                    return new SherpaModuleLease<TModule>(this, key, typed);
                }

                var module = factory();
                if (module == null)
                {
                    throw new InvalidOperationException($"Sherpa registry factory returned null for key '{key}'.");
                }

                _entries.Add(key, new Entry(module, typeof(TModule), refCount: 1));
                return new SherpaModuleLease<TModule>(this, key, module);
            }
        }

        /// <summary>
        /// Returns an existing module without changing its reference count.
        /// The returned module is only observational; use <see cref="Acquire{TModule}"/> for ownership.
        /// </summary>
        public bool TryGetExisting<TModule>(SherpaModelServiceKey key, out TModule module)
            where TModule : class, IDisposable
        {
            key = key.Normalized();
            lock (_syncRoot)
            {
                if (IsShutdown)
                {
                    module = null;
                    return false;
                }

                if (_entries.TryGetValue(key, out var entry) && entry.Module is TModule typed)
                {
                    module = typed;
                    return true;
                }
            }

            module = null;
            return false;
        }

        /// <summary>
        /// Shuts down this registry and disposes all currently registered modules.
        /// Call this only from the owner shutdown path, after all active EasyMic/Sherpa sessions have stopped.
        /// Outstanding leases become invalid; accessing their <see cref="SherpaModuleLease{TModule}.Module"/> throws.
        /// </summary>
        public void ReleaseAll()
        {
            Entry[] entries;
            lock (_syncRoot)
            {
                if (Interlocked.Exchange(ref _shutdown, 1) != 0)
                {
                    return;
                }

                if (_entries.Count == 0)
                {
                    return;
                }

                entries = new Entry[_entries.Count];
                _entries.Values.CopyTo(entries, 0);
                _entries.Clear();
            }

            for (int i = 0; i < entries.Length; i++)
            {
                SafeDispose(entries[i].Module);
            }
        }

        public SherpaModelServiceRegistrySnapshot GetSnapshot()
        {
            lock (_syncRoot)
            {
                if (_entries.Count == 0)
                {
                    return new SherpaModelServiceRegistrySnapshot(
                        Array.Empty<SherpaModelServiceRegistryEntrySnapshot>(),
                        IsShutdown);
                }

                var rows = new SherpaModelServiceRegistryEntrySnapshot[_entries.Count];
                int index = 0;
                foreach (var pair in _entries)
                {
                    rows[index++] = new SherpaModelServiceRegistryEntrySnapshot(
                        pair.Key,
                        pair.Value.ModuleType,
                        pair.Value.RefCount);
                }

                return new SherpaModelServiceRegistrySnapshot(rows, IsShutdown);
            }
        }

        internal void Release(SherpaModelServiceKey key)
        {
            IDisposable moduleToDispose = null;
            key = key.Normalized();

            lock (_syncRoot)
            {
                if (IsShutdown)
                {
                    return;
                }

                if (!_entries.TryGetValue(key, out var entry))
                {
                    return;
                }

                entry.RefCount--;
                if (entry.RefCount > 0)
                {
                    return;
                }

                _entries.Remove(key);
                moduleToDispose = entry.Module;
            }

            SafeDispose(moduleToDispose);
        }

        private static void SafeDispose(IDisposable disposable)
        {
            try
            {
                disposable?.Dispose();
            }
            catch
            {
            }
        }

        private void ThrowIfShutdown()
        {
            if (IsShutdown)
            {
                throw new ObjectDisposedException(
                    nameof(SherpaModelServiceRegistry),
                    "This Sherpa model service registry has been shut down by ReleaseAll().");
            }
        }

        private sealed class Entry
        {
            public Entry(IDisposable module, Type moduleType, int refCount)
            {
                Module = module;
                ModuleType = moduleType;
                RefCount = refCount;
            }

            public IDisposable Module { get; }
            public Type ModuleType { get; }
            public int RefCount { get; set; }
        }
    }

    public readonly struct SherpaModelServiceKey : IEquatable<SherpaModelServiceKey>
    {
        public SherpaModelServiceKey(string moduleKind, string modelId, int sampleRate, int optionsHash = 0)
            : this(moduleKind, modelId, sampleRate, optionsHash, string.Empty)
        {
        }

        public SherpaModelServiceKey(string moduleKind, string modelId, int sampleRate, int optionsHash, string scopeId)
        {
            ModuleKind = Normalize(moduleKind);
            ModelId = Normalize(modelId);
            SampleRate = Math.Max(0, sampleRate);
            OptionsHash = optionsHash;
            ScopeId = Normalize(scopeId);
        }

        public string ModuleKind { get; }
        public string ModelId { get; }
        public int SampleRate { get; }
        public int OptionsHash { get; }
        public string ScopeId { get; }

        public SherpaModelServiceKey Normalized()
        {
            return new SherpaModelServiceKey(ModuleKind, ModelId, SampleRate, OptionsHash, ScopeId);
        }

        public bool Equals(SherpaModelServiceKey other)
        {
            return string.Equals(ModuleKind, other.ModuleKind, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ModelId, other.ModelId, StringComparison.OrdinalIgnoreCase) &&
                   SampleRate == other.SampleRate &&
                   OptionsHash == other.OptionsHash &&
                   string.Equals(ScopeId, other.ScopeId, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return obj is SherpaModelServiceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(ModuleKind ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(ModelId ?? string.Empty);
                hash = (hash * 397) ^ SampleRate;
                hash = (hash * 397) ^ OptionsHash;
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(ScopeId ?? string.Empty);
                return hash;
            }
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(ScopeId)
                ? $"{ModuleKind}:{ModelId}:{SampleRate}:{OptionsHash}"
                : $"{ModuleKind}:{ModelId}:{SampleRate}:{OptionsHash}:{ScopeId}";
        }

        public static bool operator ==(SherpaModelServiceKey left, SherpaModelServiceKey right) => left.Equals(right);

        public static bool operator !=(SherpaModelServiceKey left, SherpaModelServiceKey right) => !left.Equals(right);

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public sealed class SherpaModuleLease<TModule> : IDisposable
        where TModule : class, IDisposable
    {
        private SherpaModelServiceRegistry _registry;
        private readonly TModule _module;
        private int _disposed;

        internal SherpaModuleLease(SherpaModelServiceRegistry registry, SherpaModelServiceKey key, TModule module)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Key = key;
            _module = module ?? throw new ArgumentNullException(nameof(module));
        }

        public TModule Module
        {
            get
            {
                var registry = _registry;
                if (registry != null && registry.IsShutdown)
                {
                    throw new ObjectDisposedException(
                        nameof(SherpaModuleLease<TModule>),
                        "The owning Sherpa model service registry has been shut down.");
                }

                return _module;
            }
        }

        public SherpaModelServiceKey Key { get; }

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public bool IsValid
        {
            get
            {
                var registry = _registry;
                return !IsDisposed && registry != null && !registry.IsShutdown;
            }
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var registry = _registry;
            _registry = null;
            registry?.Release(Key);
        }
    }

    public readonly struct SherpaModelServiceRegistrySnapshot
    {
        public SherpaModelServiceRegistrySnapshot(SherpaModelServiceRegistryEntrySnapshot[] entries)
            : this(entries, false)
        {
        }

        public SherpaModelServiceRegistrySnapshot(SherpaModelServiceRegistryEntrySnapshot[] entries, bool isShutdown)
        {
            Entries = entries ?? Array.Empty<SherpaModelServiceRegistryEntrySnapshot>();
            IsShutdown = isShutdown;
        }

        public SherpaModelServiceRegistryEntrySnapshot[] Entries { get; }

        public bool IsShutdown { get; }
    }

    public readonly struct SherpaModelServiceRegistryEntrySnapshot
    {
        public SherpaModelServiceRegistryEntrySnapshot(SherpaModelServiceKey key, Type moduleType, int refCount)
        {
            Key = key;
            ModuleType = moduleType;
            RefCount = refCount;
        }

        public SherpaModelServiceKey Key { get; }
        public Type ModuleType { get; }
        public int RefCount { get; }
    }

    public static class SherpaModelServiceKeys
    {
        public static SherpaModelServiceKey ForModule<TModule>(string modelId, int sampleRate, int optionsHash = 0)
            where TModule : SherpaONNXModule
        {
            return new SherpaModelServiceKey(typeof(TModule).Name, modelId, sampleRate, optionsHash);
        }

        public static SherpaModelServiceKey ForModule<TModule>(string modelId, int sampleRate, int optionsHash, string scopeId)
            where TModule : SherpaONNXModule
        {
            return new SherpaModelServiceKey(typeof(TModule).Name, modelId, sampleRate, optionsHash, scopeId);
        }
    }
}
#endif
