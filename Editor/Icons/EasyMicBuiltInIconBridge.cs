using System;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Icons
{
    internal static class EasyMicBuiltInIconBridge
    {
        public static Texture TryGetTexture(EasyMicBuiltInIconId id)
        {
            string[] names = GetCandidateNames(id);
            for (int i = 0; i < names.Length; i++)
            {
                GUIContent content;
                try
                {
                    content = EditorGUIUtility.IconContent(names[i]);
                }
                catch (Exception)
                {
                    content = null;
                }

                if (content != null && content.image != null)
                {
                    return content.image;
                }
            }

            return null;
        }

        private static string[] GetCandidateNames(EasyMicBuiltInIconId id)
        {
            switch (id)
            {
                case EasyMicBuiltInIconId.Refresh:
                    return new[] { "Refresh" };
                case EasyMicBuiltInIconId.Add:
                    return new[] { "Toolbar Plus", "d_Toolbar Plus" };
                case EasyMicBuiltInIconId.Remove:
                    return new[] { "Toolbar Minus", "d_Toolbar Minus" };
                case EasyMicBuiltInIconId.Duplicate:
                    return new[] { "TreeEditor.Duplicate" };
                case EasyMicBuiltInIconId.Edit:
                    return new[] { "editicon.sml" };
                case EasyMicBuiltInIconId.Pick:
                    return new[] { "_Popup" };
                case EasyMicBuiltInIconId.Ping:
                    return new[] { "d_UnityEditor.SceneHierarchyWindow", "UnityEditor.SceneHierarchyWindow" };
                case EasyMicBuiltInIconId.Settings:
                    return new[] { "EditorSettings Icon", "d_Settings", "SettingsIcon" };
                default:
                    return Array.Empty<string>();
            }
        }
    }
}
