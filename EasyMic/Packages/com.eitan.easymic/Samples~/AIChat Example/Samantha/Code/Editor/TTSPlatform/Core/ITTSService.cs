// ============================================================================
// ITTSService.cs - 语音合成服务抽象接口
// 所有平台服务都必须实现此接口，保证统一的调用方式
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TTSPlatform.Core
{
    /// <summary>
    /// TTS服务接口 - 所有平台必须实现
    /// </summary>
    public interface ITTSService
    {
        /// <summary>服务唯一标识</summary>
        string ServiceId { get; }

        /// <summary>服务显示名称</summary>
        string DisplayName { get; }

        /// <summary>API密钥</summary>
        string ApiKey { get; set; }

        /// <summary>服务是否已配置（可用）</summary>
        bool IsConfigured { get; }

        /// <summary>获取服务支持的配置选项</summary>
        ServiceCapabilities GetCapabilities();

        /// <summary>上传参考音频</summary>
        Task<VoiceUploadResult> UploadVoiceAsync(VoiceUploadRequest request,
            IProgress<float> progress = null);

        /// <summary>获取音色列表</summary>
        Task<VoiceListResult> GetVoiceListAsync();

        /// <summary>删除音色</summary>
        Task<OperationResult> DeleteVoiceAsync(string voiceUri);

        /// <summary>语音合成</summary>
        Task<SynthesisResult> SynthesizeSpeechAsync(SynthesisRequest request,
            IProgress<float> progress = null);

        /// <summary>验证API密钥有效性</summary>
        Task<OperationResult> ValidateApiKeyAsync();
    }

    /// <summary>
    /// 服务能力描述 - 描述服务支持的功能和可选项
    /// </summary>
    public class ServiceCapabilities
    {
        /// <summary>支持的音色上传模型</summary>
        public List<ModelOption> VoiceUploadModels { get; set; } = new List<ModelOption>();

        /// <summary>支持的语音合成模型</summary>
        public List<ModelOption> SynthesisModels { get; set; } = new List<ModelOption>();

        /// <summary>预置音色列表</summary>
        public List<VoiceOption> PresetVoices { get; set; } = new List<VoiceOption>();

        /// <summary>支持的音频格式</summary>
        public List<string> AudioFormats { get; set; } = new List<string>();

        /// <summary>支持的采样率</summary>
        public List<int> SampleRates { get; set; } = new List<int>();

        /// <summary>是否支持流式输出</summary>
        public bool SupportsStreaming { get; set; }

        /// <summary>是否支持多说话人</summary>
        public bool SupportsMultiSpeaker { get; set; }

        /// <summary>是否支持参考音色克隆</summary>
        public bool SupportsVoiceCloning { get; set; }

        /// <summary>语速范围</summary>
        public RangeValue SpeedRange { get; set; } = new RangeValue(0.25f, 4f, 1f);

        /// <summary>增益范围</summary>
        public RangeValue GainRange { get; set; } = new RangeValue(-10f, 10f, 0f);
    }

    /// <summary>模型选项</summary>
    public class ModelOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }

        public ModelOption() { }
        public ModelOption(string id, string displayName, string description = "")
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
        }
    }

    /// <summary>音色选项</summary>
    public class VoiceOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Category { get; set; }

        public VoiceOption() { }
        public VoiceOption(string id, string displayName, string category = "")
        {
            Id = id;
            DisplayName = displayName;
            Category = category;
        }
    }

    /// <summary>范围值</summary>
    public struct RangeValue
    {
        public float Min;
        public float Max;
        public float Default;

        public RangeValue(float min, float max, float defaultValue)
        {
            Min = min;
            Max = max;
            Default = defaultValue;
        }
    }
}
