#pragma once
#include <cmath>
#include <cstdint>

// Reclaim a sampler's near-silent leftovers, never a held note or pedal tail.
// This is not a gate: it does not modify the rendered output samples.
class QuietVoiceReclaimer {
    uint64_t quietFrames_ = 0;
public:
    void reset() { quietFrames_ = 0; }
    bool observe(float outputPeak, bool keysHeld, bool pedalDown, bool inputChanged,
                 uint32_t voices, int frames, int sampleRate) {
        if (keysHeld || pedalDown || inputChanged || voices == 0 || frames <= 0 || sampleRate <= 0
            || !std::isfinite(outputPeak) || outputPeak < 0.0f || outputPeak > 0.000001f) {
            reset();
            return false;
        }
        quietFrames_ += static_cast<uint64_t>(frames);
        if (quietFrames_ < static_cast<uint64_t>(sampleRate) * 2) return false;
        reset();
        return true;
    }
};
