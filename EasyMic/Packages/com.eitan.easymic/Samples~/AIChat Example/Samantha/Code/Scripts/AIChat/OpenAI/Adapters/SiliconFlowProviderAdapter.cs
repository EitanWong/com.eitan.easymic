using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed class SiliconFlowProviderAdapter : OpenAIProviderAdapterBase
    {
        private static readonly HashSet<string> ThinkingModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "zai-org/GLM-4.6",
            "Qwen/Qwen3-8B",
            "Qwen/Qwen3-14B",
            "Qwen/Qwen3-32B",
            "wen/Qwen3-30B-A3B",
            "Qwen/Qwen3-30B-A3B",
            "Qwen/Qwen3-235B-A22B",
            "tencent/Hunyuan-A13B-Instruct",
            "zai-org/GLM-4.5V",
            "deepseek-ai/DeepSeek-V3.1-Terminus",
            "Pro/deepseek-ai/DeepSeek-V3.1-Terminus"
        };

        public override string Name => "SiliconFlow";

        public override string BuildChatCompletionsPayload(OpenAIChatRequest request)
        {
            if (request == null)
            {
                return "{}";
            }

            string model = request.Model?.Trim() ?? string.Empty;
            if (!ThinkingModels.Contains(model))
            {
                return base.BuildChatCompletionsPayload(request);
            }

            bool enableThinking = request.EnableThinkingOverride ?? false;
            var siliconRequest = new OpenAISiliconFlowChatRequest(request, enableThinking);
            return JsonUtility.ToJson(siliconRequest);
        }
    }
}
