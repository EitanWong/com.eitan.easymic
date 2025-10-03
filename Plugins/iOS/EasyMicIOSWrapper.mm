//
//  EasyMicIOSWrapper.mm
//  Wrapper functions for missing iOS symbols in miniaudio.framework
//
//  This wrapper implements sf_allocate_device_config_ex by calling the existing
//  sf_allocate_device_config function. The _ex variant is not included in the
//  iOS framework build, so we provide it here for compatibility.
//

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Forward declare the function that exists in miniaudio.framework
extern void* sf_allocate_device_config(int capabilityType, uint32_t sampleRate, void* dataCallback, void* pSfConfig);

// Implement the missing _ex variant as a wrapper
// The _ex version has more parameters but we ignore the extra ones and call the simpler version
void* sf_allocate_device_config_ex(
    int capabilityType,           // DeviceType (Playback=1, Record=2)
    int format,                   // SampleFormat (ignored - use default F32)
    uint32_t channels,            // Channels (ignored - will be set in config)
    uint32_t sampleRate,          // Sample rate
    void* dataCallback,           // AudioCallbackEx
    void* pUserData,              // User data pointer (ignored - callback handles this)
    void* playbackDevice,         // Playback device (ignored - use default)
    void* captureDevice)          // Capture device (ignored - use default)
{
    // Map DeviceType to Capability
    // DeviceType: Playback=1, Record=2, Mixed=3, Loopback=4
    // Capability: Playback=1, Record=2, Mixed=3, Loopback=4
    // They match, so pass through directly
    int capability = capabilityType;

    // Call the simpler version with NULL for pSfConfig (use defaults)
    // The callback will be set correctly, other parameters use framework defaults
    return sf_allocate_device_config(capability, sampleRate, dataCallback, NULL);
}

#ifdef __cplusplus
}
#endif
