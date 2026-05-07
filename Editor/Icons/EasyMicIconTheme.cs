using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Icons
{
    internal readonly struct EasyMicIconTheme
    {
        public readonly bool ProSkin;
        public readonly Color Primary;
        public readonly Color Secondary;
        public readonly Color Accent;
        public readonly Color Muted;
        public readonly Color Warning;
        public readonly Color Success;
        public readonly Color Error;

        private EasyMicIconTheme(bool proSkin)
        {
            ProSkin = proSkin;
            Primary = proSkin ? Rgb(226, 231, 236) : Rgb(43, 50, 57);
            Secondary = proSkin ? Rgb(132, 145, 158) : Rgb(93, 105, 116);
            Accent = proSkin ? Rgb(68, 190, 214) : Rgb(17, 132, 158);
            Muted = proSkin ? Rgb(88, 96, 104) : Rgb(151, 159, 166);
            Warning = proSkin ? Rgb(238, 184, 74) : Rgb(176, 112, 24);
            Success = proSkin ? Rgb(84, 203, 132) : Rgb(34, 139, 82);
            Error = proSkin ? Rgb(234, 101, 96) : Rgb(181, 50, 47);
        }

        public static EasyMicIconTheme Current => new EasyMicIconTheme(EditorGUIUtility.isProSkin);

        public Color ForState(Color color, EasyMicIconState state)
        {
            if (state == EasyMicIconState.Disabled)
            {
                return new Color(color.r, color.g, color.b, color.a * 0.42f);
            }

            if (state == EasyMicIconState.Active)
            {
                return Color.Lerp(color, Accent, 0.22f);
            }

            return color;
        }

        private static Color Rgb(byte r, byte g, byte b)
        {
            return new Color32(r, g, b, 255);
        }
    }
}
