namespace Eitan.EasyMic.Runtime{
    public enum SampleRate{
        /// <summary>
        /// 电话音质标准采样率 (8 kHz)
        /// </summary>
        Hz8000 = 8000,

        /// <summary>
        /// 语音识别/VoIP标准采样率 (16 kHz)
        /// </summary>
        Hz16000 = 16000,

        /// <summary>
        /// 广播/数字电台常用采样率 (32 kHz)
        /// </summary>
        Hz32000 = 32000,

        /// <summary>
        /// 专业音频/视频制作标准采样率 (48 kHz)
        /// </summary>
        Hz48000 = 48000,

    }
}