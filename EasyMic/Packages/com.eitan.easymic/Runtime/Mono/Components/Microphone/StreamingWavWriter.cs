using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Recording
{
    /// <summary>
    /// Streaming 16-bit PCM writer that keeps audio in a single temporary file.
    /// It automatically upgrades to RF64 when exceeding classic RIFF limits while
    /// still patching compact headers for shorter recordings.
    /// Thread-affinity: methods may be called from the audio thread.
    /// </summary>
    internal sealed class StreamingWavWriter : IAudioSink, IDisposable
    {
        private const uint Placeholder32 = 0xFFFFFFFF;
        private const uint Ds64PayloadSize = 28;

        private readonly string _filePath;

        private FileStream _stream;
        private byte[] _scratch;
        private bool _headerInitialized;
        private bool _finalized;
        private bool _reportedOpenFailure;

        private int _sampleRate;
        private int _channels;
        private long _dataBytesWritten;

        private long _ds64RiffSizeOffset;
        private long _ds64DataSizeOffset;
        private long _ds64SampleCountOffset;
        private long _dataSizeFieldOffset;

        public StreamingWavWriter(string filePath)
        {
            _filePath = filePath;
        }

        public string FilePath => _filePath;

        public void OnSamples(ReadOnlySpan<float> samples, int sampleRate, int channels)
        {
            if (_finalized)
            {
                return;
            }

            EnsureStream(sampleRate, channels);

            if (samples.IsEmpty || _stream == null)
            {
                return;
            }

            int byteCount = samples.Length * sizeof(short);
            if (_scratch == null || _scratch.Length < byteCount)
            {
                _scratch = new byte[byteCount];
            }

            int idx = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                short quantized = (short)Mathf.RoundToInt(clamped * short.MaxValue);
                _scratch[idx++] = (byte)(quantized & 0xFF);
                _scratch[idx++] = (byte)((quantized >> 8) & 0xFF);
            }

            _stream.Write(_scratch, 0, byteCount);
            _dataBytesWritten += byteCount;
        }

        private void EnsureStream(int sampleRate, int channels)
        {
            if (_headerInitialized || _finalized)
            {
                return;
            }

            _sampleRate = Math.Max(8000, sampleRate);
            _channels = Math.Max(1, channels);

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                _stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            }
            catch (IOException ex)
            {
                if (!_reportedOpenFailure)
                {
                    Debug.LogWarning($"StreamingWavWriter: Unable to open '{_filePath}' for writing. {ex.Message}");
                    _reportedOpenFailure = true;
                }
                return;
            }

            WriteInitialHeader(_stream, _sampleRate, _channels);
            _headerInitialized = true;
        }

        private void WriteInitialHeader(FileStream fs, int sampleRate, int channels)
        {
            using var writer = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes("RF64"));
            writer.Write(Placeholder32);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("ds64"));
            writer.Write(Ds64PayloadSize);
            _ds64RiffSizeOffset = fs.Position;
            writer.Write((ulong)0);
            _ds64DataSizeOffset = fs.Position;
            writer.Write((ulong)0);
            _ds64SampleCountOffset = fs.Position;
            writer.Write((ulong)0);
            writer.Write((uint)0); // table length (unused)

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16u);
            writer.Write((ushort)1);
            writer.Write((ushort)channels);
            writer.Write(sampleRate);
            int byteRate = sampleRate * channels * sizeof(short);
            writer.Write(byteRate);
            writer.Write((ushort)(channels * sizeof(short)));
            writer.Write((ushort)16);

            writer.Write(Encoding.ASCII.GetBytes("data"));
            _dataSizeFieldOffset = fs.Position;
            writer.Write(Placeholder32);
        }

        public string FinalizeRecording()
        {
            if (_finalized)
            {
                return _filePath;
            }

            if (_stream == null && !_headerInitialized && _dataBytesWritten == 0)
            {
                _finalized = true;
                return null;
            }

            try
            {
                if (_stream == null)
                {
                    EnsureStream(_sampleRate > 0 ? _sampleRate : 48000, _channels > 0 ? _channels : 1);
                    if (_stream == null)
                    {
                        return null;
                    }
                }

                UpdateHeader();
            }
            finally
            {
                _stream?.Dispose();
                _stream = null;
                _finalized = true;
            }

            return _filePath;
        }

        private void UpdateHeader()
        {
            if (_stream == null)
            {
                return;
            }

            long dataBytes = _dataBytesWritten;
            long totalFrames = _channels > 0 ? dataBytes / (_channels * sizeof(short)) : 0;

            ulong riffSize64 = (ulong)(_stream.Position - 8);
            ulong dataSize64 = (ulong)dataBytes;
            ulong sampleCount64 = (ulong)totalFrames;

            bool requiresRf64 = riffSize64 > uint.MaxValue || dataSize64 > uint.MaxValue;

            using var writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);

            _stream.Seek(_ds64RiffSizeOffset, SeekOrigin.Begin);
            writer.Write(riffSize64);
            _stream.Seek(_ds64DataSizeOffset, SeekOrigin.Begin);
            writer.Write(dataSize64);
            _stream.Seek(_ds64SampleCountOffset, SeekOrigin.Begin);
            writer.Write(sampleCount64);

            _stream.Seek(0, SeekOrigin.Begin);
            writer.Write(Encoding.ASCII.GetBytes(requiresRf64 ? "RF64" : "RIFF"));
            writer.Write(requiresRf64 ? Placeholder32 : (uint)riffSize64);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            _stream.Seek(_dataSizeFieldOffset, SeekOrigin.Begin);
            writer.Write(requiresRf64 ? Placeholder32 : (uint)dataSize64);

            _stream.Flush(true);
        }

        public void Dispose()
        {
            FinalizeRecording();
        }
    }
}
