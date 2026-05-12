#if UNITY_EDITOR
using Eitan.EasyMic.Runtime;
using UnityEngine;

namespace Eitan.EasyMic.Editor
{
    internal static class EasyMicPipelineStyles
    {
        public static readonly Color CanvasBackground = new Color(0.105f, 0.11f, 0.12f);
        public static readonly Color PanelBackground = new Color(0.13f, 0.135f, 0.145f);
        public static readonly Color NodeBase = new Color(0.16f, 0.17f, 0.18f);
        public static readonly Color PrimaryText = new Color(0.86f, 0.88f, 0.90f);
        public static readonly Color SecondaryText = new Color(0.58f, 0.61f, 0.64f);
        public static readonly Color Separator = new Color(0.21f, 0.22f, 0.24f);
        public static readonly Color Edge = new Color(0.42f, 0.50f, 0.56f);
        public static readonly Color Boundary = new Color(0.88f, 0.58f, 0.30f);

        public static Color NodeBackground(EasyMicPipelineNodeKind kind)
        {
            switch (kind)
            {
                case EasyMicPipelineNodeKind.CaptureDevice:
                case EasyMicPipelineNodeKind.PlaybackDevice:
                    return new Color(0.18f, 0.20f, 0.205f);
                case EasyMicPipelineNodeKind.Queue:
                    return new Color(0.17f, 0.16f, 0.145f);
                case EasyMicPipelineNodeKind.Mixer:
                    return new Color(0.145f, 0.17f, 0.17f);
                case EasyMicPipelineNodeKind.Group:
                    return new Color(0.105f, 0.11f, 0.12f);
                case EasyMicPipelineNodeKind.Reader:
                case EasyMicPipelineNodeKind.Writer:
                case EasyMicPipelineNodeKind.Processor:
                    return new Color(0.155f, 0.16f, 0.18f);
                default:
                    return NodeBase;
            }
        }

        public static Color NodeBorder(EasyMicPipelineThreadKind thread)
        {
            var color = ThreadColor(thread);
            color.a = 0.72f;
            return color;
        }

        public static Color ThreadColor(EasyMicPipelineThreadKind thread)
        {
            switch (thread)
            {
                case EasyMicPipelineThreadKind.NativeThread:
                    return new Color(0.95f, 0.62f, 0.36f);
                case EasyMicPipelineThreadKind.AudioThread:
                    return new Color(0.42f, 0.72f, 0.95f);
                case EasyMicPipelineThreadKind.WorkerThread:
                    return new Color(0.44f, 0.82f, 0.68f);
                case EasyMicPipelineThreadKind.MainThread:
                    return new Color(0.76f, 0.68f, 0.94f);
                case EasyMicPipelineThreadKind.TelemetryThread:
                    return new Color(0.88f, 0.78f, 0.42f);
                default:
                    return new Color(0.54f, 0.56f, 0.58f);
            }
        }

        public static Color Activity(float value)
        {
            value = Mathf.Clamp01(value);
            return Color.Lerp(new Color(0.24f, 0.25f, 0.26f), new Color(0.48f, 0.82f, 0.64f), value);
        }

        public static Color Accent(EasyMicPipelineNodeKind kind)
        {
            switch (kind)
            {
                case EasyMicPipelineNodeKind.CaptureDevice:
                    return new Color(0.44f, 0.82f, 0.68f);
                case EasyMicPipelineNodeKind.PlaybackDevice:
                case EasyMicPipelineNodeKind.PlaybackSource:
                case EasyMicPipelineNodeKind.Mixer:
                    return new Color(0.42f, 0.72f, 0.95f);
                case EasyMicPipelineNodeKind.Queue:
                case EasyMicPipelineNodeKind.Transport:
                    return new Color(0.88f, 0.58f, 0.30f);
                case EasyMicPipelineNodeKind.Reader:
                case EasyMicPipelineNodeKind.Writer:
                case EasyMicPipelineNodeKind.Processor:
                    return new Color(0.76f, 0.68f, 0.94f);
                case EasyMicPipelineNodeKind.Group:
                case EasyMicPipelineNodeKind.Output:
                    return new Color(0.88f, 0.78f, 0.42f);
                default:
                    return SecondaryText;
            }
        }
    }

    internal static class EasyMicPipelineFormatting
    {
        public static string ThreadLabel(EasyMicPipelineThreadKind thread)
        {
            switch (thread)
            {
                case EasyMicPipelineThreadKind.NativeThread:
                    return EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ThreadNative);
                case EasyMicPipelineThreadKind.AudioThread:
                    return EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ThreadAudio);
                case EasyMicPipelineThreadKind.WorkerThread:
                    return EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ThreadWorker);
                case EasyMicPipelineThreadKind.MainThread:
                    return EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ThreadMain);
                case EasyMicPipelineThreadKind.TelemetryThread:
                    return EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ThreadTelemetry);
                default:
                    return EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ThreadUnknown);
            }
        }
    }
}
#endif
