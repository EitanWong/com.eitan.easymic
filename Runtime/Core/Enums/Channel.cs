namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// 音频通道配置
    /// </summary>
    public enum Channel
    {
        /// <summary>
        /// 单声道 (1通道)
        /// </summary>
        Mono = 1,
        
        /// <summary>
        /// 立体声 (2通道)
        /// </summary>
        Stereo = 2,
        
        /// <summary>
        /// 四声道环绕 (4通道)
        /// </summary>
        Quad = 4,
        
        /// <summary>
        /// 5.1环绕声 (6通道)
        /// </summary>
        Surround51 = 6,
        
        /// <summary>
        /// 6.1环绕声 (7通道)
        /// </summary>
        Surround61 = 7,
        
        /// <summary>
        /// 7.1环绕声 (8通道)
        /// </summary>
        Surround71 = 8,
        
        /// <summary>
        /// 杜比全景声基础配置 (9通道)
        /// </summary>
        AtmosBase = 9,
        
        /// <summary>
        /// 专业录音棚配置 (12通道)
        /// </summary>
        Studio = 12,
        
        /// <summary>
        /// 杜比全景声高级配置 (16通道)
        /// </summary>
        AtmosExtended = 16
    }
}