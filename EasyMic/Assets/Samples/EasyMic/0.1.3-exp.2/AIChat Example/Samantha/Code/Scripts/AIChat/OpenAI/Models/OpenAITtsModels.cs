using System;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// TTS 请求
    /// </summary>
    [Serializable]
    internal sealed class OpenAITtsRequest
    {
        [SerializeField] private string model = "tts-1";
        [SerializeField] private string input;
        [SerializeField] private string voice = "alloy";
        [SerializeField] private string response_format = "mp3";
        [SerializeField] private int sample_rate = 44100;
        [SerializeField] private float speed = 1f;
        [SerializeField] private float gain = 0f;

        public bool stream = false;

        /// <summary>
        /// 模型名称
        /// OpenAI: tts-1, tts-1-hd
        /// SiliconFlow: FunAudioLLM/CosyVoice2-0.5B, fnlp/MOSS-TTSD-v0.5
        /// </summary>
        public string Model
        {
            get => model;
            set => model = value;
        }

        /// <summary>
        /// 要转换的文本
        /// 对于 CosyVoice2 支持情感控制：使用 &lt;|endofprompt|&gt; 分隔指令和内容
        /// </summary>
        public string Input
        {
            get => input;
            set => input = value;
        }

        /// <summary>
        /// 音色
        /// OpenAI: alloy, echo, fable, onyx, nova, shimmer
        /// SiliconFlow CosyVoice2: FunAudioLLM/CosyVoice2-0.5B:alex 等
        /// </summary>
        public string Voice
        {
            get => voice;
            set => voice = value;
        }

        /// <summary>
        /// 输出格式：mp3, opus, wav, pcm
        /// </summary>
        public string ResponseFormat
        {
            get => response_format;
            set => response_format = value;
        }

        /// <summary>
        /// 采样率
        /// opus: 48000
        /// wav, pcm: 8000, 16000, 24000, 32000, 44100
        /// mp3: 32000, 44100
        /// </summary>
        public int SampleRate
        {
            get => sample_rate;
            set => sample_rate = value;
        }

        /// <summary>
        /// 语速 (0.25 - 4.0)
        /// </summary>
        public float Speed
        {
            get => speed;
            set => speed = Mathf.Clamp(value, 0.25f, 4f);
        }

        /// <summary>
        /// 音量增益 (-10 到 10 dB)，仅 SiliconFlow 支持
        /// </summary>
        public float Gain
        {
            get => gain;
            set => gain = Mathf.Clamp(value, -10f, 10f);
        }
    }
}
