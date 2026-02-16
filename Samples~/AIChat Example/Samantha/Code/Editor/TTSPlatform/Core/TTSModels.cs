// ============================================================================
// TTSModels.cs - 通用数据模型定义
// 定义了请求、响应等所有数据结构
// ============================================================================

using System;
using System.Collections.Generic;

namespace TTSPlatform.Core
{
    #region 基础结果类型

    /// <summary>通用操作结果</summary>
    public class OperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int ErrorCode { get; set; }

        public static OperationResult Ok(string message = "Success")
            => new OperationResult { Success = true, Message = message };

        public static OperationResult Fail(string message, int code = -1)
            => new OperationResult { Success = false, Message = message, ErrorCode = code };
    }

    #endregion

    #region 音色上传

    /// <summary>音色上传请求</summary>
    public class VoiceUploadRequest
    {
        /// <summary>模型ID</summary>
        public string Model { get; set; }

        /// <summary>自定义音色名称</summary>
        public string CustomName { get; set; }

        /// <summary>参考文本（与音频内容一致）</summary>
        public string Text { get; set; }

        /// <summary>音频文件路径（与Base64二选一）</summary>
        public string AudioFilePath { get; set; }

        /// <summary>Base64编码音频（与文件路径二选一）</summary>
        public string AudioBase64 { get; set; }
    }

    /// <summary>音色上传结果</summary>
    public class VoiceUploadResult : OperationResult
    {
        /// <summary>生成的音色URI</summary>
        public string VoiceUri { get; set; }
    }

    #endregion

    #region 音色列表

    /// <summary>音色信息</summary>
    public class VoiceInfo
    {
        public string Uri { get; set; }
        public string CustomName { get; set; }
        public string Model { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>扩展数据（平台特定）</summary>
        public Dictionary<string, object> ExtendedData { get; set; }
            = new Dictionary<string, object>();
    }

    /// <summary>音色列表结果</summary>
    public class VoiceListResult : OperationResult
    {
        public List<VoiceInfo> Voices { get; set; } = new List<VoiceInfo>();
    }

    #endregion

    #region 语音合成

    /// <summary>参考音频（用于音色克隆）</summary>
    public class ReferenceAudio
    {
        public string Audio { get; set; }  // Base64或URI
        public string Text { get; set; }
    }

    /// <summary>语音合成请求</summary>
    public class SynthesisRequest
    {
        /// <summary>合成模型</summary>
        public string Model { get; set; }

        /// <summary>输入文本</summary>
        public string Input { get; set; }

        /// <summary>预置音色（与References二选一）</summary>
        public string Voice { get; set; }

        /// <summary>参考音色列表（与Voice二选一）</summary>
        public List<ReferenceAudio> References { get; set; }

        /// <summary>音频格式</summary>
        public string ResponseFormat { get; set; } = "mp3";

        /// <summary>采样率</summary>
        public int SampleRate { get; set; } = 32000;

        /// <summary>是否流式</summary>
        public bool Stream { get; set; } = false;

        /// <summary>语速 (0.25-4.0)</summary>
        public float Speed { get; set; } = 1.0f;

        /// <summary>增益 (-10 to 10)</summary>
        public float Gain { get; set; } = 0f;

        /// <summary>最大Token数</summary>
        public int MaxTokens { get; set; } = 2048;
    }
   
    /// <summary>语音合成结果</summary>
    public class SynthesisResult : OperationResult
    {
        /// <summary>音频数据</summary>
        public byte[] AudioData { get; set; }

        /// <summary>音频格式</summary>
        public string Format { get; set; }

        /// <summary>音频时长（秒）</summary>
        public float Duration { get; set; }
    }

    #endregion
}
