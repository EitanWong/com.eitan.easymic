using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Icons
{
    /// <summary>
    /// Explicit, idempotent component icon maintenance for EasyMic MonoScript assets.
    /// This class intentionally does not run on import.
    /// </summary>
    public static class EasyMicComponentIconInstaller
    {
        private const int ComponentIconSize = EasyMicIcons.Large;

        public readonly struct RefreshResult
        {
            public readonly int MatchedScripts;
            public readonly int UpdatedScripts;
            public readonly int MissingScripts;

            public RefreshResult(int matchedScripts, int updatedScripts, int missingScripts)
            {
                MatchedScripts = matchedScripts;
                UpdatedScripts = updatedScripts;
                MissingScripts = missingScripts;
            }
        }

        public static RefreshResult RefreshComponentIcons()
        {
            int matched = 0;
            int updated = 0;
            int missing = 0;

            IReadOnlyList<EasyMicComponentIconMap.Entry> entries = EasyMicComponentIconMap.All;
            for (int i = 0; i < entries.Count; i++)
            {
                EasyMicComponentIconMap.Entry entry = entries[i];
                MonoScript script = FindMonoScript(entry);
                if (script == null)
                {
                    missing++;
                    continue;
                }

                matched++;
                if (ApplyIcon(script, entry.IconId))
                {
                    updated++;
                }
            }

            if (updated > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return new RefreshResult(matched, updated, missing);
        }

        public static void ApplyTemporaryIcon(UnityEngine.Object target, EasyMicIconId iconId)
        {
            if (target == null)
            {
                return;
            }

            EditorGUIUtility.SetIconForObject(target, EasyMicIcons.Get(iconId, ComponentIconSize));
        }

        public static void ApplyTemporaryIcon(Component component)
        {
            if (component == null)
            {
                return;
            }

            Type type = component.GetType();
            if (!EasyMicComponentIconMap.TryGetIconId(type, out EasyMicIconId iconId))
            {
                iconId = EasyMicIconId.EasyMic;
            }

            ApplyTemporaryIcon(component.gameObject, iconId);
            EditorGUIUtility.SetIconForObject(component, EasyMicIcons.Get(iconId, ComponentIconSize));
        }

        private static bool ApplyIcon(MonoScript script, EasyMicIconId iconId)
        {
            string assetPath = AssetDatabase.GetAssetPath(script);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            var importer = AssetImporter.GetAtPath(assetPath) as MonoImporter;
            if (importer == null)
            {
                return false;
            }

            Texture2D icon = EasyMicIcons.Get(iconId, ComponentIconSize);
            if (HasEasyMicIcon(importer.GetIcon(), iconId))
            {
                return false;
            }

            importer.SetIcon(icon);
            importer.SaveAndReimport();
            return true;
        }

        private static bool HasEasyMicIcon(Texture2D currentIcon, EasyMicIconId iconId)
        {
            return currentIcon != null
                && !string.IsNullOrEmpty(currentIcon.name)
                && currentIcon.name.IndexOf($"EasyMic_{iconId}_", StringComparison.Ordinal) >= 0;
        }

        private static MonoScript FindMonoScript(EasyMicComponentIconMap.Entry entry)
        {
            string[] guids = AssetDatabase.FindAssets($"{entry.ScriptName} t:MonoScript", new[] { "Packages/com.eitan.easymic/Runtime" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null)
                {
                    continue;
                }

                Type scriptClass = script.GetClass();
                if (scriptClass != null && scriptClass.FullName == entry.TypeFullName)
                {
                    return script;
                }

                if (scriptClass == null && path.EndsWith($"/{entry.ScriptName}.cs", StringComparison.Ordinal))
                {
                    return script;
                }
            }

            return null;
        }
    }
}
