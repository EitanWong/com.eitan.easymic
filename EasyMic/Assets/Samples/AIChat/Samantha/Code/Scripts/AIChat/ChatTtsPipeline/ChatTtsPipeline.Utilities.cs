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
            ClearInFlightSentences();
        }

        private bool TryRegisterInFlightSentence(string sentence)
        {
            lock (_inFlightLock)
            {
                if (_inFlightSentences.Contains(sentence))
                {
                    return false;
                }

                _inFlightSentences.Add(sentence);
                return true;
            }
        }

        private void ReleaseInFlightSentence(string sentence)
        {
            if (string.IsNullOrEmpty(sentence))
            {
                return;
            }

            lock (_inFlightLock)
            {
                _inFlightSentences.Remove(sentence);
            }
        }

        private void ClearInFlightSentences()
        {
            lock (_inFlightLock)
            {
                _inFlightSentences.Clear();
            }
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

        private void SafeInvoke(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (_mainThreadDispatcher != null)
            {
                try
                {
                    _mainThreadDispatcher(() =>
                    {
                        try
                        {
                            action();
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogWarning($"[ParallelTtsPipeline] Callback error: {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[ParallelTtsPipeline] Callback dispatch error: {ex.Message}");
                }

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

        private static bool TryDecodeAudioPayload(
            byte[] payload,
            int expectedChannels,
            int expectedSampleRate,
            out float[] samples,
            out int channels,
            out int sampleRate)
        {
            samples = Array.Empty<float>();
            channels = expectedChannels > 0 ? expectedChannels : RemoteDefaultChannels;
            sampleRate = expectedSampleRate > 0 ? expectedSampleRate : RemoteDefaultSampleRate;

            if (payload == null || payload.Length < 2)
            {
                return false;
            }

            if (LooksLikeWav(payload))
            {
                if (TryParseWavFormat(payload, out samples, out channels, out sampleRate))
                {
                    return true;
                }
            }

            return TryParseRawPcm16(payload, channels, sampleRate, out samples, out channels, out sampleRate);
        }

        private static bool TryDecodeAudioPayloadStreaming(
            byte[] payload,
            int expectedChannels,
            int expectedSampleRate,
            ref byte[] remainder,
            out float[] samples,
            out int channels,
            out int sampleRate)
        {
            samples = Array.Empty<float>();
            channels = expectedChannels > 0 ? expectedChannels : RemoteDefaultChannels;
            sampleRate = expectedSampleRate > 0 ? expectedSampleRate : RemoteDefaultSampleRate;

            if (payload == null || payload.Length == 0)
            {
                return false;
            }

            if (LooksLikeWav(payload))
            {
                remainder = Array.Empty<byte>();
                if (TryParseWavFormat(payload, out samples, out channels, out sampleRate))
                {
                    return true;
                }
            }

            if (remainder != null && remainder.Length > 0)
            {
                byte[] combined = new byte[remainder.Length + payload.Length];
                Buffer.BlockCopy(remainder, 0, combined, 0, remainder.Length);
                Buffer.BlockCopy(payload, 0, combined, remainder.Length, payload.Length);
                payload = combined;
                remainder = Array.Empty<byte>();
            }

            if (payload.Length < 2)
            {
                remainder = payload;
                return false;
            }

            int alignedLength = payload.Length - (payload.Length % 2);
            if (alignedLength <= 0)
            {
                remainder = payload;
                return false;
            }

            if (alignedLength != payload.Length)
            {
                int remainderLength = payload.Length - alignedLength;
                remainder = new byte[remainderLength];
                Buffer.BlockCopy(payload, alignedLength, remainder, 0, remainderLength);

                byte[] aligned = new byte[alignedLength];
                Buffer.BlockCopy(payload, 0, aligned, 0, alignedLength);
                payload = aligned;
            }
            else
            {
                remainder = Array.Empty<byte>();
            }

            return TryParseRawPcm16(payload, channels, sampleRate, out samples, out channels, out sampleRate);
        }

        private static bool LooksLikeWav(byte[] payload)
        {
            if (payload == null || payload.Length < 12)
            {
                return false;
            }

            return payload[0] == 'R' &&
                   payload[1] == 'I' &&
                   payload[2] == 'F' &&
                   payload[3] == 'F' &&
                   payload[8] == 'W' &&
                   payload[9] == 'A' &&
                   payload[10] == 'V' &&
                   payload[11] == 'E';
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

        private static bool TryParseRawPcm16(
            byte[] data,
            int expectedChannels,
            int expectedSampleRate,
            out float[] samples,
            out int channels,
            out int sampleRate)
        {
            samples = Array.Empty<float>();
            channels = expectedChannels > 0 ? expectedChannels : RemoteDefaultChannels;
            sampleRate = expectedSampleRate > 0 ? expectedSampleRate : RemoteDefaultSampleRate;

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
