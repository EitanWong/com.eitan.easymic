using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Icons
{
    /// <summary>
    /// Public facade for all EasyMic Editor icons. Callers request icons by semantic meaning,
    /// never by package file path or Unity built-in icon name.
    /// </summary>
    public static class EasyMicIcons
    {
        public const int Small = 16;
        public const int Medium = 32;
        public const int Large = 64;

        public static Texture2D Get(EasyMicIconId id, int size = Small, EasyMicIconState state = EasyMicIconState.Normal)
        {
            return EasyMicIconCache.Get(id, size, state);
        }

        public static GUIContent Content(EasyMicIconId id, string tooltip = null, int size = Small, EasyMicIconState state = EasyMicIconState.Normal)
        {
            return new GUIContent(Get(id, size, state), tooltip ?? string.Empty);
        }

        public static GUIContent LabeledContent(EasyMicIconId id, string text, string tooltip = null, int size = Small, EasyMicIconState state = EasyMicIconState.Normal)
        {
            return new GUIContent($" {text}", Get(id, size, state), tooltip ?? string.Empty);
        }

        public static GUIContent BuiltInContent(EasyMicBuiltInIconId builtInId, EasyMicIconId fallbackId, string fallbackText = null, string tooltip = null)
        {
            Texture image = EasyMicBuiltInIconBridge.TryGetTexture(builtInId);
            if (image != null)
            {
                return new GUIContent(image, tooltip ?? string.Empty);
            }

            Texture2D fallback = Get(fallbackId, Small);
            return string.IsNullOrEmpty(fallbackText)
                ? new GUIContent(fallback, tooltip ?? string.Empty)
                : new GUIContent(fallbackText, fallback, tooltip ?? string.Empty);
        }

        public static GUIContent LabeledBuiltInContent(EasyMicBuiltInIconId builtInId, EasyMicIconId fallbackId, string text, string tooltip = null)
        {
            Texture image = EasyMicBuiltInIconBridge.TryGetTexture(builtInId);
            return new GUIContent($" {text}", image != null ? image : Get(fallbackId, Small), tooltip ?? string.Empty);
        }

        public static void Invalidate()
        {
            EasyMicIconCache.Invalidate();
        }

        internal static int NormalizeSize(int size)
        {
            if (size <= Small)
            {
                return Small;
            }

            if (size <= Medium)
            {
                return Medium;
            }

            return Large;
        }
    }
}
