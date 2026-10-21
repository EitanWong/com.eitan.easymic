#define MINIAUDIO_IMPLEMENTATION
#include "../Native/miniaudio.h"

extern "C" {
    MA_API ma_result EasyMicContextInitWithSharedAudioSession(
        const ma_backend* pBackends,
        ma_uint32 backendCount,
        ma_context* pContext)
    {
        ma_context_config config = ma_context_config_init();
        config.coreaudio.sessionCategory = ma_ios_session_category_play_and_record;
        config.coreaudio.sessionCategoryOptions =
            ma_ios_session_category_option_default_to_speaker |
            ma_ios_session_category_option_allow_bluetooth |
            ma_ios_session_category_option_allow_bluetooth_a2dp |
            ma_ios_session_category_option_allow_air_play;
        config.coreaudio.noAudioSessionDeactivate = MA_TRUE;
        return ma_context_init(pBackends, backendCount, &config, pContext);
    }
}
