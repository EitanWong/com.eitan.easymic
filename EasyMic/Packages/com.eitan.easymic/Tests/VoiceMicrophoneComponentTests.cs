#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime.Mono;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS.Internal;
using Eitan.SherpaONNXUnity.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Tests
{
    public class LocalTtsPlaybackTests
    {
        [Test]
        public void SpeechOutputLeveler_BalancesQuietAndLoudModelsWithoutClipping()
        {
            const int sampleCount = 960;
            var quiet = new float[sampleCount];
            var loud = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float wave = Mathf.Sin(i * 0.17f);
                quiet[i] = wave * 0.02f;
                loud[i] = wave * 0.9f;
            }

            var quietLeveler = new SpeechOutputLeveler();
            quietLeveler.Process(quiet, quiet.Length, 1, 48000, normalize: true, volume: 1f);
            var loudLeveler = new SpeechOutputLeveler();
            loudLeveler.Process(loud, loud.Length, 1, 48000, normalize: true, volume: 1f);

            Assert.That(Rms(quiet), Is.GreaterThan(0.06f));
            Assert.That(Rms(loud), Is.LessThan(0.25f));
            Assert.That(MaxAbs(quiet), Is.LessThanOrEqualTo(SpeechOutputLeveler.PeakCeiling + 0.0001f));
            Assert.That(MaxAbs(loud), Is.LessThanOrEqualTo(SpeechOutputLeveler.PeakCeiling + 0.0001f));
            Assert.That(Mathf.Abs(Rms(quiet) - Rms(loud)), Is.LessThan(0.08f));
        }

        [Test]
        public void SpeechOutputLeveler_DoesNotBoostSilenceAndHonorsManualVolume()
        {
            var silence = new float[960];
            var leveler = new SpeechOutputLeveler();
            leveler.Process(silence, silence.Length, 1, 44100, normalize: true, volume: 1f);
            Assert.That(MaxAbs(silence), Is.EqualTo(0f));

            var samples = new float[960];
            for (int i = 0; i < samples.Length; i++) samples[i] = 0.2f;
            leveler = new SpeechOutputLeveler();
            leveler.Process(samples, samples.Length, 1, 44100, normalize: false, volume: 0.5f);
            Assert.That(Rms(samples), Is.EqualTo(0.1f).Within(0.01f));
        }

        private static float Rms(float[] samples)
        {
            double power = 0;
            for (int i = 0; i < samples.Length; i++) power += samples[i] * samples[i];
            return (float)Math.Sqrt(power / samples.Length);
        }

        [TestCase(16000)]
        [TestCase(44100)]
        [TestCase(48000)]
        public void SpeechOutputLeveler_ProtectsSuddenPeaksAndLinksStereo(int rate)
        {
            var leveler = new SpeechOutputLeveler();
            var data = new float[(rate / 50) * 2];
            for (int block = 0; block < 4; block++)
            {
                for (int i = 0; i < data.Length; i += 2)
                {
                    data[i] = block == 0 ? 0.01f : 3f;
                    data[i + 1] = data[i] * 0.5f;
                }
                leveler.Process(data, data.Length, 2, rate, true, 2f);
                Assert.LessOrEqual(MaxAbs(data), SpeechOutputLeveler.PeakCeiling + 0.0001f);
                for (int i = 0; i < data.Length; i += 2)
                    Assert.AreEqual(data[i] * 0.5f, data[i + 1], 0.0001f);
            }
            leveler.Process(data, data.Length, 2, rate, true, 0f);
            Assert.AreEqual(0f, MaxAbs(data));
        }

        [Test]
        public void SpeechOutputLeveler_DoesNotAmplifyNoiseAfterQuietSpeech()
        {
            var leveler = new SpeechOutputLeveler();
            var data = new float[960];
            for (int i = 0; i < data.Length; i++) data[i] = 0.01f;
            leveler.Process(data, data.Length, 1, 48000, true, 1f);
            for (int i = 0; i < data.Length; i++) data[i] = 0.0001f;
            leveler.Process(data, data.Length, 1, 48000, true, 1f);
            Assert.LessOrEqual(MaxAbs(data), 0.0001f);
            data[0] = float.NaN;
            data[1] = float.PositiveInfinity;
            leveler.Process(data, data.Length, 1, 48000, true, 1f);
            Assert.IsFalse(float.IsNaN(data[0]));
            Assert.IsFalse(float.IsInfinity(data[1]));
        }

        private static float MaxAbs(float[] samples)
        {
            float peak = 0;
            for (int i = 0; i < samples.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            return peak;
        }

        [Test]
        public async Task CompletedGenerationDoesNotHoldTheSessionForFourSeconds()
        {
            var owner = new GameObject("Local TTS session completion test");
            owner.SetActive(false);
            try
            {
                var synth = owner.AddComponent<Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS.SpeechSynthesizer>();
                var method = synth.GetType().GetMethod("RunPlaybackWorkerAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                var task = (Task)method.Invoke(synth, new object[]
                {
                    1L, new ConcurrentDictionary<int, SpeechSynthesisResult>(),
                    (Func<bool>)(() => true), CancellationToken.None
                });
                var completed = await Task.WhenAny(task, Task.Delay(200));
                Assert.AreSame(task, completed, "A completed producer cannot add more results; release the session immediately.");
                await task;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }
    }

    public class AdaptiveSynthesisSchedulerTests
    {
        [TestCase(1)]
        [TestCase(2)]
        public void LoadChangesNeverExceedConfiguredConcurrency(int limit)
        {
            var scheduler = new AdaptiveSynthesisScheduler(0.12f, limit);
            foreach (float load in new[] { 0f, 0.1f, 1f, -1f, 0f })
            {
                for (int i = 0; i < 20; i++)
                {
                    scheduler.UpdateLoad(load);
                    Assert.That(scheduler.MaxParallel, Is.InRange(1, limit));
                }
            }
        }

        [Test]
        public void HighLoadReducesConcurrencyAndIncreasesBuffer()
        {
            var scheduler = new AdaptiveSynthesisScheduler(0.12f, 2);
            scheduler.UpdateLoad(1f);
            Assert.AreEqual(1, scheduler.MaxParallel);
            Assert.Greater(scheduler.TargetBufferedSeconds, 0.12f);
        }
    }

    public class RecognitionBufferTests
    {
        [Test]
        public void DeltaExtractionHandlesSamePrefixShrinkAndDiverge()
        {
            var buffer = new RecognitionBuffer(
                null,
                _ => { });

            string deltaSame = buffer.DebugExtractDelta("hello");
            Assert.AreEqual("hello", deltaSame);

            string deltaNoChange = buffer.DebugExtractDelta("hello");
            Assert.AreEqual(string.Empty, deltaNoChange);

            string deltaPrefix = buffer.DebugExtractDelta("hello world");
            Assert.AreEqual(" world", deltaPrefix);

            string deltaShrink = buffer.DebugExtractDelta("hel");
            Assert.AreEqual(string.Empty, deltaShrink);

            string deltaDiverge = buffer.DebugExtractDelta("hazel");
            Assert.AreEqual("hazel", deltaDiverge);
        }

        [Test]
        public void AdaptiveTurnDetectorRespectsMaxDelayAndStrongEndingFastPath()
        {
            var settings = new TurnDetectionOptions(0.5f, 2.4f);

            var detector = new AdaptiveTurnDetector(settings);

            // When silence exceeds max delay, detector should immediately allow finalization.
            var maxContext = new TurnDetectionContext(
                silenceSeconds: 3.0f,
                segmentCount: 1,
                characterCount: 5,
                endsWithPunctuation: false,
                endsWithConjunction: false,
                hasOpenParentheses: false,
                hasOpenQuotes: false,
                lastCharacter: 'o');
            Assert.That(detector.EvaluateDelay(in maxContext), Is.EqualTo(0f).Within(0.001f));

            // Strong sentence ending after 60% of min delay should follow the fast-path to "minDelay - silence".
            var strongEndingContext = new TurnDetectionContext(
                silenceSeconds: 0.4f,
                segmentCount: 1,
                characterCount: 12,
                endsWithPunctuation: true,
                endsWithConjunction: false,
                hasOpenParentheses: false,
                hasOpenQuotes: false,
                lastCharacter: '.');
            Assert.That(detector.EvaluateDelay(in strongEndingContext), Is.EqualTo(0.1f).Within(0.05f));
        }
    }

    public class VoiceActivityMonitorTests
    {
        [Test]
        public void VoiceActivityTransitionsTriggerEvents()
        {
            var monitor = new VoiceActivityMonitor();
            bool changed = false;
            monitor.VoiceActivityChanged += active => changed = active;

            monitor.SetVoiceActivity(true);
            Assert.IsTrue(monitor.IsVoiceActive);
            Assert.IsTrue(changed);

            monitor.SetVoiceActivity(false);
            Assert.IsFalse(monitor.IsVoiceActive);
        }

        [Test]
        public void ResetClearsVoiceActivity()
        {
            var monitor = new VoiceActivityMonitor();
            monitor.SetVoiceActivity(true);
            monitor.Reset();
            Assert.IsFalse(monitor.IsVoiceActive);
        }
    }

    public class ModelProgressAggregatorTests
    {
        [Test]
        public void CalculatesAverageAcrossModels()
        {
            var aggregator = new ModelProgressAggregator();
            aggregator.Reset(2);

            var metadataA = new SherpaONNXModelMetadata { modelId = "modelA" };
            var metadataB = new SherpaONNXModelMetadata { modelId = "modelB" };

            aggregator.RegisterPrepare(metadataA, "prepare");
            aggregator.RegisterDownload(metadataA, 50f, "download");
            aggregator.RegisterSuccess(metadataA, "success");

            aggregator.RegisterPrepare(metadataB, "prepare");
            aggregator.RegisterDownload(metadataB, 100f, "download");
            aggregator.RegisterSuccess(metadataB, "success");

            Assert.That(aggregator.CalculateProgress(), Is.EqualTo(1f));
        }
    }

    public class KeywordGateTests
    {
        [TearDown]
        public void StopNativePlayback()
        {
            Eitan.EasyMic.Runtime.AudioSystem.Instance.Stop();
        }

        [Test]
        public void ContinuousConversationTimeoutClosesGate()
        {
            var settings = new KeywordOptions
            {
                Enabled = true,
                ModelId = "model",
                ContinuousConversation = true,
                ContinuousConversationTimeoutSeconds = 0.5f
            };

            bool wasDeactivated = false;
            var gate = new KeywordGate(settings, 0.5f, 0.5f, _ => { });
            gate.ActivityChanged += (_, active) => wasDeactivated = wasDeactivated || !active;

            gate.Activate("wake");
            gate.Update(0.3f, false, false);
            Assert.IsFalse(wasDeactivated);

            gate.Update(0.3f, false, false);
            Assert.IsTrue(wasDeactivated);
        }

        [Test]
        public void TriggerSoundExtendsSilenceHold()
        {
            var settings = new KeywordOptions
            {
                Enabled = true,
                ModelId = "model",
                UseTriggerSound = true,
                TriggerSoundClip = AudioClip.Create("trigger", 4410, 1, 44100, false)
            };

            var holds = new List<float>();
            var gate = new KeywordGate(settings, 0.5f, 0.3f, holds.Add);

            gate.Activate("wake");

            Assert.That(holds.Count, Is.EqualTo(2));
            Assert.That(holds[0], Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(holds[1], Is.EqualTo(settings.TriggerSoundClip.length * 2f).Within(0.001f));
        }
    }
}
#endif
