using System;
using System.IO;
using System.Text;
using Eitan.EasyMic.Runtime.Mono.Recording;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class StreamingWavWriterContractTests
    {
        private string _directory;

        [SetUp]
        public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "EasyMicTests", Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void FinalizeWithoutSamples_IsIdempotentAndDoesNotCreateFile()
        {
            string path = Path.Combine(_directory, "empty.wav");
            using var writer = new StreamingWavWriter(path);
            Assert.That(writer.FinalizeRecording(), Is.Null);
            Assert.That(writer.FinalizeRecording(), Is.Null);
            writer.OnSamples(new[] { 1f }, 48000, 1);
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void WritesNestedPath_ClampsSamples_AndPatchesClassicRiffHeader()
        {
            string path = Path.Combine(_directory, "nested", "capture.wav");
            using (var writer = new StreamingWavWriter(path))
            {
                writer.OnSamples(Array.Empty<float>(), 48000, 2);
                writer.OnSamples(new[] { -2f, -1f, 0f, 1f, 2f }, 48000, 2);
                Assert.That(writer.FinalizeRecording(), Is.EqualTo(path));
                Assert.That(writer.FinalizeRecording(), Is.EqualTo(path));
            }

            byte[] bytes = File.ReadAllBytes(path);
            Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("RIFF"));
            Assert.That(Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("WAVE"));
            Assert.That(Encoding.ASCII.GetString(bytes, 12, 4), Is.EqualTo("ds64"));
            Assert.That(BitConverter.ToUInt16(bytes, 58), Is.EqualTo((ushort)2));
            Assert.That(BitConverter.ToInt32(bytes, 60), Is.EqualTo(48000));
            Assert.That(Encoding.ASCII.GetString(bytes, 72, 4), Is.EqualTo("data"));
            Assert.That(BitConverter.ToUInt32(bytes, 76), Is.EqualTo(10u));
            Assert.That(bytes.Length, Is.EqualTo(80 + 10));
            Assert.That(BitConverter.ToInt16(bytes, 80), Is.EqualTo(-32767));
            Assert.That(BitConverter.ToInt16(bytes, 82), Is.EqualTo(-32767));
            Assert.That(BitConverter.ToInt16(bytes, 84), Is.Zero);
            Assert.That(BitConverter.ToInt16(bytes, 86), Is.EqualTo(short.MaxValue));
            Assert.That(BitConverter.ToInt16(bytes, 88), Is.EqualTo(short.MaxValue));
        }

        [Test]
        public void DisposeFinalizesHeader_AndPostFinalizeWritesAreIgnored()
        {
            string path = Path.Combine(_directory, "disposed.wav");
            var writer = new StreamingWavWriter(path);
            writer.OnSamples(new[] { 0.25f }, 16000, 1);
            writer.Dispose();
            long length = new FileInfo(path).Length;
            writer.OnSamples(new[] { 1f, 1f }, 16000, 1);
            Assert.That(new FileInfo(path).Length, Is.EqualTo(length));
            Assert.That(Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4), Is.EqualTo("RIFF"));
        }

        [Test]
        public void NonfiniteNanSample_IsWrittenAsSilence()
        {
            string path = Path.Combine(_directory, "nan.wav");
            using (var writer = new StreamingWavWriter(path))
            {
                writer.OnSamples(new[] { float.NaN }, 48000, 1);
            }
            Assert.That(BitConverter.ToInt16(File.ReadAllBytes(path), 80), Is.Zero);
        }
    }
}
