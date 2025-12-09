#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System.Collections.Generic;
using Eitan.EasyMic.Runtime.Mono;
using Eitan.EasyMic.Runtime.Mono.ASR;
using Eitan.EasyMic.Runtime.SherpaONNXUnity;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Tests
{
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
        public void HeuristicTurnDetectorHonoursPunctuation()
        {
            var settings = new TurnDetectionSettings
            {
                MinDelaySeconds = 0f,
                MaxDelaySeconds = 0.5f
            };

            var detector = new HeuristicTurnDetector(settings);
            var context = new TurnDetectionContext("Hello world.", 1, true);
            Assert.That(detector.EvaluateDelay(in context), Is.EqualTo(0.5f).Within(0.001f));

            var quickContext = new TurnDetectionContext("Hello", 1, false);
            Assert.That(detector.EvaluateDelay(in quickContext), Is.EqualTo(0f).Within(0.001f));
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
        [Test]
        public void ContinuousConversationTimeoutClosesGate()
        {
            var settings = new KeywordSettings
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
            var settings = new KeywordSettings
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
