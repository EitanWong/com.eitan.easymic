using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    #region Chat Completion API 模型

    /// <summary>
    /// Chat Completions API 请求
    /// </summary>
    [Serializable]
    internal sealed class OpenAIChatRequest
    {
        [SerializeField] private string model;
        [SerializeField] private bool stream = true;
        [SerializeField] private float temperature = 0.7f;
        [SerializeField] private int max_tokens = 2048;
        [SerializeField] private List<OpenAIChatMessage> messages = new List<OpenAIChatMessage>();

        public string Model
        {
            get => model;
            set => model = value;
        }

        public bool Stream
        {
            get => stream;
            set => stream = value;
        }

        public float Temperature
        {
            get => temperature;
            set => temperature = value;
        }

        public int MaxTokens
        {
            get => max_tokens;
            set => max_tokens = value;
        }

        public List<OpenAIChatMessage> Messages
        {
            get => messages;
            set => messages = value ?? new List<OpenAIChatMessage>();
        }
    }

    /// <summary>
    /// 聊天消息
    /// </summary>
    [Serializable]
    internal sealed class OpenAIChatMessage
    {
        [SerializeField] private string role;
        [SerializeField] private string content;

        public OpenAIChatMessage() { }

        public OpenAIChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }

        public string Role
        {
            get => role;
            set => role = value;
        }

        public string Content
        {
            get => content;
            set => content = value;
        }

        // 便捷工厂方法
        public static OpenAIChatMessage System(string content) => new OpenAIChatMessage("system", content);
        public static OpenAIChatMessage User(string content) => new OpenAIChatMessage("user", content);
        public static OpenAIChatMessage Assistant(string content) => new OpenAIChatMessage("assistant", content);
    }

    /// <summary>
    /// Chat Completions API 响应
    /// </summary>
    [Serializable]
    internal sealed class OpenAIChatCompletionResponse
    {
        public string id;
        public List<OpenAIChatCompletionChoice> choices;
        public OpenAIUsage usage;
        public long created;
        public string model;
        [SerializeField] private string @object;
        public string Object => @object;
    }

    [Serializable]
    internal sealed class OpenAIChatCompletionChoice
    {
        public OpenAIChatMessageResponse message;
        public string finish_reason;
        public int index;
    }

    [Serializable]
    internal sealed class OpenAIChatMessageResponse
    {
        public string role;
        public string content;
        public string reasoning_content; // 用于推理模型 (QwQ, o1, DeepSeek-R1 等)
    }

    /// <summary>
    /// Chat Completions 流式响应块
    /// </summary>
    [Serializable]
    internal sealed class OpenAIChatCompletionChunk
    {
        public string id;
        public List<OpenAIChatCompletionStreamChoice> choices;
        public long created;
        public string model;
    }

    [Serializable]
    internal sealed class OpenAIChatCompletionStreamChoice
    {
        public OpenAIChatDelta delta;
        public string finish_reason;
        public int index;
    }

    [Serializable]
    internal sealed class OpenAIChatDelta
    {
        public string role;
        public string content;
        public string reasoning_content;
    }

    #endregion

    #region Responses API 模型

    /// <summary>
    /// Responses API 请求
    /// </summary>
    [Serializable]
    internal sealed class OpenAIResponseRequest
    {
        [SerializeField] private string model;
        [SerializeField] private bool stream = true;
        [SerializeField] private float temperature = 1f;
        [SerializeField] private string instructions;
        [SerializeField] private OpenAIResponseInputItem[] input;
        [SerializeField] private int max_output_tokens;

        public string Model
        {
            get => model;
            set => model = value;
        }

        public bool Stream
        {
            get => stream;
            set => stream = value;
        }

        public float Temperature
        {
            get => temperature;
            set => temperature = value;
        }

        public string Instructions
        {
            get => instructions;
            set => instructions = value;
        }

        public OpenAIResponseInputItem[] Input
        {
            get => input;
            set => input = value;
        }

        public int MaxOutputTokens
        {
            get => max_output_tokens;
            set => max_output_tokens = value;
        }

        /// <summary>
        /// 从 ChatRequest 转换为 ResponseRequest
        /// </summary>
        public static OpenAIResponseRequest FromChatRequest(OpenAIChatRequest chatRequest)
        {
            if (chatRequest == null) return null;

            string systemInstruction = null;
            var inputItems = new List<OpenAIResponseInputItem>();

            foreach (var msg in chatRequest.Messages)
            {
                if (msg == null) continue;

                string role = string.IsNullOrWhiteSpace(msg.Role) ? "user" : msg.Role.Trim().ToLower();
                string content = msg.Content ?? string.Empty;

                // system 消息转为 instructions
                if (role == "system" || role == "developer")
                {
                    systemInstruction = content;
                    continue;
                }

                inputItems.Add(new OpenAIResponseInputItem
                {
                    type = "message",
                    role = role,
                    content = new[]
                    {
                        new OpenAIResponseInputContent
                        {
                            type = "input_text",
                            text = content
                        }
                    }
                });
            }

            return new OpenAIResponseRequest
            {
                Model = chatRequest.Model,
                Stream = chatRequest.Stream,
                Temperature = chatRequest.Temperature,
                Instructions = systemInstruction,
                Input = inputItems.ToArray(),
                MaxOutputTokens = chatRequest.MaxTokens > 0 ? chatRequest.MaxTokens : 0
            };
        }
    }

    [Serializable]
    internal sealed class OpenAIResponseInputItem
    {
        public string type = "message";
        public string role;
        public OpenAIResponseInputContent[] content;
    }

    [Serializable]
    internal sealed class OpenAIResponseInputContent
    {
        public string type = "input_text";
        public string text;
    }

    /// <summary>
    /// Responses API 完整响应对象
    /// </summary>
    [Serializable]
    internal sealed class OpenAIResponseObject
    {
        public string id;
        [SerializeField] private string @object;
        public string Object => @object;
        public long created_at;
        public string status;
        public string model;
        public OpenAIResponseOutputItem[] output;
        public OpenAIResponseError error;
        public OpenAIUsage usage;
        public string instructions;
    }

    /// <summary>
    /// Responses API 流式事件
    /// </summary>
    [Serializable]
    internal sealed class OpenAIResponseStreamEvent
    {
        public string type;
        public OpenAIResponseObject response;
        public string delta;
        public OpenAIResponseError error;

        // 用于某些事件类型
        public string item_id;
        public int output_index;
        public int content_index;
    }

    [Serializable]
    internal sealed class OpenAIResponseOutputItem
    {
        public string id;
        public string type;
        public string status;
        public string role;
        public OpenAIResponseOutputContent[] content;
    }

    [Serializable]
    internal sealed class OpenAIResponseOutputContent
    {
        public string type;
        public string text;
        public string[] annotations;
    }

    [Serializable]
    internal sealed class OpenAIResponseError
    {
        public string message;
        public string code;
        public string type;
    }

    #endregion

    #region TTS API 模型

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

    #endregion

    #region 通用模型

    /// <summary>
    /// Token 使用统计
    /// </summary>
    [Serializable]
    internal sealed class OpenAIUsage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
        public int input_tokens;   // Responses API 使用
        public int output_tokens;  // Responses API 使用
    }

    /// <summary>
    /// 聊天结果
    /// </summary>
    internal sealed class OpenAIChatResult
    {
        public bool Success { get; set; }
        public string Content { get; set; }
        public string ReasoningContent { get; set; }
        public string ErrorMessage { get; set; }
        public bool FallbackRequired { get; set; }
    }

    #endregion

    #region 常量定义

    /// <summary>
    /// 常用模型名称
    /// </summary>
    internal static class OpenAIModels
    {
        // OpenAI Chat 模型
        public const string GPT4o = "gpt-4o";
        public const string GPT4o_Mini = "gpt-4o-mini";
        public const string GPT4_1 = "gpt-4.1";
        public const string GPT4_1_Mini = "gpt-4.1-mini";
        public const string GPT4_1_Nano = "gpt-4.1-nano";
        public const string O3 = "o3";
        public const string O3_Mini = "o3-mini";
        public const string O1 = "o1";
        public const string O1_Mini = "o1-mini";

        // OpenAI TTS 模型
        public const string TTS1 = "tts-1";
        public const string TTS1_HD = "tts-1-hd";

        // SiliconFlow Chat 模型
        public const string Qwen_QwQ_32B = "Qwen/QwQ-32B";
        public const string Qwen2_5_72B = "Qwen/Qwen2.5-72B-Instruct";
        public const string Qwen2_5_32B = "Qwen/Qwen2.5-32B-Instruct";
        public const string Qwen2_5_14B = "Qwen/Qwen2.5-14B-Instruct";
        public const string Qwen2_5_7B = "Qwen/Qwen2.5-7B-Instruct";
        public const string DeepSeek_V3 = "deepseek-ai/DeepSeek-V3";
        public const string DeepSeek_R1 = "deepseek-ai/DeepSeek-R1";

        // SiliconFlow TTS 模型
        public const string CosyVoice2 = "FunAudioLLM/CosyVoice2-0.5B";
        public const string MOSS_TTS = "fnlp/MOSS-TTSD-v0.5";
    }

    /// <summary>
    /// 常用音色
    /// </summary>
    internal static class OpenAIVoices
    {
        // OpenAI 音色
        public const string Alloy = "alloy";
        public const string Echo = "echo";
        public const string Fable = "fable";
        public const string Onyx = "onyx";
        public const string Nova = "nova";
        public const string Shimmer = "shimmer";

        // SiliconFlow CosyVoice2 音色
        public const string Alex = "FunAudioLLM/CosyVoice2-0.5B:alex";         // 沉稳男声
        public const string Benjamin = "FunAudioLLM/CosyVoice2-0.5B:benjamin"; // 低沉男声
        public const string Charles = "FunAudioLLM/CosyVoice2-0.5B:charles";   // 磁性男声
        public const string David = "FunAudioLLM/CosyVoice2-0.5B:david";       // 欢快男声
        public const string Anna = "FunAudioLLM/CosyVoice2-0.5B:anna";         // 沉稳女声
        public const string Bella = "FunAudioLLM/CosyVoice2-0.5B:bella";       // 激情女声
        public const string Claire = "FunAudioLLM/CosyVoice2-0.5B:claire";     // 温柔女声
        public const string Diana = "FunAudioLLM/CosyVoice2-0.5B:diana";       // 欢快女声
    }

    #endregion
}
