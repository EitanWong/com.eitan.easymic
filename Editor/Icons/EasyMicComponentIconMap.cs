using System;
using System.Collections.Generic;

namespace Eitan.EasyMic.Editor.Icons
{
    internal static class EasyMicComponentIconMap
    {
        public readonly struct Entry
        {
            public readonly string ScriptName;
            public readonly string TypeFullName;
            public readonly EasyMicIconId IconId;

            public Entry(string scriptName, string typeFullName, EasyMicIconId iconId)
            {
                ScriptName = scriptName;
                TypeFullName = typeFullName;
                IconId = iconId;
            }
        }

        private static readonly Entry[] Entries =
        {
            new Entry("EasyMicrophone", "Eitan.EasyMic.Runtime.Mono.EasyMicrophone", EasyMicIconId.Microphone),
            new Entry("PlaybackAudioSourceBehaviour", "Eitan.EasyMic.Runtime.Mono.Components.PlaybackAudioSourceBehaviour", EasyMicIconId.PlaybackSource),
            new Entry("VoiceMicrophone", "Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR.VoiceMicrophone", EasyMicIconId.VoiceMicrophone),
            new Entry("SpeechSynthesizer", "Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS.SpeechSynthesizer", EasyMicIconId.SpeechSynthesis)
        };

        public static IReadOnlyList<Entry> All => Entries;

        public static bool TryGetIconId(Type type, out EasyMicIconId iconId)
        {
            if (type != null)
            {
                string fullName = type.FullName;
                for (int i = 0; i < Entries.Length; i++)
                {
                    if (Entries[i].TypeFullName == fullName)
                    {
                        iconId = Entries[i].IconId;
                        return true;
                    }
                }
            }

            iconId = EasyMicIconId.EasyMic;
            return false;
        }
    }
}
