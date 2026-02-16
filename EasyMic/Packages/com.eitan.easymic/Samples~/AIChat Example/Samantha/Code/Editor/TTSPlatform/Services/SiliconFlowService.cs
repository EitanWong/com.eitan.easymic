// ============================================================================
// SiliconFlowService.cs - SiliconFlow平台服务实现
// 实现SiliconFlow的所有API调用
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TTSPlatform.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace TTSPlatform.Services
{
    /// <summary>
    /// SiliconFlow平台服务实现
    /// </summary>
    public class SiliconFlowService : ITTSService
    {
        #region 常量定义

        private const string BASE_URL = "https://api.siliconflow.cn/v1";
        private const string UPLOAD_VOICE_ENDPOINT = "/uploads/audio/voice";
        private const string VOICE_LIST_ENDPOINT = "/audio/voice/list";
        private const string DELETE_VOICE_ENDPOINT = "/audio/voice/deletions";
        private const string SPEECH_ENDPOINT = "/audio/speech";

        #endregion

        #region 属性实现

        public string ServiceId => "siliconflow";
        public string DisplayName => "SiliconFlow";
        public string ApiKey { get; set; }
        public bool IsConfigured => !string.IsNullOrEmpty(ApiKey);

        #endregion

        #region 服务能力

        public ServiceCapabilities GetCapabilities()
        {
            return new ServiceCapabilities
            {
                VoiceUploadModels = new List<ModelOption>
                {
                    new ModelOption("FunAudioLLM/CosyVoice2-0.5B", "CosyVoice2 0.5B", "Voice cloning model")
                },
                SynthesisModels = new List<ModelOption>
                {
                    new ModelOption("FunAudioLLM/CosyVoice2-0.5B", "CosyVoice2 0.5B", "Speech synthesis"),
                    new ModelOption("fnlp/MOSS-TTSD-v0.5", "MOSS-TTSD v0.5", "Multi-voice dialogue synthesis"),
                    new ModelOption("IndexTeam/IndexTTS-2", "IndexTTS-2", "Speech synthesis")
                },
                PresetVoices = new List<VoiceOption>
                {
                    new VoiceOption("fnlp/MOSS-TTSD-v0.5:alex", "Alex", "English male"),
                    new VoiceOption("fnlp/MOSS-TTSD-v0.5:anna", "Anna", "English female"),
                    new VoiceOption("fnlp/MOSS-TTSD-v0.5:bella", "Bella", "English female"),
                    new VoiceOption("fnlp/MOSS-TTSD-v0.5:benjamin", "Benjamin", "English male"),
                    new VoiceOption("fnlp/MOSS-TTSD-v0.5:charles", "Charles", "English male"),
                    new VoiceOption("fnlp/MOSS-TTSD-v0.5:claire", "Claire", "English female"),
                    new VoiceOption("fnlp/MOSS-TTSD-v0.5:david", "David", "English male"),
                    new VoiceOption("fnlp/MOSS-TTSD-v0.5:diana", "Diana", "English female"),
                },
                AudioFormats = new List<string> { "mp3", "wav", "opus", "pcm" },
                SampleRates = new List<int> { 8000, 16000, 24000, 32000, 44100, 48000 },
                SupportsStreaming = true,
                SupportsMultiSpeaker = true,
                SupportsVoiceCloning = true,
                SpeedRange = new RangeValue(0.25f, 4f, 1f),
                GainRange = new RangeValue(-10f, 10f, 0f)
            };
        }

        #endregion

        #region API实现

        public async Task<OperationResult> ValidateApiKeyAsync()
        {
            if (!IsConfigured)
            {

                return OperationResult.Fail("API key is not configured.");
            }


            try
            {
                // 通过获取音色列表来验证API Key
                var result = await GetVoiceListAsync();
                return result.Success
                    ? OperationResult.Ok("API key validated.")
                    : OperationResult.Fail(result.Message);
            }
            catch (Exception e)
            {
                return OperationResult.Fail($"Validation failed: {e.Message}");
            }
        }

        public async Task<VoiceUploadResult> UploadVoiceAsync(VoiceUploadRequest request,
            IProgress<float> progress = null)
        {
            if (!IsConfigured)
            {

                return new VoiceUploadResult { Success = false, Message = "API key is not configured." };
            }


            try
            {
                var form = new List<IMultipartFormSection>();
                form.Add(new MultipartFormDataSection("model", request.Model));
                form.Add(new MultipartFormDataSection("customName", request.CustomName));
                form.Add(new MultipartFormDataSection("text", request.Text));

                // 添加音频文件
                if (!string.IsNullOrEmpty(request.AudioFilePath))
                {
                    var audioData = System.IO.File.ReadAllBytes(request.AudioFilePath);
                    var fileName = System.IO.Path.GetFileName(request.AudioFilePath);
                    form.Add(new MultipartFormFileSection("file", audioData, fileName,
                        GetMimeType(request.AudioFilePath)));
                }
                else if (!string.IsNullOrEmpty(request.AudioBase64))
                {
                    form.Add(new MultipartFormDataSection("audio", request.AudioBase64));
                }
                else
                {
                    return new VoiceUploadResult
                    {
                        Success = false,
                        Message = "Provide an audio file or Base64 data."
                    };
                }

                var response = await SendMultipartRequestAsync(
                    BASE_URL + UPLOAD_VOICE_ENDPOINT,
                    form,
                    progress);

                if (response.Success)
                {
                    var json = JsonUtility.FromJson<UploadVoiceResponse>(response.Data);
                    return new VoiceUploadResult
                    {
                        Success = true,
                        VoiceUri = json.uri,
                        Message = "Voice upload succeeded."
                    };
                }

                return new VoiceUploadResult { Success = false, Message = response.Message };
            }
            catch (Exception e)
            {
                return new VoiceUploadResult { Success = false, Message = $"Upload failed: {e.Message}" };
            }
        }

        public async Task<VoiceListResult> GetVoiceListAsync()
        {
            if (!IsConfigured)
            {

                return new VoiceListResult { Success = false, Message = "API key is not configured." };
            }


            try
            {
                var response = await SendGetRequestAsync(BASE_URL + VOICE_LIST_ENDPOINT);

                if (response.Success)
                {
                    var json = JsonUtility.FromJson<VoiceListResponse>(response.Data);
                    var voices = new List<VoiceInfo>();

                    if (json.result != null)
                    {
                        foreach (var item in json.result)
                        {
                            voices.Add(new VoiceInfo
                            {
                                Uri = item.uri,
                                CustomName = item.customName,
                                Model = item.model,
                                CreatedAt = ParseDateTime(item.created_at)
                            });
                        }
                    }

                    return new VoiceListResult
                    {
                        Success = true,
                        Voices = voices,
                        Message = $"Loaded {voices.Count} voices."
                    };
                }

                return new VoiceListResult { Success = false, Message = response.Message };
            }
            catch (Exception e)
            {
                return new VoiceListResult { Success = false, Message = $"Load failed: {e.Message}" };
            }
        }

        public async Task<OperationResult> DeleteVoiceAsync(string voiceUri)
        {
            if (!IsConfigured)
            {

                return OperationResult.Fail("API key is not configured.");
            }


            try
            {
                var requestBody = JsonUtility.ToJson(new DeleteVoiceRequest { uri = voiceUri });
                var response = await SendPostRequestAsync(
                    BASE_URL + DELETE_VOICE_ENDPOINT,
                    requestBody);

                return response.Success
                    ? OperationResult.Ok("Voice deleted.")
                    : OperationResult.Fail(response.Message);
            }
            catch (Exception e)
            {
                return OperationResult.Fail($"Delete failed: {e.Message}");
            }
        }

        public async Task<SynthesisResult> SynthesizeSpeechAsync(SynthesisRequest request,
            IProgress<float> progress = null)
        {
            if (!IsConfigured)
            {

                return new SynthesisResult { Success = false, Message = "API key is not configured." };
            }


            try
            {
                var speechRequest = new SpeechApiRequest
                {
                    model = request.Model,
                    input = request.Input,
                    voice = request.Voice,
                    response_format = request.ResponseFormat,
                    sample_rate = request.SampleRate,
                    stream = false, // 编辑器中不使用流式
                    speed = request.Speed,
                    gain = request.Gain
                };

                var requestBody = JsonUtility.ToJson(speechRequest);
                var response = await SendPostRequestForBinaryAsync(
                    BASE_URL + SPEECH_ENDPOINT,
                    requestBody,
                    progress);

                if (response.Success)
                {
                    return new SynthesisResult
                    {
                        Success = true,
                        AudioData = response.BinaryData,
                        Format = request.ResponseFormat,
                        Message = "Synthesis succeeded."
                    };
                }

                return new SynthesisResult { Success = false, Message = response.Message };
            }
            catch (Exception e)
            {
                return new SynthesisResult { Success = false, Message = $"Synthesis failed: {e.Message}" };
            }
        }

        #endregion

        #region HTTP请求辅助方法

        private async Task<ApiResponse> SendGetRequestAsync(string url)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {ApiKey}");

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Delay(10);
                }


                return ParseResponse(request);
            }
        }

        private async Task<ApiResponse> SendPostRequestAsync(string url, string jsonBody)
        {
            using (var request = new UnityWebRequest(url, "POST"))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {ApiKey}");
                request.SetRequestHeader("Content-Type", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Delay(10);
                }


                return ParseResponse(request);
            }
        }

        private async Task<BinaryApiResponse> SendPostRequestForBinaryAsync(
            string url, string jsonBody, IProgress<float> progress = null)
        {
            using (var request = new UnityWebRequest(url, "POST"))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {ApiKey}");
                request.SetRequestHeader("Content-Type", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    progress?.Report(request.downloadProgress);
                    await Task.Delay(10);
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    return new BinaryApiResponse
                    {
                        Success = true,
                        BinaryData = request.downloadHandler.data
                    };
                }

                return new BinaryApiResponse
                {
                    Success = false,
                    Message = ParseErrorMessage(request)
                };
            }
        }

        private async Task<ApiResponse> SendMultipartRequestAsync(
            string url, List<IMultipartFormSection> form, IProgress<float> progress = null)
        {
            using (var request = UnityWebRequest.Post(url, form))
            {
                request.SetRequestHeader("Authorization", $"Bearer {ApiKey}");

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    progress?.Report(request.uploadProgress);
                    await Task.Delay(10);
                }

                return ParseResponse(request);
            }
        }

        private ApiResponse ParseResponse(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                return new ApiResponse
                {
                    Success = true,
                    Data = request.downloadHandler.text
                };
            }

            return new ApiResponse
            {
                Success = false,
                Message = ParseErrorMessage(request),
                StatusCode = (int)request.responseCode
            };
        }

        private string ParseErrorMessage(UnityWebRequest request)
        {
            try
            {
                var text = request.downloadHandler?.text;
                if (!string.IsNullOrEmpty(text))
                {
                    var error = JsonUtility.FromJson<ErrorResponse>(text);
                    if (!string.IsNullOrEmpty(error.message))
                    {

                        return error.message;
                    }

                }
            }
            catch { }

            return $"HTTP {request.responseCode}: {request.error}";
        }

        private string GetMimeType(string filePath)
        {
            var ext = System.IO.Path.GetExtension(filePath).ToLower();
            return ext switch
            {
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                ".flac" => "audio/flac",
                ".m4a" => "audio/mp4",
                _ => "application/octet-stream"
            };
        }

        private DateTime ParseDateTime(string dateStr)
        {
            if (DateTime.TryParse(dateStr, out var dt))
            {

                return dt;
            }


            return DateTime.MinValue;
        }

        #endregion

        #region 内部数据类型

        private class ApiResponse
        {
            public bool Success;
            public string Data;
            public string Message;
            public int StatusCode;
        }

        private class BinaryApiResponse
        {
            public bool Success;
            public byte[] BinaryData;
            public string Message;
        }

        [Serializable]
        private class UploadVoiceResponse
        {
            public string uri;
        }

        [Serializable]
        private class VoiceListResponse
        {
            public VoiceItemResponse[] result;
        }

        [Serializable]
        private class VoiceItemResponse
        {
            public string uri;
            public string customName;
            public string model;
            public string created_at;
        }

        [Serializable]
        private class DeleteVoiceRequest
        {
            public string uri;
        }

        [Serializable]
        private class SpeechApiRequest
        {
            public string model;
            public string input;
            public string voice;
            public string response_format;
            public int sample_rate;
            public bool stream;
            public float speed;
            public float gain;
        }

        [Serializable]
        private class ErrorResponse
        {
            public int code;
            public string message;
            public string data;
        }

        #endregion
    }
}
