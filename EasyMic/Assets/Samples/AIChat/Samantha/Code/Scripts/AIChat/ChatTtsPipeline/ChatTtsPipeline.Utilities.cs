using System;
using System.IO;
using System.Text;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed partial class ChatTtsPipeline
    {
        private void ClearQueues()
        {
            while (_pendingJobs.TryDequeue(out _)) { }
            _completedJobs.Clear();
            System.Threading.Interlocked.Exchange(ref _nextSequenceNumber, 0);
            System.Threading.Interlocked.Exchange(ref _nextPlaybackSequence, 0);
        }

        private void NotifySpeakingState(bool speaking)
        {
            if (_isSpeaking == speaking)
            {
                return;
            }

            _isSpeaking = speaking;
            SafeInvoke(() => OnSpeakingStateChanged?.Invoke(speaking));
        }

        private static void SafeInvoke(Action action)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[ParallelTtsPipeline] Callback error: {ex.Message}");
            }
        }

        private static bool TryDecodeAudioPayload(byte[] payload, out float[] samples, out int channels, out int sampleRate)
        {
            samples = Array.Empty<float>();
            channels = RemoteDefaultChannels;
            sampleRate = RemoteDefaultSampleRate;

            if (payload == null || payload.Length < 2)
            {
                return false;
            }

            if (payload.Length >= 44 && payload[0] == 'R' && payload[1] == 'I')
            {
                return TryParseWavFormat(payload, out samples, out channels, out sampleRate);
            }

            return TryParseRawPcm16(payload, out samples, out channels, out sampleRate);
        }

        private static bool TryParseWavFormat(byte[] data, out float[] samples, out int channels, out int sampleRate)
        {
            samples = Array.Empty<float>();
            channels = 1;
            sampleRate = 24000;

            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                string riff = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (riff != "RIFF")
                {
                    return false;
                }

                reader.ReadInt32();

                string wave = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (wave != "WAVE")
                {
                    return false;
                }

                ushort bitsPerSample = 16;
                bool fmtFound = false;

                while (reader.BaseStream.Position < reader.BaseStream.Length - 8)
                {
                    string chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    int chunkSize = reader.ReadInt32();

                    switch (chunkId)
                    {
                        case "fmt ":
                            ushort audioFormat = reader.ReadUInt16();
                            channels = reader.ReadUInt16();
                            sampleRate = reader.ReadInt32();
                            reader.ReadInt32();
                            reader.ReadUInt16();
                            bitsPerSample = reader.ReadUInt16();

                            int remaining = chunkSize - 16;
                            if (remaining > 0)
                            {
                                reader.BaseStream.Seek(remaining, SeekOrigin.Current);
                            }

                            if (audioFormat != 1 && audioFormat != 3)
                            {
                                return false;
                            }

                            fmtFound = true;
                            break;

                        case "data":
                            if (!fmtFound)
                            {
                                return false;
                            }

                            byte[] rawData = reader.ReadBytes(chunkSize);

                            if (bitsPerSample == 16)
                            {
                                int sampleCount = rawData.Length / 2;
                                samples = new float[sampleCount];
                                for (int i = 0; i < sampleCount; i++)
                                {
                                    short sample = BitConverter.ToInt16(rawData, i * 2);
                                    samples[i] = sample / 32768f;
                                }
                            }
                            else if (bitsPerSample == 32)
                            {
                                int sampleCount = rawData.Length / 4;
                                samples = new float[sampleCount];
                                for (int i = 0; i < sampleCount; i++)
                                {
                                    samples[i] = BitConverter.ToSingle(rawData, i * 4);
                                }
                            }
                            else
                            {
                                return false;
                            }

                            channels = Math.Max(1, channels);
                            sampleRate = Math.Max(8000, sampleRate);
                            return true;

                        default:
                            reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
                            if ((chunkSize & 1) == 1)
                            {
                                reader.BaseStream.Seek(1, SeekOrigin.Current);
                            }
                            break;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseRawPcm16(byte[] data, out float[] samples, out int channels, out int sampleRate)
        {
            samples = Array.Empty<float>();
            channels = RemoteDefaultChannels;
            sampleRate = RemoteDefaultSampleRate;

            if (data == null || data.Length < 2)
            {
                return false;
            }

            int sampleCount = data.Length / 2;
            samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(data, i * 2);
                samples[i] = sample / 32768f;
            }

            return true;
        }
    }
}
