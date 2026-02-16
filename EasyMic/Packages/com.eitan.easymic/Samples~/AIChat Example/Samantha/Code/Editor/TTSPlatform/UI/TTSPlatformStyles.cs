// ============================================================================
// TTSPlatformStyles.cs - 编辑器UI样式定义
// 统一管理所有UI样式，便于主题切换和维护
// ============================================================================

using UnityEditor;
using UnityEngine;

namespace TTSPlatform.UI
{
    /// <summary>
    /// TTS平台编辑器样式
    /// </summary>
    public static class TTSPlatformStyles
    {
        private static GUIStyle _headerStyle;
        private static GUIStyle _subHeaderStyle;
        private static GUIStyle _boxStyle;
        private static GUIStyle _richLabelStyle;
        private static GUIStyle _buttonLargeStyle;
        private static GUIStyle _textAreaStyle;
        private static GUIStyle _statusSuccessStyle;
        private static GUIStyle _statusErrorStyle;

        public static GUIStyle Header
        {
            get
            {
                if (_headerStyle == null)
                {
                    _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 16,
                        alignment = TextAnchor.MiddleLeft,
                        margin = new RectOffset(0, 0, 10, 10)
                    };
                }
                return _headerStyle;
            }
        }

        public static GUIStyle SubHeader
        {
            get
            {
                if (_subHeaderStyle == null)
                {
                    _subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 12,
                        margin = new RectOffset(0, 0, 5, 5)
                    };
                }
                return _subHeaderStyle;
            }
        }

        public static GUIStyle Box
        {
            get
            {
                if (_boxStyle == null)
                {
                    _boxStyle = new GUIStyle("box")
                    {
                        padding = new RectOffset(10, 10, 10, 10),
                        margin = new RectOffset(0, 0, 5, 5)
                    };
                }
                return _boxStyle;
            }
        }

        public static GUIStyle RichLabel
        {
            get
            {
                if (_richLabelStyle == null)
                {
                    _richLabelStyle = new GUIStyle(EditorStyles.label)
                    {
                        richText = true,
                        wordWrap = true
                    };
                }
                return _richLabelStyle;
            }
        }

        public static GUIStyle ButtonLarge
        {
            get
            {
                if (_buttonLargeStyle == null)
                {
                    _buttonLargeStyle = new GUIStyle(GUI.skin.button)
                    {
                        fontSize = 12,
                        fixedHeight = 30
                    };
                }
                return _buttonLargeStyle;
            }
        }

        public static GUIStyle TextArea
        {
            get
            {
                if (_textAreaStyle == null)
                {
                    _textAreaStyle = new GUIStyle(EditorStyles.textArea)
                    {
                        wordWrap = true
                    };
                }
                return _textAreaStyle;
            }
        }

        public static GUIStyle StatusSuccess
        {
            get
            {
                if (_statusSuccessStyle == null)
                {
                    _statusSuccessStyle = new GUIStyle(EditorStyles.helpBox)
                    {
                        richText = true,
                        fontSize = 11
                    };
                }
                return _statusSuccessStyle;
            }
        }

        public static GUIStyle StatusError
        {
            get
            {
                if (_statusErrorStyle == null)
                {
                    _statusErrorStyle = new GUIStyle(EditorStyles.helpBox)
                    {
                        richText = true,
                        fontSize = 11
                    };
                }
                return _statusErrorStyle;
            }
        }

        /// <summary>重置所有样式（用于编辑器重载）</summary>
        public static void Reset()
        {
            _headerStyle = null;
            _subHeaderStyle = null;
            _boxStyle = null;
            _richLabelStyle = null;
            _buttonLargeStyle = null;
            _textAreaStyle = null;
            _statusSuccessStyle = null;
            _statusErrorStyle = null;
        }
    }
}
