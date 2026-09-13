#pragma once
#include <cstdint>
#ifdef RP_TESTING
#define RP_API extern "C"
#elif defined(RP_BUILD)
#define RP_API extern "C" __declspec(dllexport)
#else
#define RP_API extern "C" __declspec(dllimport)
#endif
struct RpStats {
    uint64_t blocks, frames, attacks, releases, lateEvents, droppedEvents;
    uint64_t renderOverruns, maxRenderUs, nonFinite, peaksOverOne;
    uint64_t conflicts, outOfRange, resets, hookEvents;
    uint32_t voices, maxVoices, velocity, enabled;
    float peak;
    uint32_t pedalDown; // Reuses the reserved word; struct size/previous offsets stay unchanged.
};
struct RpInputStatus {
    uint64_t polls, maxGapUs;
    uint32_t requested, foreground, active, running;
    uint32_t lastVk, lastDown, highResolutionTimer, heldNotes;
};
RP_API int rp_create(const char* sfzPath, int sampleRate, int blockFrames);
RP_API const char* rp_error();
RP_API void* rp_dsp_read();
RP_API int rp_create_dsp(void* fmodSystem, void** dsp);
RP_API int rp_configure_bindings(const int* noteOffsets, int noteCount,
    const int* layerKeys, int layerCount, const int* controlKeys, int controlCount);
RP_API int rp_start_keyboard();
RP_API void rp_enable(int active);
RP_API void rp_stats(RpStats* stats);
RP_API void rp_input_status(RpInputStatus* status);
RP_API void rp_destroy(); // Caller MUST detach the DSP and synchronize its mixer first.
#ifdef RP_TESTING
RP_API void rp_test_key(int vk, int down, int64_t stamp);
RP_API void rp_test_render(float* stereo, int frames, int64_t stamp);
RP_API int64_t rp_test_clock();
RP_API int64_t rp_test_frequency();
RP_API int rp_test_note_for_key(int vk, int layer);
RP_API int rp_test_trace(int64_t* frames, int* types, int* values, int capacity);
RP_API void rp_test_reset_trace();
RP_API void rp_test_quality(int quality);
RP_API void rp_test_keyboard_fixture(int active, int foreground);
RP_API void rp_test_keyboard_state(int vk, int down);
#endif
