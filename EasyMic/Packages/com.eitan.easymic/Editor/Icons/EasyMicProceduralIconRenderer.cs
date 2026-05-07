using System;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Icons
{
    internal static class EasyMicProceduralIconRenderer
    {
        public static Texture2D Render(EasyMicIconId id, int size, EasyMicIconTheme theme, EasyMicIconState state)
        {
            var canvas = new IconCanvas(size);
            Draw(id, canvas, theme, state);

            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(canvas.Pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static void Draw(EasyMicIconId id, IconCanvas c, EasyMicIconTheme theme, EasyMicIconState state)
        {
            Color primary = theme.ForState(theme.Primary, state);
            Color secondary = theme.ForState(theme.Secondary, state);
            Color accent = theme.ForState(theme.Accent, state);

            switch (id)
            {
                case EasyMicIconId.EasyMic:
                    DrawMicrophone(c, primary, accent);
                    DrawWave(c, accent, 0.64f, 0.5f, 0.24f, 2.0f);
                    break;
                case EasyMicIconId.Microphone:
                    DrawMicrophone(c, primary, secondary);
                    break;
                case EasyMicIconId.VoiceMicrophone:
                    DrawMicrophone(c, primary, secondary);
                    c.StrokeRoundedRect(0.58f, 0.18f, 0.26f, 0.22f, 0.08f, accent, 0.055f);
                    c.FillTriangle(0.66f, 0.4f, 0.72f, 0.5f, 0.75f, 0.38f, accent);
                    c.StrokePolyline(accent, 0.045f, 0.63f, 0.29f, 0.67f, 0.24f, 0.71f, 0.34f, 0.75f, 0.25f, 0.79f, 0.3f);
                    break;
                case EasyMicIconId.AudioInput:
                    DrawMicrophone(c, primary, secondary);
                    DrawArrow(c, accent, 0.78f, 0.5f, 0.56f, 0.5f);
                    break;
                case EasyMicIconId.AudioOutput:
                case EasyMicIconId.PlaybackSource:
                    DrawSpeaker(c, primary, secondary);
                    DrawWave(c, accent, 0.62f, 0.5f, 0.2f, 2.0f);
                    break;
                case EasyMicIconId.DeviceBinding:
                    DrawChip(c, primary, secondary);
                    DrawLock(c, accent);
                    break;
                case EasyMicIconId.RuntimeBridge:
                    c.StrokeCircle(0.31f, 0.5f, 0.13f, primary, 0.09f);
                    c.StrokeCircle(0.69f, 0.5f, 0.13f, primary, 0.09f);
                    c.StrokeLine(0.42f, 0.5f, 0.58f, 0.5f, accent, 0.1f);
                    break;
                case EasyMicIconId.Diagnostics:
                    c.StrokeRoundedRect(0.16f, 0.2f, 0.68f, 0.6f, 0.08f, secondary, 0.055f);
                    c.StrokePolyline(primary, 0.065f, 0.22f, 0.6f, 0.35f, 0.6f, 0.43f, 0.39f, 0.54f, 0.7f, 0.64f, 0.48f, 0.78f, 0.48f);
                    break;
                case EasyMicIconId.Waveform:
                    DrawWaveform(c, primary, accent);
                    break;
                case EasyMicIconId.GainControl:
                    DrawGainBars(c, primary, accent);
                    break;
                case EasyMicIconId.Aec:
                    DrawMicrophone(c, primary, secondary);
                    c.StrokeArc(0.67f, 0.44f, 0.22f, -70f, 86f, accent, 0.07f);
                    c.StrokeLine(0.58f, 0.67f, 0.8f, 0.28f, theme.Error, 0.075f);
                    break;
                case EasyMicIconId.NoiseSuppression:
                    DrawWaveform(c, primary, accent);
                    c.FillCircle(0.25f, 0.29f, 0.04f, secondary);
                    c.FillCircle(0.72f, 0.73f, 0.035f, secondary);
                    c.StrokeLine(0.2f, 0.8f, 0.8f, 0.2f, theme.Error, 0.065f);
                    break;
                case EasyMicIconId.VoiceActivity:
                case EasyMicIconId.SpeechRecognition:
                    c.StrokeRoundedRect(0.15f, 0.24f, 0.7f, 0.42f, 0.16f, primary, 0.07f);
                    c.FillTriangle(0.42f, 0.66f, 0.5f, 0.8f, 0.56f, 0.65f, primary);
                    DrawWaveform(c, accent, accent);
                    break;
                case EasyMicIconId.SpeechSynthesis:
                    DrawSpeaker(c, primary, secondary);
                    c.StrokeRoundedRect(0.43f, 0.22f, 0.38f, 0.24f, 0.08f, accent, 0.06f);
                    c.FillTriangle(0.56f, 0.46f, 0.62f, 0.56f, 0.66f, 0.45f, accent);
                    break;
                case EasyMicIconId.Keyword:
                    DrawKey(c, primary, accent);
                    break;
                case EasyMicIconId.License:
                    c.StrokeRoundedRect(0.18f, 0.18f, 0.64f, 0.64f, 0.08f, primary, 0.07f);
                    DrawKey(c, accent, accent);
                    break;
                case EasyMicIconId.Settings:
                    DrawGear(c, primary, accent);
                    break;
                case EasyMicIconId.Refresh:
                    c.StrokeArc(0.5f, 0.5f, 0.28f, -35f, 250f, primary, 0.08f);
                    c.FillTriangle(0.72f, 0.28f, 0.83f, 0.32f, 0.74f, 0.4f, primary);
                    break;
                case EasyMicIconId.Add:
                    DrawPlus(c, primary);
                    break;
                case EasyMicIconId.Remove:
                    c.StrokeLine(0.25f, 0.5f, 0.75f, 0.5f, primary, 0.12f);
                    break;
                case EasyMicIconId.Duplicate:
                    c.StrokeRoundedRect(0.2f, 0.28f, 0.42f, 0.46f, 0.06f, secondary, 0.065f);
                    c.StrokeRoundedRect(0.36f, 0.18f, 0.44f, 0.48f, 0.06f, primary, 0.065f);
                    break;
                case EasyMicIconId.Edit:
                    c.StrokeLine(0.26f, 0.72f, 0.7f, 0.28f, primary, 0.1f);
                    c.FillTriangle(0.66f, 0.23f, 0.78f, 0.35f, 0.72f, 0.2f, accent);
                    c.StrokeLine(0.22f, 0.78f, 0.44f, 0.72f, secondary, 0.07f);
                    break;
                case EasyMicIconId.Pick:
                    c.FillTriangle(0.25f, 0.36f, 0.75f, 0.36f, 0.5f, 0.66f, primary);
                    break;
                case EasyMicIconId.Ping:
                    c.StrokeCircle(0.5f, 0.5f, 0.28f, primary, 0.065f);
                    c.StrokeLine(0.5f, 0.18f, 0.5f, 0.33f, accent, 0.07f);
                    c.StrokeLine(0.5f, 0.67f, 0.5f, 0.82f, accent, 0.07f);
                    c.StrokeLine(0.18f, 0.5f, 0.33f, 0.5f, accent, 0.07f);
                    c.StrokeLine(0.67f, 0.5f, 0.82f, 0.5f, accent, 0.07f);
                    break;
                case EasyMicIconId.Warning:
                    c.StrokeTriangle(0.5f, 0.17f, 0.85f, 0.78f, 0.15f, 0.78f, theme.Warning, 0.075f);
                    c.StrokeLine(0.5f, 0.36f, 0.5f, 0.58f, theme.Warning, 0.08f);
                    c.FillCircle(0.5f, 0.68f, 0.04f, theme.Warning);
                    break;
                case EasyMicIconId.Success:
                    c.StrokeCircle(0.5f, 0.5f, 0.33f, theme.Success, 0.075f);
                    c.StrokePolyline(theme.Success, 0.09f, 0.32f, 0.51f, 0.45f, 0.65f, 0.71f, 0.36f);
                    break;
                case EasyMicIconId.Error:
                    c.StrokeCircle(0.5f, 0.5f, 0.33f, theme.Error, 0.075f);
                    c.StrokeLine(0.36f, 0.36f, 0.64f, 0.64f, theme.Error, 0.085f);
                    c.StrokeLine(0.64f, 0.36f, 0.36f, 0.64f, theme.Error, 0.085f);
                    break;
                default:
                    DrawMicrophone(c, primary, accent);
                    break;
            }
        }

        private static void DrawMicrophone(IconCanvas c, Color primary, Color accent)
        {
            c.FillRoundedRect(0.38f, 0.16f, 0.24f, 0.45f, 0.12f, primary);
            c.StrokeArc(0.5f, 0.48f, 0.24f, 18f, 162f, accent, 0.07f);
            c.StrokeLine(0.5f, 0.68f, 0.5f, 0.82f, primary, 0.075f);
            c.StrokeLine(0.34f, 0.82f, 0.66f, 0.82f, primary, 0.075f);
        }

        private static void DrawSpeaker(IconCanvas c, Color primary, Color secondary)
        {
            c.FillRoundedRect(0.17f, 0.39f, 0.17f, 0.22f, 0.04f, secondary);
            c.FillTriangle(0.31f, 0.39f, 0.52f, 0.24f, 0.52f, 0.76f, primary);
            c.FillTriangle(0.31f, 0.61f, 0.52f, 0.76f, 0.31f, 0.39f, primary);
        }

        private static void DrawWave(IconCanvas c, Color color, float centerX, float centerY, float radius, float thickness)
        {
            c.StrokeArc(centerX, centerY, radius, -52f, 52f, color, 0.035f * thickness);
            c.StrokeArc(centerX, centerY, radius * 1.45f, -48f, 48f, color, 0.03f * thickness);
        }

        private static void DrawWaveform(IconCanvas c, Color primary, Color accent)
        {
            c.StrokePolyline(primary, 0.075f, 0.18f, 0.55f, 0.29f, 0.55f, 0.37f, 0.35f, 0.5f, 0.72f, 0.63f, 0.28f, 0.72f, 0.55f, 0.82f, 0.55f);
            c.FillCircle(0.5f, 0.72f, 0.045f, accent);
        }

        private static void DrawGainBars(IconCanvas c, Color primary, Color accent)
        {
            c.FillRoundedRect(0.2f, 0.58f, 0.12f, 0.22f, 0.035f, primary);
            c.FillRoundedRect(0.44f, 0.4f, 0.12f, 0.4f, 0.035f, primary);
            c.FillRoundedRect(0.68f, 0.2f, 0.12f, 0.6f, 0.035f, accent);
        }

        private static void DrawChip(IconCanvas c, Color primary, Color secondary)
        {
            c.StrokeRoundedRect(0.27f, 0.24f, 0.46f, 0.48f, 0.08f, primary, 0.07f);
            for (int i = 0; i < 3; i++)
            {
                float x = 0.33f + i * 0.17f;
                c.StrokeLine(x, 0.16f, x, 0.24f, secondary, 0.045f);
                c.StrokeLine(x, 0.72f, x, 0.84f, secondary, 0.045f);
            }
        }

        private static void DrawLock(IconCanvas c, Color color)
        {
            c.StrokeArc(0.5f, 0.46f, 0.13f, 200f, 340f, color, 0.055f);
            c.FillRoundedRect(0.37f, 0.48f, 0.26f, 0.2f, 0.045f, color);
        }

        private static void DrawKey(IconCanvas c, Color primary, Color accent)
        {
            c.StrokeCircle(0.33f, 0.43f, 0.13f, primary, 0.07f);
            c.StrokeLine(0.45f, 0.52f, 0.78f, 0.76f, accent, 0.075f);
            c.StrokeLine(0.66f, 0.67f, 0.62f, 0.78f, accent, 0.06f);
            c.StrokeLine(0.74f, 0.72f, 0.7f, 0.83f, accent, 0.06f);
        }

        private static void DrawGear(IconCanvas c, Color primary, Color accent)
        {
            c.StrokeCircle(0.5f, 0.5f, 0.23f, primary, 0.09f);
            c.FillCircle(0.5f, 0.5f, 0.08f, accent);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                float x0 = 0.5f + Mathf.Cos(a) * 0.29f;
                float y0 = 0.5f + Mathf.Sin(a) * 0.29f;
                float x1 = 0.5f + Mathf.Cos(a) * 0.38f;
                float y1 = 0.5f + Mathf.Sin(a) * 0.38f;
                c.StrokeLine(x0, y0, x1, y1, primary, 0.065f);
            }
        }

        private static void DrawPlus(IconCanvas c, Color color)
        {
            c.StrokeLine(0.25f, 0.5f, 0.75f, 0.5f, color, 0.12f);
            c.StrokeLine(0.5f, 0.25f, 0.5f, 0.75f, color, 0.12f);
        }

        private static void DrawArrow(IconCanvas c, Color color, float x0, float y0, float x1, float y1)
        {
            c.StrokeLine(x0, y0, x1, y1, color, 0.075f);
            float dx = x1 - x0;
            float dy = y1 - y0;
            float angle = Mathf.Atan2(dy, dx);
            float head = 0.11f;
            c.StrokeLine(x1, y1, x1 - Mathf.Cos(angle - 0.75f) * head, y1 - Mathf.Sin(angle - 0.75f) * head, color, 0.075f);
            c.StrokeLine(x1, y1, x1 - Mathf.Cos(angle + 0.75f) * head, y1 - Mathf.Sin(angle + 0.75f) * head, color, 0.075f);
        }

        private sealed class IconCanvas
        {
            private readonly int _size;
            public readonly Color32[] Pixels;

            public IconCanvas(int size)
            {
                _size = size;
                Pixels = new Color32[size * size];
            }

            public void FillCircle(float cx, float cy, float r, Color color)
            {
                ForEachPixel((x, y) => Blend(x, y, color, Smooth(r * _size - Distance(x, y, cx, cy) * _size)));
            }

            public void StrokeCircle(float cx, float cy, float r, Color color, float width)
            {
                ForEachPixel((x, y) =>
                {
                    float d = Mathf.Abs(Distance(x, y, cx, cy) - r) * _size;
                    Blend(x, y, color, Smooth(width * _size * 0.5f - d));
                });
            }

            public void StrokeArc(float cx, float cy, float r, float startDeg, float endDeg, Color color, float width)
            {
                ForEachPixel((x, y) =>
                {
                    float px = NormalizedX(x) - cx;
                    float py = NormalizedY(y) - cy;
                    float angle = Mathf.Atan2(py, px) * Mathf.Rad2Deg;
                    if (angle < 0f) angle += 360f;
                    float start = startDeg < 0f ? startDeg + 360f : startDeg;
                    float end = endDeg < 0f ? endDeg + 360f : endDeg;
                    bool inside = start <= end ? angle >= start && angle <= end : angle >= start || angle <= end;
                    if (!inside)
                    {
                        return;
                    }

                    float d = Mathf.Abs(Mathf.Sqrt(px * px + py * py) - r) * _size;
                    Blend(x, y, color, Smooth(width * _size * 0.5f - d));
                });
            }

            public void StrokeLine(float x0, float y0, float x1, float y1, Color color, float width)
            {
                ForEachPixel((x, y) =>
                {
                    float d = DistanceToSegment(NormalizedX(x), NormalizedY(y), x0, y0, x1, y1) * _size;
                    Blend(x, y, color, Smooth(width * _size * 0.5f - d));
                });
            }

            public void StrokePolyline(Color color, float width, params float[] points)
            {
                for (int i = 0; i + 3 < points.Length; i += 2)
                {
                    StrokeLine(points[i], points[i + 1], points[i + 2], points[i + 3], color, width);
                }
            }

            public void FillRoundedRect(float x, float y, float w, float h, float r, Color color)
            {
                ForEachPixel((px, py) =>
                {
                    float nx = NormalizedX(px);
                    float ny = NormalizedY(py);
                    float qx = Mathf.Abs(nx - (x + w * 0.5f)) - (w * 0.5f - r);
                    float qy = Mathf.Abs(ny - (y + h * 0.5f)) - (h * 0.5f - r);
                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
                    float d = (outside + inside - r) * _size;
                    Blend(px, py, color, Smooth(-d));
                });
            }

            public void StrokeRoundedRect(float x, float y, float w, float h, float r, Color color, float width)
            {
                ForEachPixel((px, py) =>
                {
                    float nx = NormalizedX(px);
                    float ny = NormalizedY(py);
                    float qx = Mathf.Abs(nx - (x + w * 0.5f)) - (w * 0.5f - r);
                    float qy = Mathf.Abs(ny - (y + h * 0.5f)) - (h * 0.5f - r);
                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
                    float d = Mathf.Abs(outside + inside - r) * _size;
                    Blend(px, py, color, Smooth(width * _size * 0.5f - d));
                });
            }

            public void FillTriangle(float x0, float y0, float x1, float y1, float x2, float y2, Color color)
            {
                ForEachPixel((px, py) =>
                {
                    float x = NormalizedX(px);
                    float y = NormalizedY(py);
                    float a = Edge(x0, y0, x1, y1, x, y);
                    float b = Edge(x1, y1, x2, y2, x, y);
                    float c = Edge(x2, y2, x0, y0, x, y);
                    bool inside = (a >= 0f && b >= 0f && c >= 0f) || (a <= 0f && b <= 0f && c <= 0f);
                    if (inside)
                    {
                        Blend(px, py, color, 1f);
                    }
                });
            }

            public void StrokeTriangle(float x0, float y0, float x1, float y1, float x2, float y2, Color color, float width)
            {
                StrokeLine(x0, y0, x1, y1, color, width);
                StrokeLine(x1, y1, x2, y2, color, width);
                StrokeLine(x2, y2, x0, y0, color, width);
            }

            private void ForEachPixel(Action<int, int> action)
            {
                for (int y = 0; y < _size; y++)
                {
                    for (int x = 0; x < _size; x++)
                    {
                        action(x, y);
                    }
                }
            }

            private void Blend(int x, int y, Color color, float coverage)
            {
                coverage = Mathf.Clamp01(coverage);
                if (coverage <= 0f)
                {
                    return;
                }

                int index = y * _size + x;
                Color dst = Pixels[index];
                float srcA = color.a * coverage;
                float outA = srcA + dst.a * (1f - srcA);
                if (outA <= 0f)
                {
                    Pixels[index] = new Color32(0, 0, 0, 0);
                    return;
                }

                float r = (color.r * srcA + dst.r * dst.a * (1f - srcA)) / outA;
                float g = (color.g * srcA + dst.g * dst.a * (1f - srcA)) / outA;
                float b = (color.b * srcA + dst.b * dst.a * (1f - srcA)) / outA;
                Pixels[index] = new Color(r, g, b, outA);
            }

            private float NormalizedX(int x)
            {
                return (x + 0.5f) / _size;
            }

            private float NormalizedY(int y)
            {
                return 1f - ((y + 0.5f) / _size);
            }

            private static float Smooth(float value)
            {
                return Mathf.Clamp01(value + 0.5f);
            }

            private float Distance(int x, int y, float cx, float cy)
            {
                float dx = NormalizedX(x) - cx;
                float dy = NormalizedY(y) - cy;
                return Mathf.Sqrt(dx * dx + dy * dy);
            }

            private static float DistanceToSegment(float px, float py, float x0, float y0, float x1, float y1)
            {
                float vx = x1 - x0;
                float vy = y1 - y0;
                float wx = px - x0;
                float wy = py - y0;
                float len = vx * vx + vy * vy;
                float t = len > 0f ? Mathf.Clamp01((wx * vx + wy * vy) / len) : 0f;
                float dx = px - (x0 + vx * t);
                float dy = py - (y0 + vy * t);
                return Mathf.Sqrt(dx * dx + dy * dy);
            }

            private static float Edge(float x0, float y0, float x1, float y1, float px, float py)
            {
                return (px - x0) * (y1 - y0) - (py - y0) * (x1 - x0);
            }
        }
    }
}
