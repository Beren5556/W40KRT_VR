#pragma once
#include <cstdint>
#ifdef RTMaquetaBridge_EXPORTS
#define RTXR extern "C" __declspec(dllexport)
#else
#define RTXR extern "C" __declspec(dllimport)
#endif

// Plain C ABI. All poses use the OpenXR right-handed convention (forward -Z).
#pragma pack(push, 4)
struct Pose { float qx, qy, qz, qw, x, y, z; };
struct Fov { float left, right, up, down; };
struct View { Pose pose; Fov fov; };
struct Frame {
    int32_t width, height, state, shouldRender, valid;
    uint64_t serial;
    Pose head;
    View left, right;
};
struct Crop { float left, top, right, bottom; };
struct Stats {
    uint64_t begun, submittedPairs, flatFrames, emptyFrames, failedFrames;
    uint64_t lastSerial, lastLeftSerial, lastRightSerial;
    int32_t state, queued;
};
struct BeginTiming {
    uint64_t serial;
    double queueWaitMs, runtimeWaitMs, beginLocateMs, predictedPeriodMs;
};
// Completed render-thread event, independent of Frame/Stats ABI and GPU queries.
struct RenderTiming {
    uint64_t serial;
    int32_t images, flushes, sourceViewsCreated, completed;
    double acquireWaitMs, drawFlushMs, releaseMs, endFrameMs, totalMs;
};
// Independent diagnostics ABI. Native GPU samples are delayed, never waited on.
struct GpuPassTiming {
    uint64_t observations, rejected;
    double sumMs, peakMs;
};
// Independent ABI: Frame remains 144 bytes. Pose flags use the low four
// XrSpaceLocationFlags bits (valid orientation/position, tracked orientation/position).
enum TouchControl : uint32_t {
    TouchAim = 1, TouchGrip = 2, TouchTrigger = 4, TouchSqueeze = 8,
    TouchStick = 16, TouchPrimary = 32, TouchSecondary = 64,
    TouchMenu = 128, TouchStickClick = 256, TouchThumbrest = 512
};
enum TouchButton : uint32_t {
    TouchPrimaryButton = 1, TouchSecondaryButton = 2, TouchMenuButton = 4, TouchStickButton = 8, TouchThumbrestButton = 16
};
struct TouchHand {
    Pose aim, grip;
    uint32_t aimFlags, gripFlags, activeControls, buttons;
    float trigger, squeeze, stickX, stickY;
};
struct TouchFrame {
    uint64_t serial;
    int64_t predictedDisplayTime;
    int32_t ready, focused, state, lastResult;
    TouchHand left, right;
};
struct FlatPanel {
    uint64_t serial;
    Pose pose;
    float width, height;
    int32_t valid, flipVertical;
};
#pragma pack(pop)
static_assert(sizeof(Frame) == 144, "Managed/native frame ABI mismatch");
static_assert(sizeof(Stats) == 72, "Managed/native stats ABI mismatch");
static_assert(sizeof(BeginTiming) == 40, "Managed/native begin timing ABI mismatch");
static_assert(sizeof(RenderTiming) == 64, "Managed/native render timing ABI mismatch");
static_assert(sizeof(TouchHand) == 88, "Managed/native touch hand ABI mismatch");
static_assert(sizeof(TouchFrame) == 208, "Managed/native touch frame ABI mismatch");
static_assert(sizeof(FlatPanel) == 52, "Managed/native flat panel ABI mismatch");

RTXR int __cdecl RTX_Init(void* unityTexture, float renderScale, const wchar_t* logPath);
RTXR int __cdecl RTX_SelectRuntime(int runtime); // 0 VDXR, 1 Meta Quest Link; before initialization.
RTXR int __cdecl RTX_Begin(Frame* frame);
// Queues immutable frame data; GPU work happens exclusively in RTX_RenderEvent.
RTXR int __cdecl RTX_Queue(uint64_t serial, void* left, void* right, void* flat,
                          Crop leftCrop, Crop rightCrop, int flipEyes, int flipFlat,
                          float panelDistance, int recenterPanel);
// Queue snapshots the next flat width/distance ratio; existing frame ABI remains unchanged.
RTXR void __cdecl RTX_SetFlatPanelWidth(float widthRatio);
RTXR void __cdecl RTX_SetFlatPanelOffset(float x, float y);
RTXR void __cdecl RTX_SetFlatPanelAspect(float aspect);
// UI-only black/white captures from the same Unity frame. Retained by Queue;
// independent of the world-eye resolution and never reused across serials.
RTXR int __cdecl RTX_SetSpatialFrame(uint64_t serial, void* black, void* white, Pose pose, float width, float height, int linear);
RTXR int __cdecl RTX_SetSpatialInformation(uint64_t serial, Pose pose, float size, int visible);
RTXR int __cdecl RTX_GetSpatialStatus();
RTXR int __cdecl RTX_SetHudFrame(uint64_t serial, void* black, void* white,
    float width, float height, float distance, float x, float y, int linear);
RTXR int __cdecl RTX_GetHudStatus();
RTXR uint64_t __cdecl RTX_GetTrackingOriginRevision();
RTXR void* __cdecl RTX_GetRenderEvent();
// 0 retains all resources until the queued CPU event and prior GPU copies retire.
RTXR int __cdecl RTX_Shutdown();
RTXR void __cdecl RTX_GetStats(Stats* stats);
RTXR void __cdecl RTX_GetBeginTiming(BeginTiming* timing);
RTXR int __cdecl RTX_GetRenderTiming(RenderTiming* timing);
// Event IDs: pass * 2 + 1 begins, pass * 2 + 2 ends (0 <= pass < 128).
RTXR void* __cdecl RTX_GetGpuPassEvent();
// Try-copy and clear completed observations. Returns 0 if the snapshot is busy.
RTXR int __cdecl RTX_ReadGpuPassTimings(GpuPassTiming* timings, int capacity);
// Atomic switch for periodic diagnostic breadcrumbs. Error/state logs remain on.
RTXR void __cdecl RTX_SetDiagnosticsEnabled(int enabled);
// Try-copy immutable snapshots. No runtime calls, GPU work or waits.
// Touch: 0 = unavailable/busy; serial matches RTX_Begin.
RTXR int __cdecl RTX_GetTouch(TouchFrame* touch);
// Panel: 1 = valid, 0 = invalid, -1 = snapshot mutex busy (or null output).
// Busy/invalid calls leave the output neutral; callers may distinguish them.
RTXR int __cdecl RTX_GetFlatPanel(FlatPanel* panel);
RTXR void __cdecl RTX_GetSize(int* width, int* height);
RTXR void __cdecl RTX_GetResolutionInfo(int* recommendedWidth, int* recommendedHeight, float* scale);
// Between complete frame pairs only. 0 means CPU/GPU work is still pending;
// poll on later updates. -1 keeps the previous pair. Neither call waits for GPU.
RTXR int __cdecl RTX_PreviewSize(float scale, int* width, int* height);
RTXR int __cdecl RTX_TryResize(float scale);
RTXR int __cdecl RTX_GetError(char* text, int capacity);
RTXR int __cdecl RTX_TestBlitter(void* testDevice);
RTXR int __cdecl RTX_TestGpuPassTimings(void* testDevice);
RTXR int __cdecl RTX_TestTouch();
RTXR int __cdecl RTX_TestFlatHands();
