using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SherpaSpeechSynthesis = Eitan.SherpaONNXUnity.Runtime.Modules.SpeechSynthesis;

namespace Eitan.EasyMic.Runtime.Mono.Components.TTS.Internal
{
    internal sealed class SpeechSynthesisJob
    {
        private int _finalized;
        private readonly string _originalSentence;
        private readonly SherpaSpeechSynthesis _speechSynthesis;
        private readonly SpeechSynthesizerConfiguration _config;
        private readonly Action<string> _onStart;
        private readonly Action<string> _onFinish;
        private readonly Action<string> _logWarning;
        private readonly Action<string> _logError;
        private readonly SpeechSynthesisResult _result;

        public bool IsDone { get; private set; }

        public SpeechSynthesisJob(
            string sentence,
            SherpaSpeechSynthesis speechSynthesis,
            SpeechSynthesizerConfiguration config,
            Action<string> onStart,
            Action<string> onFinish,
            Action<string> logWarning,
            Action<string> logError,
            SpeechSynthesisResult result)
        {
            _originalSentence = sentence;
            _speechSynthesis = speechSynthesis;
            _config = config;
            _onStart = onStart;
            _onFinish = onFinish;
            _logWarning = logWarning;
            _logError = logError;
            _result = result;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            string ttsText = SpeechTextPreprocessor.CleanForTts(_originalSentence);
            _result?.MarkStarted();

            try
            {
                _onStart?.Invoke(ttsText);
            }
            catch (Exception ex)
            {
                _logWarning?.Invoke($"[SpeechSynthesisJob] OnStart callback error: {ex.Message}");
            }

            if (string.IsNullOrEmpty(ttsText))
            {
                FinalizeJob(ttsText);
                return;
            }

            int sampleRate = Math.Max(8000, _config.SampleRates);
            const int channels = 1;
            _result?.ConfigureFormat(channels, sampleRate);

            try
            {
                var ttsRequest = SpeechTextPreprocessor.ApplyPronunciationRules(ttsText);
                cancellationToken.ThrowIfCancellationRequested();

                await _speechSynthesis.GenerateWithProgressCallbackAsync(
                    ttsRequest,
                    _config.VoiceId,
                    _config.Speed,
                    (samplesPtr, count, progress) =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return 0;
                        }

                        if (samplesPtr == IntPtr.Zero || count <= 0)
                        {
                            return 1;
                        }

                        var samples = new float[count];
                        Marshal.Copy(samplesPtr, samples, 0, count);
                        _result?.EnqueueChunk(samples);
                        return 1;
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _result?.MarkFailed(new OperationCanceledException());
            }
            catch (Exception ex)
            {
                _logError?.Invoke($"[SpeechSynthesisJob] Generation exception: {ex}");
                _result?.MarkFailed(ex);
            }
            finally
            {
                FinalizeJob(ttsText);
            }
        }

        private void FinalizeJob(string sentence)
        {
            if (Interlocked.Exchange(ref _finalized, 1) == 0)
            {
                IsDone = true;
                _result?.MarkComplete();
                try
                {
                    _onFinish?.Invoke(sentence);
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke($"[SpeechSynthesisJob] OnFinish callback error: {ex.Message}");
                }
            }
        }
    }
}
