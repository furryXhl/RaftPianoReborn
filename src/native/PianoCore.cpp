// Raft Piano Reborn: new input + sample-clock renderer. No MIDI file player.
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#define RP_BUILD
#include <windows.h>
#include <mmsystem.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstddef>
#include <cstring>
#include <fstream>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>
#include "PianoCore.h"
#include "QuietVoiceReclaimer.h"
#include "vendor/sfizz.h"

namespace {
constexpr int Capacity = 8192;
constexpr int MaxFrames = 8192;
constexpr float OutputGain = 0.525f; // +50% linear amplitude vs 0.35, no limiter/clipping/filtering.
int64_t clockNow() { LARGE_INTEGER x; QueryPerformanceCounter(&x); return x.QuadPart; }
int64_t clockHz() { LARGE_INTEGER x; QueryPerformanceFrequency(&x); return x.QuadPart; }
std::string sfizzWindowsPath(const char* utf8) {
    // sfizz 1.2.3's Windows file backend takes an ANSI filesystem path, not UTF-8.
    // Keep the public adapter ABI UTF-8, and convert explicitly at this boundary.
    int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8, -1, nullptr, 0);
    if (!count) throw std::runtime_error("Invalid UTF-8 sample path");
    std::wstring wide(static_cast<size_t>(count), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8, -1, &wide[0], count);
    BOOL replaced = FALSE;
    UINT cp = GetACP();
    DWORD flags = cp == CP_UTF8 ? 0 : WC_NO_BEST_FIT_CHARS;
    BOOL* replacement = cp == CP_UTF8 ? nullptr : &replaced;
    int bytes = WideCharToMultiByte(cp, flags, wide.c_str(), -1, nullptr, 0, nullptr, replacement);
    std::string result(static_cast<size_t>(bytes), '\0');
    WideCharToMultiByte(cp, flags, wide.c_str(), -1, &result[0], bytes, nullptr, replacement);
    if (replaced) {
        DWORD n = GetShortPathNameW(wide.c_str(), nullptr, 0);
        if (!n) throw std::runtime_error("Sample path cannot be represented by the Windows file backend");
        std::wstring shortPath(n, L'\0'); GetShortPathNameW(wide.c_str(), &shortPath[0], n);
        replaced = FALSE;
        bytes = WideCharToMultiByte(cp, flags, shortPath.c_str(), -1, nullptr, 0, nullptr, replacement);
        result.assign(static_cast<size_t>(bytes), '\0');
        WideCharToMultiByte(cp, flags, shortPath.c_str(), -1, &result[0], bytes, nullptr, replacement);
        if (replaced) throw std::runtime_error("Use an ASCII/Chinese-compatible RML data path for this sfizz build");
    }
    return result;
}
struct Event { int64_t stamp; uint32_t epoch; int type, a, b; };
// Exactly one producer (dedicated keyboard thread), one consumer (FMOD mixer).
struct EventQueue {
    std::array<Event, Capacity> slots{};
    alignas(64) std::atomic<uint32_t> write{0};
    alignas(64) std::atomic<uint32_t> read{0};
    bool push(Event e) {
        const uint32_t w = write.load(std::memory_order_relaxed);
        if (w - read.load(std::memory_order_acquire) == Capacity) return false;
        slots[w % Capacity] = e;
        write.store(w + 1, std::memory_order_release);
        return true;
    }
    bool peek(Event& e) {
        const uint32_t r = read.load(std::memory_order_relaxed);
        if (r == write.load(std::memory_order_acquire)) return false;
        e = slots[r % Capacity]; return true;
    }
    void pop() { read.fetch_add(1, std::memory_order_release); }
};
constexpr int Unbound = -1000;
constexpr int Layers[] = {-3, -2, -1, 1, 2, 3};
#ifdef RP_TESTING
std::atomic<bool> fakeKeyboard{false}, fakeForeground{true};
std::array<std::atomic<bool>, 256> fakeKeys{};
#endif
bool readPianoKey(int vk) {
#ifdef RP_TESTING
    if (fakeKeyboard.load()) return fakeKeys[vk].load();
#endif
    return (GetAsyncKeyState(vk) & 0x8000) != 0;
}

struct Engine {
    sfizz_synth_t* synth = nullptr;
    int rate, expectedFrames;
    const int64_t frequency = clockHz();
    // Input slack is constant, at least two mixer blocks. It is NOT an audio FIFO.
    int inputSlack;
    EventQueue events;
    std::atomic<bool> requested{false};
    std::atomic<uint32_t> epoch{1};
    std::atomic<uint64_t> dropped{0}, conflicts{0}, outOfRange{0}, hookEvents{0};
    std::atomic<uint32_t> velocity{96}, active{0};
    std::atomic<uint32_t> heldNotes{0};
    bool accepting = false; // The following key state belongs only to the input thread.
    // Windows virtual-key bindings are configured before the input thread starts.
    std::array<int, 256> noteOffsets{};
    std::array<int, 6> layerKeys{};
    // Control order: reset, velocity up, velocity down, sustain.
    std::array<int, 4> controlKeys{};
    std::array<bool, 256> down{};
    std::array<int, 256> latched{};
    std::array<int, 128> owner{};
    HANDLE inputThreadHandle = nullptr, inputReady = nullptr, inputWake = nullptr;
    std::atomic<bool> inputOK{false}, inputStop{false};
    std::atomic<uint64_t> polls{0}, maxPollGap{0};
    std::atomic<uint32_t> inputForeground{0}, inputRunning{0}, lastVk{0}, lastDown{0}, highRes{0};
    std::array<float, MaxFrames> left{}, right{};
    std::array<bool, 128> sounding{}; // Audio-thread ownership.
    uint32_t audioEpoch = 1;
    uint64_t sampleClock = 0;
    bool clockStarted = false;
    double origin = 0;
    int64_t previousCallback = 0;
    RpStats counters{};
    QuietVoiceReclaimer quietReclaimer;
    // Published through individual atomics: the audio callback never locks for UI reads.
    std::array<std::atomic<uint64_t>, 16> published{};
#ifdef RP_TESTING
    struct Trace { int64_t frame; int type, value; };
    std::vector<Trace> trace;
#endif
    explicit Engine(int sr, int block) : rate(sr), expectedFrames(block) {
        inputSlack = std::max(block * 2, sr * 32 / 1000);
        latched.fill(-1); owner.fill(-1);
        setDefaultBindings();
    }
    ~Engine() { if (synth) sfizz_free(synth); }
    void setDefaultBindings() {
        noteOffsets.fill(Unbound);
        auto bind = [&](int vk, int offset) { noteOffsets[vk] = offset; };
        bind('Z', 0); bind('S', 1); bind('X', 2); bind('D', 3);
        bind('C', 4); bind('V', 5); bind('G', 6); bind('B', 7);
        bind('H', 8); bind('N', 9); bind('J', 10); bind('M', 11);
        bind(VK_OEM_COMMA, 12); bind('Q', 12);
        bind('L', 13); bind('2', 13); bind(VK_OEM_PERIOD, 14); bind('W', 14);
        bind(VK_OEM_1, 15); bind('3', 15); bind(VK_OEM_2, 16); bind('4', 16);
        bind('R', 17); bind('5', 18); bind('T', 19); bind('6', 20);
        bind('Y', 21); bind('7', 22); bind('U', 23); bind('I', 24);
        bind('9', 25); bind('O', 26); bind('0', 27); bind('P', 28);
        bind(VK_OEM_4, 29); bind(VK_OEM_PLUS, 30); bind(VK_OEM_6, 31);
        layerKeys = {VK_TAB, VK_CAPITAL, VK_LSHIFT, VK_OEM_5, VK_RETURN, VK_RSHIFT};
        controlKeys = {VK_HOME, VK_UP, VK_DOWN, VK_SPACE};
    }
    int keyOffset(int vk) const { return vk >= 0 && vk < 256 ? noteOffsets[vk] : Unbound; }
    bool isLayerKey(int vk) const {
        for (int key : layerKeys) if (vk == key) return true;
        return false;
    }
    bool isPianoKey(int vk) const {
        if (keyOffset(vk) != Unbound || isLayerKey(vk)) return true;
        for (int key : controlKeys) if (vk == key) return true;
        return false;
    }
    void releaseKeys() {
        // Epoch invalidates unplayed attacks from the previous focus/seat session.
        // Audio thread releases keys naturally; no all_sound_off or waveform reset.
        epoch.fetch_add(1, std::memory_order_release);
        latched.fill(-1); owner.fill(-1);
        heldNotes.store(0);
    }
    void setAccepting(bool value) {
        if (value == accepting) return;
        accepting = value; active.store(value ? 1u : 0u);
        releaseKeys();
        for (int i = 0; i < 256; ++i) down[i] = value && isPianoKey(i) && readPianoKey(i);
        // A held note on entering the seat must be released before it can start.
    }
    void enqueue(int type, int a, int b, int64_t stamp) {
        if (!events.push(Event{stamp, epoch.load(std::memory_order_relaxed), type, a, b})) {
            dropped.fetch_add(1, std::memory_order_relaxed);
            releaseKeys(); // Fail safely on overflow instead of leaving stuck notes.
        }
    }
    void key(int vk, bool pressed, int64_t stamp) {
        if (vk < 0 || vk >= 256) return;
        if (down[vk] == pressed) return; // OS typematic is not a new piano attack.
        hookEvents.fetch_add(1, std::memory_order_relaxed); // Kept at its original stats ABI offset.
        lastVk.store(static_cast<uint32_t>(vk)); lastDown.store(pressed ? 1u : 0u);
        down[vk] = pressed;
        if (!accepting) return;
        if (vk == controlKeys[0] && pressed) { releaseKeys(); velocity.store(96); return; }
        if (vk == controlKeys[1] && pressed) { velocity.store(std::min(127u, velocity.load() + 8u)); return; }
        if (vk == controlKeys[2] && pressed) { velocity.store(std::max(8u, velocity.load() > 8 ? velocity.load() - 8 : 8u)); return; }
        if (vk == controlKeys[3]) { enqueue(2, 64, pressed ? 127 : 0, stamp); return; }
        int offset = keyOffset(vk);
        if (offset == Unbound) return;
        if (!pressed) {
            int note = latched[vk]; latched[vk] = -1;
            if (note >= 0) heldNotes.fetch_sub(1);
            if (note >= 0 && owner[note] == vk) {
                owner[note] = -1; enqueue(1, note, 0, stamp);
            }
            return;
        }
        int count = 0, layer = 0;
        for (int i = 0; i < 6; ++i) if (down[layerKeys[i]]) { ++count; layer = Layers[i]; }
        if (count > 1) { conflicts.fetch_add(1); return; }
        const int note = 48 + offset + 12 * layer;
        if (note < 21 || note > 108) { outOfRange.fetch_add(1); return; }
        // Last attack owns the note-off. Releasing another same-pitch alias cannot
        // stop this attack, and modifier changes never alter a latched pitch.
        latched[vk] = note; owner[note] = vk;
        heldNotes.fetch_add(1);
        enqueue(0, note, static_cast<int>(velocity.load()), stamp);
    }
    void pollKeyboard() {
        std::array<bool, 256> snapshot{};
        for (int vk = 0; vk < 256; ++vk) if (isPianoKey(vk)) snapshot[vk] = readPianoKey(vk);
        const int64_t stamp = clockNow();
        // Release latched notes first, then apply all modifier changes, then note-ons.
        // This gives one coherent chord snapshot and never starts a note twice.
        for (int vk = 0; vk < 256; ++vk) if (down[vk] && !snapshot[vk]) key(vk, false, stamp);
        for (int vk : layerKeys) if (!down[vk] && snapshot[vk]) key(vk, true, stamp);
        for (int vk : controlKeys) if (!down[vk] && snapshot[vk]) key(vk, true, stamp);
        for (int vk = 0; vk < 256; ++vk)
            if (keyOffset(vk) != Unbound && !down[vk] && snapshot[vk]) key(vk, true, stamp);
    }
    void syncAudioEpoch() {
        uint32_t e = epoch.load(std::memory_order_acquire);
        if (audioEpoch == e) return;
        quietReclaimer.reset();
        sfizz_send_cc(synth, 0, 64, 0);
        counters.pedalDown = 0;
        for (int n = 0; n < 128; ++n) if (sounding[n]) sfizz_send_note_off(synth, 0, n, 0);
        sounding.fill(false); audioEpoch = e; ++counters.resets;
    }
    void dispatch(Event e, int delay) {
#ifdef RP_TESTING
        trace.push_back(Trace{static_cast<int64_t>(sampleClock) + delay, e.type, e.a});
#endif
        if (e.type == 0) {
            sfizz_send_note_on(synth, delay, e.a, e.b); sounding[e.a] = true; ++counters.attacks;
        } else if (e.type == 1) {
            sfizz_send_note_off(synth, delay, e.a, e.b); sounding[e.a] = false; ++counters.releases;
        } else {
            sfizz_send_cc(synth, delay, e.a, e.b);
            if (e.a == 64) counters.pedalDown = e.b >= 64 ? 1u : 0u;
        }
    }
    void render(float* output, int frames, int64_t now) {
        const int64_t begin = clockNow();
        syncAudioEpoch();
        if (!clockStarted) { origin = static_cast<double>(now); clockStarted = true; }
        const double ticksPerFrame = static_cast<double>(frequency) / rate;
        // Hardware audio clock and QPC may drift. Correct the phase very slowly,
        // never collapsing the events in a block to delay=0.
        double predicted = origin + static_cast<double>(sampleClock) * ticksPerFrame;
        const double phaseError = now - predicted;
        if (previousCallback != 0 && now - previousCallback > frequency / 4) {
            // Device suspension is not normal timing jitter; reanchor and report it.
            origin += phaseError;
        } else {
            origin += std::max(-ticksPerFrame * 0.1, std::min(ticksPerFrame * 0.1, phaseError * 0.001));
        }
        previousCallback = now;
        Event e{};
        int lastDelay = 0;
        bool inputChanged = false;
        while (events.peek(e)) {
            if (e.epoch != audioEpoch) { events.pop(); continue; }
            const int64_t target = static_cast<int64_t>(std::llround((e.stamp - origin) / ticksPerFrame)) + inputSlack;
            if (target >= static_cast<int64_t>(sampleClock + frames)) break;
            events.pop();
            int64_t delta = target - static_cast<int64_t>(sampleClock);
            if (delta < 0) ++counters.lateEvents;
            const int delay = std::max(lastDelay, static_cast<int>(std::max<int64_t>(0, delta)));
            dispatch(e, delay); lastDelay = delay; inputChanged = true;
        }
        float* channels[] = {left.data(), right.data()};
        sfizz_render_block(synth, channels, 2, frames);
        float blockPeak = 0.0f;
        bool validOutput = true;
        for (int i = 0; i < frames; ++i) {
            float l = left[i] * OutputGain, r = right[i] * OutputGain;
            if (!std::isfinite(l) || !std::isfinite(r)) { ++counters.nonFinite; l = r = 0; validOutput = false; }
            const float peak = std::max(std::abs(l), std::abs(r));
            blockPeak = std::max(blockPeak, peak);
            counters.peak = std::max(counters.peak, peak);
            if (peak > 1.0f) ++counters.peaksOverOne;
            output[2 * i] = l; output[2 * i + 1] = r;
        }
        sampleClock += frames;
        counters.frames = sampleClock; ++counters.blocks;
        counters.voices = static_cast<uint32_t>(sfizz_get_num_active_voices(synth));
        counters.maxVoices = std::max(counters.maxVoices, counters.voices);
        const bool audioKeysHeld = std::any_of(sounding.begin(), sounding.end(), [](bool v) { return v; });
        if (quietReclaimer.observe(blockPeak, audioKeysHeld || heldNotes.load() != 0,
                counters.pedalDown != 0, inputChanged || !validOutput,
                counters.voices, frames, rate)) {
            // Audio-thread only, after the output block is complete. No queues,
            // bindings, tuning, sample data or future attacks are reset.
            sfizz_send_cc(synth, 0, 120, 0);
            ++counters.resets;
            counters.voices = static_cast<uint32_t>(sfizz_get_num_active_voices(synth));
        }
        const uint64_t micros = static_cast<uint64_t>((clockNow() - begin) * 1000000 / frequency);
        counters.maxRenderUs = std::max(counters.maxRenderUs, micros);
        if (micros > static_cast<uint64_t>(frames) * 1000000 / rate) ++counters.renderOverruns;
        const uint64_t values[] = {counters.blocks, counters.frames, counters.attacks, counters.releases,
            counters.lateEvents, counters.renderOverruns, counters.maxRenderUs, counters.nonFinite,
            counters.peaksOverOne, counters.resets, counters.voices, counters.maxVoices};
        for (int i = 0; i < 12; ++i) published[i].store(values[i], std::memory_order_relaxed);
        uint32_t peakBits; std::memcpy(&peakBits, &counters.peak, 4); published[12].store(peakBits);
        published[13].store(counters.pedalDown);
    }
    void snapshot(RpStats& s) {
        s = {}; s.blocks = published[0].load(); s.frames = published[1].load();
        s.attacks = published[2].load(); s.releases = published[3].load();
        s.lateEvents = published[4].load(); s.renderOverruns = published[5].load();
        s.maxRenderUs = published[6].load(); s.nonFinite = published[7].load();
        s.peaksOverOne = published[8].load(); s.resets = published[9].load();
        s.voices = static_cast<uint32_t>(published[10].load()); s.maxVoices = static_cast<uint32_t>(published[11].load());
        uint32_t p = static_cast<uint32_t>(published[12].load()); std::memcpy(&s.peak, &p, 4);
        s.droppedEvents = dropped.load(); s.conflicts = conflicts.load(); s.outOfRange = outOfRange.load();
        s.velocity = velocity.load(); s.enabled = active.load(); s.hookEvents = hookEvents.load();
        s.pedalDown = static_cast<uint32_t>(published[13].load());
    }
};
std::atomic<Engine*> engine{nullptr};
std::string lastError;

bool ownForeground() {
#ifdef RP_TESTING
    if (fakeKeyboard.load()) return fakeForeground.load();
#endif
    DWORD pid = 0; GetWindowThreadProcessId(GetForegroundWindow(), &pid);
    return pid != 0 && pid == GetCurrentProcessId();
}
DWORD WINAPI inputThread(void* ptr) {
    Engine* p = static_cast<Engine*>(ptr);
    // Games or other hooks can prevent delivery of WH_KEYBOARD_LL events. Read
    // the Windows state directly on an independent 1ms-target thread instead.
    // No global hook, window subclass, key suppression, or game-frame polling.
    HANDLE timer = CreateWaitableTimerExW(nullptr, nullptr, 0x2, TIMER_ALL_ACCESS);
    bool highRes = timer != nullptr;
    bool periodRaised = false;
    if (!timer) {
        timer = CreateWaitableTimerW(nullptr, FALSE, nullptr);
        if (timer) periodRaised = timeBeginPeriod(1) == TIMERR_NOERROR;
    }
    p->highRes.store(highRes ? 1u : 0u);
    p->inputOK.store(timer != nullptr);
    p->inputRunning.store(timer ? 1u : 0u);
    SetEvent(p->inputReady);
    if (!timer) return 1;
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_ABOVE_NORMAL);
    int64_t previous = 0;
    while (!p->inputStop.load()) {
        const int64_t now = clockNow();
        p->polls.fetch_add(1, std::memory_order_relaxed);
        bool foreground = ownForeground();
        p->inputForeground.store(foreground ? 1u : 0u);
        p->setAccepting(p->requested.load() && foreground);
        if (!p->accepting) {
            previous = 0;
            WaitForSingleObject(p->inputWake, 10);
            continue;
        }
        if (previous) {
            const uint64_t gap = static_cast<uint64_t>((now - previous) * 1000000 / p->frequency);
            if (gap > p->maxPollGap.load()) p->maxPollGap.store(gap);
        }
        previous = now;
        p->pollKeyboard();
        int64_t remaining = now + p->frequency / 1000 - clockNow();
        LARGE_INTEGER due;
        due.QuadPart = -std::max<int64_t>(1, remaining * 10000000 / p->frequency);
        if (!SetWaitableTimer(timer, &due, 0, nullptr, nullptr, FALSE)) {
            p->inputOK.store(false); break;
        }
        HANDLE waits[] = {p->inputWake, timer};
        if (WaitForMultipleObjects(2, waits, FALSE, INFINITE) == WAIT_FAILED) {
            p->inputOK.store(false); break;
        }
    }
    p->setAccepting(false);
    p->inputRunning.store(0);
    CancelWaitableTimer(timer); CloseHandle(timer);
    if (periodRaised) timeEndPeriod(1);
    return 0;
}
// Exact FMOD DSP_READ_CALLBACK ABI; FMOD receives this native function address.
// This path does not enter C#, allocate managed memory, or read an audio FIFO.
int __stdcall dspRead(void*, float*, float* output, unsigned int length, int, int* outputChannels) {
    if (outputChannels) *outputChannels = 2;
    Engine* p = engine.load(std::memory_order_acquire);
    if (p == nullptr) { std::memset(output, 0, static_cast<size_t>(length) * 2 * sizeof(float)); return 0; }
    // Large/variable FMOD blocks are sliced without losing the continuous clock.
    const int64_t now = clockNow();
    for (unsigned int offset = 0; offset < length;) {
        const int n = static_cast<int>(std::min<unsigned int>(p->expectedFrames, length - offset));
        p->render(output + 2 * offset, n, now + static_cast<int64_t>(offset) * p->frequency / p->rate);
        offset += n;
    }
    return 0;
}
// FMOD_DSP_DESCRIPTION layout for Raft's Windows x64 FMOD SDK (110).
// Deliberately built and passed in native code: Unity Mono may replace an
// unmanaged function pointer with a managed thunk during delegate round-trips.
using DspReadFunction = int (__stdcall*)(void*, float*, float*, unsigned int, int, int*);
struct NativeDspDescription {
    uint32_t sdkVersion;
    char name[32];
    uint32_t version;
    int numInputBuffers, numOutputBuffers;
    void* create;
    void* release;
    void* reset;
    DspReadFunction read;
    void* process;
    void* setPosition;
    int numParameters;
    void* paramDescription;
    void* setFloat;
    void* setInt;
    void* setBool;
    void* setData;
    void* getFloat;
    void* getInt;
    void* getBool;
    void* getData;
    void* shouldProcess;
    void* userData;
    void* systemRegister;
    void* systemDeregister;
    void* systemMix;
};
static_assert(sizeof(void*) == 8, "Raft Piano Reborn requires Windows x64");
static_assert(sizeof(NativeDspDescription) == 216, "FMOD DSP description size changed");
static_assert(offsetof(NativeDspDescription, read) == 72, "FMOD read callback offset changed");
static_assert(offsetof(NativeDspDescription, userData) == 184, "FMOD userdata offset changed");
}

int rp_create(const char* path, int sampleRate, int blockFrames) {
    if (engine.load()) { lastError = "Engine already exists. Restart Raft before loading another piano mod."; return 0; }
    try {
        if (!path || sampleRate < 8000 || sampleRate > 192000 || blockFrames < 1 || blockFrames > MaxFrames)
            throw std::runtime_error("Invalid sample rate or DSP block size");
        std::unique_ptr<Engine> p(new Engine(sampleRate, blockFrames));
        p->synth = sfizz_create_synth();
        if (!p->synth) throw std::runtime_error("sfizz allocation failed");
        sfizz_set_sample_rate(p->synth, static_cast<float>(sampleRate));
        sfizz_set_samples_per_block(p->synth, blockFrames);
        sfizz_set_num_voices(p->synth, 256);
        // Fixed windowed-sinc interpolation quality bounds real-time CPU cost and
        // never changes during playback.
        sfizz_set_sample_quality(p->synth, SFIZZ_PROCESS_LIVE, 7);
        sfizz_set_preload_size(p->synth, 8192);
        const std::string nativePath = sfizzWindowsPath(path);
        if (!sfizz_load_file(p->synth, nativePath.c_str()) || sfizz_get_num_regions(p->synth) < 1)
            throw std::runtime_error("Could not load the bundled Salamander SFZ");
        // Warm up the render buffers before the engine reaches FMOD. No audible test note.
        float* warm[] = {p->left.data(), p->right.data()};
        sfizz_render_block(p->synth, warm, 2, blockFrames);
        engine.store(p.release(), std::memory_order_release);
        lastError.clear(); return 1;
    } catch (const std::exception& e) { lastError = e.what(); return 0; }
    catch (...) { lastError = "Unknown native initialization error"; return 0; }
}
const char* rp_error() { return lastError.c_str(); }
void* rp_dsp_read() { return reinterpret_cast<void*>(&dspRead); }
int rp_create_dsp(void* fmodSystem, void** dsp) {
    if (!fmodSystem || !dsp || !engine.load()) {
        lastError = "Cannot create DSP before the piano engine and FMOD are ready";
        return -1;
    }
    *dsp = nullptr;
    HMODULE fmod = GetModuleHandleW(L"fmodstudio.dll");
    if (!fmod) fmod = GetModuleHandleW(L"fmod.dll");
    using CreateDspFunction = int (__stdcall*)(void*, const NativeDspDescription*, void**);
    CreateDspFunction createDsp = fmod ? reinterpret_cast<CreateDspFunction>(GetProcAddress(fmod, "FMOD_System_CreateDSP")) : nullptr;
    if (!createDsp) {
        lastError = "Cannot find FMOD_System_CreateDSP in Raft's loaded audio library";
        return -1;
    }
    NativeDspDescription desc{};
    desc.sdkVersion = 110;
    std::memcpy(desc.name, "Raft Piano Reborn", 17);
    desc.version = 0x00030008;
    desc.numInputBuffers = 0;
    desc.numOutputBuffers = 1;
    desc.read = &dspRead;
    int result = createDsp(fmodSystem, &desc, dsp);
    if (result != 0) lastError = "FMOD_System_CreateDSP failed, code=" + std::to_string(result);
    return result;
}
int rp_configure_bindings(const int* noteOffsets, int noteCount,
    const int* layerKeys, int layerCount, const int* controlKeys, int controlCount) {
    Engine* p = engine.load();
    if (!p) { lastError = "Cannot configure keys before the piano engine is ready"; return 0; }
    if (p->inputThreadHandle) { lastError = "Cannot configure keys after the keyboard reader has started"; return 0; }
    if (!noteOffsets || noteCount != 256 || !layerKeys || layerCount != 6 ||
        !controlKeys || controlCount != 4) {
        lastError = "Invalid keyboard binding array sizes"; return 0;
    }
    std::array<bool, 256> used{};
    std::array<bool, 32> covered{};
    for (int vk = 0; vk < 256; ++vk) {
        int offset = noteOffsets[vk];
        if (offset == Unbound) continue;
        if (offset < 0 || offset >= 32) { lastError = "A note semitone is outside 0..31"; return 0; }
        if (vk == 0) { lastError = "Virtual key zero cannot be bound"; return 0; }
        used[vk] = true; covered[offset] = true;
    }
    for (bool present : covered) if (!present) {
        lastError = "Every semitone from 0 through 31 needs at least one key"; return 0;
    }
    for (int i = 0; i < 6; ++i) {
        int vk = layerKeys[i];
        if (vk <= 0 || vk >= 256 || used[vk]) { lastError = "Duplicate or invalid octave key"; return 0; }
        used[vk] = true;
    }
    for (int i = 0; i < 4; ++i) {
        int vk = controlKeys[i];
        if (vk <= 0 || vk >= 256 || used[vk]) { lastError = "Duplicate or invalid control key"; return 0; }
        used[vk] = true;
    }
    std::copy(noteOffsets, noteOffsets + 256, p->noteOffsets.begin());
    std::copy(layerKeys, layerKeys + 6, p->layerKeys.begin());
    std::copy(controlKeys, controlKeys + 4, p->controlKeys.begin());
    lastError.clear(); return 1;
}
int rp_start_keyboard() {
    Engine* p = engine.load(); if (!p) return 0;
    if (p->inputThreadHandle) return p->inputOK.load() ? 1 : 0;
    p->inputReady = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    p->inputWake = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    if (!p->inputReady || !p->inputWake) { lastError = "Could not create keyboard control events"; return 0; }
    p->inputThreadHandle = CreateThread(nullptr, 0, inputThread, p, 0, nullptr);
    if (!p->inputThreadHandle) { lastError = "Could not create keyboard thread"; return 0; }
    if (WaitForSingleObject(p->inputReady, 5000) != WAIT_OBJECT_0 || !p->inputOK.load()) {
        lastError = "Could not start direct keyboard-state reader"; return 0;
    }
    return 1;
}
void rp_enable(int active) {
    Engine* p = engine.load(); if (!p) return;
    p->requested.store(active != 0);
    if (p->inputWake) SetEvent(p->inputWake);
}
void rp_stats(RpStats* stats) {
    if (!stats) return;
    Engine* p = engine.load(); if (p) p->snapshot(*stats); else *stats = {};
}
void rp_input_status(RpInputStatus* status) {
    if (!status) return;
    *status = {};
    Engine* p = engine.load(); if (!p) return;
    status->polls = p->polls.load(); status->maxGapUs = p->maxPollGap.load();
    status->requested = p->requested.load() ? 1u : 0u;
    status->foreground = p->inputForeground.load(); status->active = p->active.load();
    status->running = p->inputRunning.load(); status->lastVk = p->lastVk.load();
    status->lastDown = p->lastDown.load(); status->highResolutionTimer = p->highRes.load();
    status->heldNotes = p->heldNotes.load();
}
void rp_destroy() {
    Engine* p = engine.load(); if (!p) return;
    p->requested.store(false);
    p->inputStop.store(true);
    if (p->inputWake) SetEvent(p->inputWake);
    if (p->inputThreadHandle) {
        WaitForSingleObject(p->inputThreadHandle, INFINITE);
        CloseHandle(p->inputThreadHandle);
    }
    if (p->inputReady) CloseHandle(p->inputReady);
    if (p->inputWake) CloseHandle(p->inputWake);
    engine.store(nullptr, std::memory_order_release);
    delete p;
}
#ifdef RP_TESTING
void rp_test_key(int vk, int down, int64_t stamp) {
    Engine* p = engine.load(); p->accepting = true; p->active.store(1); p->key(vk, down != 0, stamp);
}
void rp_test_render(float* output, int frames, int64_t stamp) { engine.load()->render(output, frames, stamp); }
int64_t rp_test_clock() { return clockNow(); }
int64_t rp_test_frequency() { return clockHz(); }
int rp_test_note_for_key(int vk, int layer) {
    Engine* p = engine.load(); if (!p) return -1;
    int offset = p->keyOffset(vk); if (offset == Unbound) return -1;
    int n = 48 + offset + 12 * layer; return n >= 21 && n <= 108 ? n : -1;
}
int rp_test_trace(int64_t* frames, int* types, int* values, int capacity) {
    auto& trace = engine.load()->trace;
    int n = std::min(capacity, static_cast<int>(trace.size()));
    for (int i = 0; i < n; ++i) { frames[i] = trace[i].frame; types[i] = trace[i].type; values[i] = trace[i].value; }
    return static_cast<int>(trace.size());
}
void rp_test_reset_trace() { engine.load()->trace.clear(); }
void rp_test_quality(int quality) { sfizz_set_sample_quality(engine.load()->synth, SFIZZ_PROCESS_LIVE, quality); }
void rp_test_keyboard_fixture(int active, int foreground) { fakeKeyboard.store(active != 0); fakeForeground.store(foreground != 0); }
void rp_test_keyboard_state(int vk, int down) { if (vk >= 0 && vk < 256) fakeKeys[vk].store(down != 0); }
#endif
