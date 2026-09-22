#pragma once
#include <stdint.h>
#if defined(RTNeural_EXPORTS)
#define RTN_API extern "C" __declspec(dllexport)
#else
#define RTN_API extern "C" __declspec(dllimport)
#endif

// Windows x64 ABI v1. C# uses LayoutKind.Sequential, Pack=8, pointer fields IntPtr.
// All calls are CPU-only except the returned Unity render-event callback.
enum : uint32_t { RTN_ABI_VERSION=1, RTN_EVENT_EVALUATE=1, RTN_EVENT_MAINTENANCE=2 };
enum : uint32_t { RTN_MODE_DLAA=0, RTN_MODE_QUALITY=1, RTN_MODE_BALANCED=2, RTN_MODE_PERFORMANCE=3, RTN_MODE_ULTRA_PERFORMANCE=4 };
enum : uint32_t { RTN_FEATURE_HDR=1, RTN_FEATURE_DEPTH_INVERTED=2, RTN_FEATURE_MV_LOW_RES=4, RTN_FEATURE_MV_JITTERED=8, RTN_FEATURE_AUTO_EXPOSURE=16, RTN_FEATURE_FORCE_PRESET_K=32, RTN_FEATURE_COLOR_SRGB=64 };
enum : uint32_t { RTN_JOB_RESET=1, RTN_JOB_PRESENT=2 }; // No PRESENT bit means shadow evaluation.
enum : uint32_t { RTN_STATE_IDLE=0, RTN_STATE_CONFIGURED=1, RTN_STATE_READY=2, RTN_STATE_FAULTED=3, RTN_STATE_STOPPING=4 };
enum : uint32_t { RTN_JOB_QUEUED=1, RTN_JOB_SHADOW=2, RTN_JOB_PRESENTED=3, RTN_JOB_FALLBACK=4, RTN_JOB_CANCELLED=5 };
enum : uint32_t { RTN_REASON_NONE=0, RTN_REASON_ABI=1, RTN_REASON_CONFIG=2, RTN_REASON_DUPLICATE=3, RTN_REASON_QUEUE_FULL=4,
 RTN_REASON_RESOURCE=5, RTN_REASON_UNSUPPORTED=6, RTN_REASON_RUNTIME=7, RTN_REASON_INIT=8, RTN_REASON_CREATE=9,
 RTN_REASON_EVALUATE=10, RTN_REASON_DEVICE_LOST=11, RTN_REASON_CANCELLED=12, RTN_REASON_STALE=13 };

#pragma pack(push,8)
struct RTN_Config {
 uint32_t size, abi;
 uint64_t generation; // Monotonically increasing, nonzero; changed configuration retires old features.
 uint32_t mode, featureFlags;
 const wchar_t* featureDirectory; // Copied absolute search-directory hint. NGX may select an NVIDIA override.
 const wchar_t* logPath; // Copied by Configure; can be null.
};
struct RTN_Job {
 uint32_t size, abi;
 uint64_t generation, frame, cameraId;
 uint32_t eye, flags; // eye 0/1. One evaluation per eye/frame/generation.
 void* color; void* depth; void* motion; void* destination; void* fallback; // ID3D11Texture2D*
 uint32_t renderWidth, renderHeight, outputWidth, outputHeight;
 uint32_t colorX, colorY, depthX, depthY, motionX, motionY, outputX, outputY;
 float jitterX, jitterY, mvScaleX, mvScaleY; // Jitter in render pixels; MV scale converts sampled vectors to pixels.
 float preExposure, exposureScale, frameTimeMs, sharpness; // sharpness 0..1, bounded linear output filter.
};
struct RTN_JobStatus {
 uint32_t size, abi;
 uint64_t ticket, generation, frame;
 uint32_t eye, state, reason, ngxResult;
 double cpuMs;
};
struct RTN_BackendStatus {
 uint32_t size, abi, state, reason;
 uint64_t generation, lastFailureFrame, lastLeftFrame, lastRightFrame;
 uint32_t pendingJobs, retiredBatches;
 uint32_t runtimeMajor, runtimeMinor, runtimePatch, runtimeBuild;
};
// Requested hint is not proof of the effective neural model. UINT32_MAX = not identified.
struct RTN_PresetStatus { uint32_t size,abi; uint64_t generation; uint32_t requestedPreset,identifiedLeft,identifiedRight,evidence; }; // evidence 1 = synchronous runtime creation log for both eyes; 0 = not identified.
// Advisory identity, never an eligibility gate. 310.9.1.0 is the tested build,
// not a version requirement. No native module is unloaded by this diagnostic.
enum : uint32_t { RTN_IDENTITY_UNKNOWN=0, RTN_IDENTITY_LOG=1, RTN_IDENTITY_MODULE=2 };
enum : uint32_t { RTN_SOURCE_UNKNOWN=0, RTN_SOURCE_CONFIGURED=1, RTN_SOURCE_EXTERNAL=2, RTN_SOURCE_OVERRIDE=3 };
enum : uint32_t { RTN_RUNTIME_VERSION_KNOWN=1, RTN_RUNTIME_TESTED=2, RTN_RUNTIME_PRELOADED=4, RTN_RUNTIME_OVERRIDE_OBSERVED=8, RTN_RUNTIME_PATH_TRUNCATED=16 };
struct RTN_RuntimeStatus {
 uint32_t size,abi; uint64_t generation;
 uint32_t identity,source,flags,lastErrorReason;
 uint32_t versionMajor,versionMinor,versionPatch,versionBuild;
 wchar_t path[1024],message[256];
};
#pragma pack(pop)
static_assert(sizeof(RTN_Config)==40,"RTN_Config ABI");
static_assert(sizeof(RTN_Job)==160,"RTN_Job ABI");
static_assert(sizeof(RTN_JobStatus)==56,"RTN_JobStatus ABI");
static_assert(sizeof(RTN_BackendStatus)==72,"RTN_BackendStatus ABI");
static_assert(sizeof(RTN_PresetStatus)==32,"RTN_PresetStatus ABI");
static_assert(sizeof(RTN_RuntimeStatus)==2608,"RTN_RuntimeStatus ABI");

RTN_API int __cdecl RTN_Configure(const RTN_Config* config);
// Copies data and AddRefs resources before returning 1. Never evaluates or initializes NGX here.
// destination MUST already receive the current raw image before the event; fallback has no AA.
// NGX writes private scratch, never destination; only a validated successful result may replace it.
// On rejection (0), keep the current raw image and do not issue an event. Outputs are zeroed on rejection.
RTN_API int __cdecl RTN_SubmitJob(const RTN_Job* job, void** eventData, uint64_t* ticket);
// UnityRenderingEventAndData: void __stdcall callback(int eventId, void* eventData).
// eventData is an opaque ticket value, NOT a dereferenceable/GC-owned pointer. It must not be reused.
RTN_API void* __cdecl RTN_GetRenderEventAndData();
// Only before its event has been enqueued (e.g. IssuePluginEventAndData threw).
RTN_API int __cdecl RTN_CancelJob(uint64_t ticket);
RTN_API int __cdecl RTN_GetJobStatus(uint64_t ticket, RTN_JobStatus* status);
RTN_API int __cdecl RTN_GetBackendStatus(RTN_BackendStatus* status);
RTN_API int __cdecl RTN_GetPresetStatus(RTN_PresetStatus* status);
// Copies the cached record. Does not enumerate modules, touch the GPU or NGX.
RTN_API int __cdecl RTN_GetRuntimeStatus(RTN_RuntimeStatus* status);
// Requests deferred cleanup. Keep issuing MAINTENANCE events until state IDLE; never unload pending code.
RTN_API void __cdecl RTN_RequestShutdown();
// Controls our logging/timing only; functional errors and runtime identity remain queryable.
RTN_API void __cdecl RTN_SetDiagnosticsEnabled(int enabled);
