using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed class SiliconFlowProviderAdapter : OpenAIProviderAdapterBase
    {
        private static readonly HashSet<string> EnableThinkingModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Pro/zai-org/GLM-5",
            "Pro/zai-org/GLM-4.7",
            "deepseek-ai/DeepSeek-V3.2",
            "Pro/deepseek-ai/DeepSeek-V3.2",
            "zai-org/GLM-4.6",
            "Qwen/Qwen3-8B",
            "Qwen/Qwen3-14B",
            "Qwen/Qwen3-32B",
            "Qwen/Qwen3-30B-A3B",
            "tencent/Hunyuan-A13B-Instruct",
            "zai-org/GLM-4.5V",
            "deepseek-ai/DeepSeek-V3.1-Terminus",
            "Pro/deepseek-ai/DeepSeek-V3.1-Terminus",
            "Qwen/Qwen3.5-397B-A17B",
            "Qwen/Qwen3.5-122B-A10B",
            "Qwen/Qwen3.5-35B-A3B",
            "Qwen/Qwen3.5-27B",
            "Qwen/Qwen3.5-9B",
            "Qwen/Qwen3.5-4B"
        };

        public override string Name => "SiliconFlow";
        public override bool SupportsResponsesApi => false;

        public override string BuildChatCompletionsPayload(OpenAIChatRequest request)
        {
            if (request == null)
            {
                return "{}";
            }

            string model = request.Model?.Trim() ?? string.Empty;
            if (!EnableThinkingModels.Contains(model))
            {
                return base.BuildChatCompletionsPayload(request);
            }

            bool enableThinking = request.EnableThinkingOverride ?? false;
            var siliconRequest = new OpenAISiliconFlowChatRequest(request, enableThinking);
            return JsonUtility.ToJson(siliconRequest);
        }
    }
}
