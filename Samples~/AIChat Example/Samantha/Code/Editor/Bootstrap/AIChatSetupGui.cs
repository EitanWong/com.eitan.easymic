#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public static class AIChatSetupGui
    {
        private static bool _initialized;
        private static bool _proSkin;
        private static GUIStyle _hero;
        private static GUIStyle _heroTitle;
        private static GUIStyle _heroDescription;
        private static GUIStyle _badge;
        private static GUIStyle _card;
        private static GUIStyle _cardTitle;
        private static GUIStyle _body;
        private static GUIStyle _sectionIndex;

        public static Color AccentColor => new Color(0.18f, 0.48f, 0.88f);
        public static Color SuccessColor => new Color(0.20f, 0.62f, 0.34f);
        public static Color WarningColor => new Color(0.88f, 0.56f, 0.16f);

        public static GUIStyle CardStyle
        {
            get
            {
                EnsureStyles();
                return _card;
            }
        }

        public static GUIStyle BodyStyle
        {
            get
            {
                EnsureStyles();
                return _body;
            }
        }

        public static void DrawHero(string iconName, string title, string description, string badge)
        {
            EnsureStyles();
            using (new EditorGUILayout.VerticalScope(_hero))
            {
                Rect accent = EditorGUILayout.GetControlRect(false, 3f);
                EditorGUI.DrawRect(accent, AccentColor);
                GUILayout.Space(7f);

                bool compact = EditorGUIUtility.currentViewWidth < 420f;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUIContent icon = EditorGUIUtility.IconContent(iconName);
                    if (icon != null && icon.image != null)
                    {
                        GUILayout.Label(icon.image, GUILayout.Width(34f), GUILayout.Height(34f));
                        GUILayout.Space(8f);
                    }

                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Label(title, _heroTitle);
                        GUILayout.Label(description, _heroDescription);
                    }

                    if (!compact && !string.IsNullOrWhiteSpace(badge))
                    {
                        GUILayout.Space(8f);
                        GUILayout.Label(badge, _badge, GUILayout.ExpandWidth(false));
                    }
                }

                if (compact && !string.IsNullOrWhiteSpace(badge))
                {
                    GUILayout.Space(6f);
                    GUILayout.Label(badge, _badge, GUILayout.ExpandWidth(false));
                }
            }
        }

        public static void DrawSectionHeader(int index, string title, string description = null)
        {
            EnsureStyles();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(index.ToString(), _sectionIndex, GUILayout.Width(25f), GUILayout.Height(22f));
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(title, _cardTitle);
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        GUILayout.Label(description, _body);
                    }
                }
            }
            GUILayout.Space(6f);
        }

        public static void DrawStatusCard(int index, string title, string detail, bool passed)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = passed ? SuccessColor : WarningColor;
            using (new EditorGUILayout.VerticalScope(CardStyle))
            {
                GUI.backgroundColor = previous;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUIContent icon = EditorGUIUtility.IconContent(passed ? "TestPassed" : "console.warnicon.sml");
                    if (icon != null && icon.image != null)
                    {
                        GUILayout.Label(icon.image, GUILayout.Width(18f), GUILayout.Height(18f));
                    }

                    GUILayout.Label($"{index}. {title}", _cardTitle);
                }
                GUILayout.Label(detail, _body);
            }
            GUI.backgroundColor = previous;
        }

        public static bool PrimaryButton(string label, float height = 34f)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = AccentColor;
            bool clicked = GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Height(height));
            GUI.backgroundColor = previous;
            return clicked;
        }

        public static bool SecondaryButton(string label, float height = 26f)
        {
            return GUILayout.Button(label, GUILayout.Height(height));
        }

        public static void DrawContract(string title, string detail)
        {
            EnsureStyles();
            using (new EditorGUILayout.VerticalScope(CardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUIContent icon = EditorGUIUtility.IconContent("d_Valid");
                    if (icon != null && icon.image != null)
                    {
                        GUILayout.Label(icon.image, GUILayout.Width(18f), GUILayout.Height(18f));
                    }
                    GUILayout.Label(title, _cardTitle);
                }
                GUILayout.Label(detail, _body);
            }
        }

        public static void DrawDivider()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.12f)
                : new Color(0f, 0f, 0f, 0.14f));
        }

        private static void EnsureStyles()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            if (_initialized && _proSkin == proSkin)
            {
                return;
            }

            _initialized = true;
            _proSkin = proSkin;
            _hero = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(14, 14, 10, 12),
                margin = new RectOffset(8, 8, 8, 10)
            };
            _heroTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                wordWrap = true
            };
            _heroDescription = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11
            };
            _badge = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(8, 8, 3, 3)
            };
            _card = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(8, 8, 4, 6)
            };
            _cardTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = true
            };
            _body = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11
            };
            _sectionIndex = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fixedWidth = 25f,
                fixedHeight = 22f
            };
        }
    }
}

#endif
