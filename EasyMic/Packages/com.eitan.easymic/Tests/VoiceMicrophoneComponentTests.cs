#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System.Collections.Generic;
using Eitan.EasyMic.Runtime.Mono;
using Eitan.EasyMic.Runtime.SherpaOnnxUnity;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Tests
{
    public class TextAccumulatorTests
    {
        [Test]
        public void DeltaExtractionHandlesSamePrefixShrinkAndDiverge()
        {
            var accumulator = new TextAccumulator(null, _ => { });

            string deltaSame = accumulator.DebugExtractDelta("hello");
            Assert.AreEqual("hello", deltaSame);

            string deltaNoChange = accumulator.DebugExtractDelta("hello");
            Assert.AreEqual(string.Empty, deltaNoChange);

            string deltaPrefix = accumulator.DebugExtractDelta("hello world");
            Assert.AreEqual(" world", deltaPrefix);

            string deltaShrink = accumulator.DebugExtractDelta("hel");
            Assert.AreEqual(string.Empty, deltaShrink);

            string deltaDiverge = accumulator.DebugExtractDelta("hazel");
            Assert.AreEqual("hazel", deltaDiverge);
        }
    }

    public class SpeechStateMachineTests
    {
        [Test]
        public void ExitsSpeakingAfterSilenceThreshold()
        {
            var stateMachine = new SpeechStateMachine(0.16f, 1.5f, 0.3f, 0.1f);
            bool utteranceEnded = false;
            stateMachine.UtteranceEnded += () => utteranceEnded = true;

            stateMachine.SetVoiceActivity(true);
            stateMachine.Update(0.1f);
            stateMachine.Update(0.1f);

            Assert.IsTrue(stateMachine.IsSpeaking);

            stateMachine.SetVoiceActivity(false);
            stateMachine.Update(0.1f);
            Assert.IsFalse(utteranceEnded);

            stateMachine.Update(0.1f);
            Assert.IsTrue(utteranceEnded);
            Assert.IsFalse(stateMachine.IsSpeaking);
        }

        [Test]
        public void SilenceHoldDelaysUtteranceEnd()
        {
            var stateMachine = new SpeechStateMachine(0.16f, 1.5f, 0.3f, 0.1f);
            int utteranceCount = 0;
            stateMachine.UtteranceEnded += () => utteranceCount++;

            stateMachine.SetVoiceActivity(true);
            stateMachine.Update(0.2f);
            stateMachine.SetVoiceActivity(false);
            stateMachine.ExtendSilenceHold(1.0f);

            stateMachine.Update(0.5f);
            Assert.AreEqual(0, utteranceCount);

            stateMachine.Update(0.6f);
            Assert.AreEqual(1, utteranceCount);
        }
    }

    public class ModelProgressAggregatorTests
    {
        [Test]
        public void CalculatesAverageAcrossModels()
        {
            var aggregator = new ModelProgressAggregator();
            aggregator.Reset(2);

            var metadataA = new SherpaOnnxModelMetadata { modelId = "modelA" };
            var metadataB = new SherpaOnnxModelMetadata { modelId = "modelB" };

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
