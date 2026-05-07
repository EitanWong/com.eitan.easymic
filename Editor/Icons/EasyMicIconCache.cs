using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Icons
{
    internal static class EasyMicIconCache
    {
        private readonly struct Key
        {
            private readonly EasyMicIconId _id;
            private readonly int _size;
            private readonly bool _proSkin;
            private readonly EasyMicIconState _state;

            public Key(EasyMicIconId id, int size, bool proSkin, EasyMicIconState state)
            {
                _id = id;
                _size = size;
                _proSkin = proSkin;
                _state = state;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)_id;
                    hash = (hash * 397) ^ _size;
                    hash = (hash * 397) ^ (_proSkin ? 1 : 0);
                    hash = (hash * 397) ^ (int)_state;
                    return hash;
                }
            }

            public override bool Equals(object obj)
            {
                return obj is Key other
                    && other._id == _id
                    && other._size == _size
                    && other._proSkin == _proSkin
                    && other._state == _state;
            }
        }

        private static readonly Dictionary<Key, Texture2D> Textures = new Dictionary<Key, Texture2D>(128);

        public static Texture2D Get(EasyMicIconId id, int size, EasyMicIconState state)
        {
            size = EasyMicIcons.NormalizeSize(size);
            EasyMicIconTheme theme = EasyMicIconTheme.Current;
            var key = new Key(id, size, theme.ProSkin, state);
            if (Textures.TryGetValue(key, out Texture2D texture) && texture != null)
            {
                return texture;
            }

            texture = EasyMicProceduralIconRenderer.Render(id, size, theme, state);
            texture.name = $"EasyMic_{id}_{size}_{(theme.ProSkin ? "Dark" : "Light")}_{state}";
            texture.hideFlags = HideFlags.HideAndDontSave;
            Textures[key] = texture;
            return texture;
        }

        public static void Invalidate()
        {
            foreach (Texture2D texture in Textures.Values)
            {
                if (texture != null)
                {
                    Object.DestroyImmediate(texture);
                }
            }

            Textures.Clear();
        }
    }
}
