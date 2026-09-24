// Copyright (c) 2026 Proyecto personal Rogue Trader VR. MIT.
#include "Bridge.h"
#include "RuntimePolicy.h"
#include "ResolutionPolicy80.h"
#include <windows.h>
#include <d3d11_4.h>
#include <d3dcompiler.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <condition_variable>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <limits>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace {
std::mutex guard;
int requestedRuntime = 0;
std::condition_variable finished;
FILE* logfile = nullptr;
std::mutex logGuard;
std::wstring logPath;
bool ownLogfile = false;
std::atomic<bool> diagnosticsEnabled{false};
std::atomic<bool> logEnabled{false};
static_assert(std::atomic<bool>::is_always_lock_free, "Diagnostic switch must not acquire a render/runtime lock");
std::string lastError;
XrInstance instance = XR_NULL_HANDLE;
XrSession session = XR_NULL_HANDLE;
XrSpace localSpace = XR_NULL_HANDLE, viewSpace = XR_NULL_HANDLE;
XrSystemId systemId = XR_NULL_SYSTEM_ID;
XrSessionState state = XR_SESSION_STATE_UNKNOWN;
XrTime displayTime = 0;
bool running = false, begun = false, queued = false, valid = false;
bool panelAnchored = false;
std::atomic<bool> firstFocusedTracking{false};
std::atomic<uint64_t> trackingOriginRevision{0};
std::vector<XrTime> pendingLocalSpaceChanges;
bool fatal = false;
XrPosef panelPose{{0,0,0,1}, {0,0,-2}};
XrPosef panelHeadAnchor{};
float anchoredPanelDistance = 0;
std::array<XrView, 2> views{};
XrPosef head{};
int eyeWidth = 0, eyeHeight = 0;
int recommendedWidth = 0, recommendedHeight = 0;
int maximumWidth = 0, maximumHeight = 0;
int uiMaxLayers81=0,uiMaxWidth81=0,uiMaxHeight81=0;
float appliedRenderScale = 0;
int64_t colorFormat = DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
Stats stats{};
BeginTiming beginTiming{};
RenderTiming activeRenderTiming{}, renderTimingSnapshot{};
// Input snapshots never use the render mutex: it can be held by xrWaitFrame or
// xrEndFrame. Readers try-lock this tiny copy-only mutex and return neutral on
// contention, without waiting for the compositor, GPU or another Unity thread.
std::mutex snapshotGuard;
TouchFrame touchSnapshot{};
FlatPanel flatSnapshot{};
struct TouchActions {
    XrActionSet set = XR_NULL_HANDLE;
    XrAction aim = XR_NULL_HANDLE, grip = XR_NULL_HANDLE, trigger = XR_NULL_HANDLE,
        squeeze = XR_NULL_HANDLE, stick = XR_NULL_HANDLE, primary = XR_NULL_HANDLE,
        secondary = XR_NULL_HANDLE, menu = XR_NULL_HANDLE, stickClick = XR_NULL_HANDLE, thumbrest = XR_NULL_HANDLE;
    XrPath hands[2]{XR_NULL_PATH, XR_NULL_PATH};
    XrSpace aimSpaces[2]{XR_NULL_HANDLE, XR_NULL_HANDLE}, gripSpaces[2]{XR_NULL_HANDLE, XR_NULL_HANDLE};
    bool ready = false;
    XrResult result = XR_SUCCESS;
} touchActions;
struct TouchApi {
    PFN_xrSyncActions sync = xrSyncActions;
    PFN_xrGetActionStatePose pose = xrGetActionStatePose;
    PFN_xrGetActionStateFloat scalar = xrGetActionStateFloat;
    PFN_xrGetActionStateVector2f vector = xrGetActionStateVector2f;
    PFN_xrGetActionStateBoolean button = xrGetActionStateBoolean;
    PFN_xrLocateSpace locate = xrLocateSpace;
} touchApi;
unsigned touchErrorLogs = 0;
void PublishTouch(const TouchFrame& value) { std::lock_guard<std::mutex> lock(snapshotGuard); touchSnapshot = value; }
void PublishFlat(const FlatPanel& value) { std::lock_guard<std::mutex> lock(snapshotGuard); flatSnapshot = value; }
void ClearTouchSnapshot() {
    TouchFrame neutral{};
    neutral.ready = touchActions.ready ? 1 : 0;
    neutral.state = state; neutral.lastResult = touchActions.result;
    PublishTouch(neutral);
}
using TimingClock = std::chrono::steady_clock;
double MillisecondsSince(TimingClock::time_point start) {
    return std::chrono::duration<double, std::milli>(TimingClock::now() - start).count();
}
ComPtr<ID3D11Device> device;
ComPtr<ID3D11DeviceContext1> context;
ComPtr<ID3D11Multithread> multithread;
ComPtr<ID3DDeviceContextState> isolatedState;
ComPtr<ID3D11VertexShader> vertexShader;
ComPtr<ID3D11PixelShader> pixelShader;
ComPtr<ID3D11PixelShader> hudPixelShader;
ComPtr<ID3D11SamplerState> sampler;
ComPtr<ID3D11Buffer> constants;
ComPtr<ID3D11RasterizerState> rasterizer;
ComPtr<ID3D11Query> copyRetirement;
// A bounded, render-thread-only timestamp pool. At most one pass interval
// is selected per stereo frame by managed code (one disjoint query per frame).
// Queries are read on later XR
// submissions with DONOTFLUSH: an unavailable slot is skipped, never waited on.
constexpr int GpuPassCount = 128, GpuPassQuerySlots = 16;
struct GpuPassQuery {
    ComPtr<ID3D11Query> frequency, begin, end;
    int pass = -1;
    bool pending = false, open = false, invalid = false;
    uint64_t submitted = 0;
};
std::array<GpuPassQuery, GpuPassQuerySlots> gpuPassQueries;
std::array<GpuPassTiming, GpuPassCount> gpuPassPublished{};
std::array<GpuPassTiming, GpuPassCount> gpuPassUnpublished{};
std::mutex gpuPassSnapshotGuard;
uint64_t gpuPassSubmissions = 0;
int gpuPassCursor = 0;
bool gpuPassUnsupported = false;
bool gpuPassWork = false;

#include "MonitorTiming81.inc"
void ResetGpuPassQueries() {
    ResetMonitor81();
    for (auto& query : gpuPassQueries) query = {};
    gpuPassSubmissions = 0; gpuPassCursor = 0; gpuPassUnsupported = false;
    gpuPassWork = false;
    gpuPassUnpublished = {};
    std::lock_guard<std::mutex> lock(gpuPassSnapshotGuard);
    gpuPassPublished = {};
}
void PollGpuPassQueries() {
    if (!gpuPassWork) return;
    ++gpuPassSubmissions;
    bool pendingWork = false;
    for (auto& query : gpuPassQueries) {
        if (query.open) { pendingWork = true; continue; }
        if (!query.pending) continue;
        if (gpuPassSubmissions - query.submitted < 3) { pendingWork = true; continue; }
        D3D11_QUERY_DATA_TIMESTAMP_DISJOINT frequency{};
        UINT64 begin = 0, end = 0;
        const HRESULT f = context->GetData(query.frequency.Get(), &frequency, sizeof(frequency), D3D11_ASYNC_GETDATA_DONOTFLUSH);
        if (f == S_FALSE) { pendingWork = true; continue; }
        HRESULT b = S_OK, e = S_OK;
        if (SUCCEEDED(f) && !frequency.Disjoint && frequency.Frequency != 0) {
            b = context->GetData(query.begin.Get(), &begin, sizeof(begin), D3D11_ASYNC_GETDATA_DONOTFLUSH);
            e = context->GetData(query.end.Get(), &end, sizeof(end), D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if (b == S_FALSE || e == S_FALSE) { pendingWork = true; continue; }
        }
        auto& result = gpuPassUnpublished[query.pass];
        if (query.invalid || FAILED(f) || FAILED(b) || FAILED(e) || frequency.Disjoint || !frequency.Frequency || end < begin) ++result.rejected;
        else {
            const double ms = double(end - begin) * 1000.0 / double(frequency.Frequency);
            ++result.observations; result.sumMs += ms; result.peakMs = std::max(result.peakMs, ms);
        }
        query.pending = false; query.pass = -1;
    }
    // Publication never holds up the render thread; retain totals until the
    // next successful copy if the diagnostic writer is taking a snapshot.
    std::unique_lock<std::mutex> lock(gpuPassSnapshotGuard, std::try_to_lock);
    if (!lock.owns_lock()) return;
    for (int i = 0; i < GpuPassCount; ++i) {
        auto& dst = gpuPassPublished[i]; auto& src = gpuPassUnpublished[i];
        dst.observations += src.observations; dst.rejected += src.rejected;
        dst.sumMs += src.sumMs; dst.peakMs = std::max(dst.peakMs, src.peakMs); src = {};
    }
    gpuPassWork = pendingWork;
}
void __stdcall GpuPassEvent(int eventId) {
    if (eventId < 1 || eventId > GpuPassCount * 2) return;
    // RTX_Begin can hold guard across xrWaitFrame. Profiling must never add a
    // compositor wait to a rendering pass, so drop this observation if busy.
    std::unique_lock<std::mutex> lock(guard, std::try_to_lock);
    if (!lock.owns_lock() || !device || !context || gpuPassUnsupported) return;
    const int pass = (eventId - 1) / 2;
    const bool beginEvent = (eventId & 1) != 0;
    if (!beginEvent) {
        for (auto& query : gpuPassQueries) if (query.open && query.pass == pass) {
            context->End(query.end.Get()); context->End(query.frequency.Get());
            query.open = false; query.pending = true; query.submitted = gpuPassSubmissions; return;
        }
        return;
    }
    if (!diagnosticsEnabled.load(std::memory_order_relaxed)) return;
    gpuPassWork = true;
    // Recover a missing end event by closing and rejecting it. It must not be
    // interpreted as a later invocation's timing or leave a disjoint query open.
    for (auto& query : gpuPassQueries) if (query.open) {
        context->End(query.end.Get()); context->End(query.frequency.Get());
        query.open = false; query.pending = true; query.submitted = gpuPassSubmissions;
        query.invalid = true;
    }
    for (int n = 0; n < GpuPassQuerySlots; ++n) {
        auto& query = gpuPassQueries[gpuPassCursor]; gpuPassCursor = (gpuPassCursor + 1) % GpuPassQuerySlots;
        if (query.pending || query.open) continue;
        if (!query.frequency) {
            D3D11_QUERY_DESC f{D3D11_QUERY_TIMESTAMP_DISJOINT, 0}, t{D3D11_QUERY_TIMESTAMP, 0};
            if (FAILED(device->CreateQuery(&f, &query.frequency)) || FAILED(device->CreateQuery(&t, &query.begin)) ||
                FAILED(device->CreateQuery(&t, &query.end))) { gpuPassUnsupported = true; ++gpuPassUnpublished[pass].rejected; return; }
        }
        query.pass = pass; query.open = true; query.invalid = false;
        context->Begin(query.frequency.Get()); context->End(query.begin.Get()); return;
    }
    ++gpuPassUnpublished[pass].rejected;
}
uint64_t copyGeneration = 0, retirementGeneration = 0;
struct Chain {
    XrSwapchain handle = XR_NULL_HANDLE;
    uint32_t width = 0, height = 0;
    std::vector<XrSwapchainImageD3D11KHR> images;
    std::vector<ComPtr<ID3D11RenderTargetView>> targets;
    uint32_t mipCount = 1;
    // Spatial UI alone carries filtered levels for the runtime's minification.
    // Each view exposes exactly one subresource, so a mip can read its parent
    // while writing the next level without a D3D read/write overlap.
    std::vector<std::vector<ComPtr<ID3D11RenderTargetView>>> mipTargets;
    std::vector<std::vector<ComPtr<ID3D11ShaderResourceView>>> mipSources;
    // One source per destination, held strongly to prevent pointer reuse. Eye
    // textures persist for thousands of frames; avoid rebuilding an SRV each copy.
    ComPtr<ID3D11Texture2D> cachedSource;
    ComPtr<ID3D11ShaderResourceView> cachedSourceView;
    ComPtr<ID3D11Texture2D> cachedWhiteSource;
    ComPtr<ID3D11ShaderResourceView> cachedWhiteView;
};
struct SwapchainApi {
    PFN_xrAcquireSwapchainImage acquire = xrAcquireSwapchainImage;
    PFN_xrWaitSwapchainImage wait = xrWaitSwapchainImage;
    PFN_xrReleaseSwapchainImage release = xrReleaseSwapchainImage;
} swapchainApi;
uint64_t blitFlushes = 0, sourceViewsCreated = 0;
Chain eyes[2], flatChain, flatHands[2], hudChain, spatialChain;
std::atomic<bool> spatialFailed{false};
bool flatHandsFailed = false;
std::atomic<bool> hudFailed{false};
std::atomic<float> requestedFlatWidthRatio{1.7f};
std::atomic<float> requestedFlatAspect{0};
std::atomic<float> requestedFlatOffsetX{0}, requestedFlatOffsetY{0};
static_assert(std::atomic<float>::is_always_lock_free, "Flat panel preference must not block the renderer");
struct Job {
    uint64_t serial = 0;
    ComPtr<ID3D11Texture2D> left, right, flat;
    Crop crops[2]{};
    bool flipEyes = true, flipFlat = false, recenter = false;
    float distance = 2;
    float flatWidthRatio = 1.7f;
    float flatAspect = 0;
    float flatOffsetX = 0, flatOffsetY = 0;
    uint64_t hudSerial = 0;
    ComPtr<ID3D11Texture2D> hudBlack, hudWhite;
    float hudWidth = 0, hudHeight = 0, hudDistance = 0, hudX = 0, hudY = 0;
    std::array<UiRegion81,6> uiRegions81{};
    int uiRegionCount81=0;
    bool hudLinear = true;
    uint64_t spatialSerial = 0;
    ComPtr<ID3D11Texture2D> spatialBlack, spatialWhite;
    XrPosef spatialPose{};
    float spatialWidth = 0, spatialHeight = 0;
    bool spatialLinear = true;
    bool spatialAtlas = false, spatialInfoVisible = false;
    XrPosef spatialInfoPose{};
    float spatialInfoSize = 0;
} job;

void Log(const std::string& message) {
    if (!logEnabled.load(std::memory_order_relaxed)) return;
    std::lock_guard<std::mutex> lock(logGuard);
    if (!logEnabled.load(std::memory_order_relaxed)) return;
    if (!logfile && !logPath.empty()) {
        _wfopen_s(&logfile, logPath.c_str(), L"a");
        ownLogfile = logfile != nullptr;
    }
    if (!logfile) return;
    SYSTEMTIME time{}; GetSystemTime(&time);
    fprintf(logfile, "%04d-%02d-%02dT%02d:%02d:%02d.%03dZ %s\n", time.wYear,
            time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond,
            time.wMilliseconds, message.c_str());
    fflush(logfile);
    // OFF never waits for a disk flush on the caller's thread. If it arrived
    // during this write, the writer closes its own stream before releasing it.
    if (!logEnabled.load(std::memory_order_relaxed) && ownLogfile) {
        fclose(logfile); logfile = nullptr; ownLogfile = false;
    }
}
void Trace(const char* phase) {
    if (!diagnosticsEnabled.load(std::memory_order_relaxed)) return;
    // Startup breadcrumbs identify the last GPU/OpenXR operation even if the
    // driver crashes asynchronously, before a C++ exception could be caught.
    if (stats.begun <= 12 || stats.begun % 300 == 0)
        Log("frame=" + std::to_string(stats.begun) + " thread=" +
            std::to_string(GetCurrentThreadId()) + " " + phase);
}
void Check(XrResult result, const char* operation) {
    if (XR_SUCCEEDED(result)) return;
    char name[XR_MAX_RESULT_STRING_SIZE]{};
    if (instance) xrResultToString(instance, result, name);
    throw std::runtime_error(std::string(operation) + ": " + name + " (" + std::to_string(result) + ")");
}
void Hr(HRESULT result, const char* operation) {
    if (FAILED(result)) throw std::runtime_error(std::string(operation) + " HRESULT=" + std::to_string(result));
}
int Error(const std::exception& exception) {
    lastError = exception.what(); Log("ERROR " + lastError); return -1;
}
// queued=false means CPU submission ended, not that the GPU finished using an
// OpenXR image. Only retirement paths issue this event; normal frames incur no
// query/Flush overhead. Individual context calls are already protected by
// ID3D11Multithread, and no D3D lock spans any OpenXR operation.
bool CopiesComplete(bool allowLostDevice = false) {
    if (!copyGeneration) return true;
    const HRESULT removed = device->GetDeviceRemovedReason();
    if (FAILED(removed)) {
        if (allowLostDevice) return true; // A removed device cannot execute old work.
        Hr(removed, "swapchain retirement device");
    }
    if (!copyRetirement || retirementGeneration != copyGeneration) {
        ComPtr<ID3D11Query> next;
        D3D11_QUERY_DESC desc{D3D11_QUERY_EVENT, 0};
        Hr(device->CreateQuery(&desc, &next), "swapchain retirement query");
        context->End(next.Get());
        context->Flush(); // Submit the event once; future polls never flush or wait.
        copyRetirement = std::move(next);
        retirementGeneration = copyGeneration;
        return false;
    }
    BOOL complete = FALSE;
    const HRESULT result = context->GetData(copyRetirement.Get(), &complete, sizeof(complete), D3D11_ASYNC_GETDATA_DONOTFLUSH);
    Hr(result, "swapchain retirement completion");
    return result == S_OK && complete != FALSE;
}
void DestroyChain(Chain& chain) {
    chain.mipSources.clear(); chain.mipTargets.clear();
    chain.targets.clear(); chain.images.clear();
    if (chain.handle) xrDestroySwapchain(chain.handle);
    chain = {};
}
void EndEmpty() {
    if (!begun) return;
    XrFrameEndInfo end{XR_TYPE_FRAME_END_INFO};
    end.displayTime = displayTime; end.environmentBlendMode = XR_ENVIRONMENT_BLEND_MODE_OPAQUE;
    const XrResult result = xrEndFrame(session, &end);
    begun = false; valid = false; ++stats.emptyFrames;
    PublishFlat({});
    Check(result, "xrEndFrame(empty)");
}
void DestroyTouchActions() {
    for (auto space : touchActions.aimSpaces) if (space) xrDestroySpace(space);
    for (auto space : touchActions.gripSpaces) if (space) xrDestroySpace(space);
    if (touchActions.set) xrDestroyActionSet(touchActions.set); // Also destroys its actions.
    touchActions = {};
    PublishTouch({});
}
void Destroy() {
    if (begun) { try { EndEmpty(); } catch (...) {} }
    job = {};
    DestroyChain(eyes[0]); DestroyChain(eyes[1]); DestroyChain(flatChain);
    DestroyChain(flatHands[0]); DestroyChain(flatHands[1]); flatHandsFailed = false;
    DestroyChain(hudChain); hudFailed = false;
    DestroyChain(spatialChain); spatialFailed = false;
    DestroyTouchActions(); PublishFlat({});
    { std::lock_guard<std::mutex> snapshotLock(snapshotGuard); renderTimingSnapshot = {}; }
    activeRenderTiming = {};
    if (viewSpace) xrDestroySpace(viewSpace);
    if (localSpace) xrDestroySpace(localSpace);
    if (session) xrDestroySession(session);
    if (instance) xrDestroyInstance(instance);
    session = XR_NULL_HANDLE; instance = XR_NULL_HANDLE;
    viewSpace = localSpace = XR_NULL_HANDLE; systemId = XR_NULL_SYSTEM_ID;
    running = begun = queued = valid = panelAnchored = fatal = false;
    firstFocusedTracking = false; trackingOriginRevision.store(0); pendingLocalSpaceChanges.clear();
    state = XR_SESSION_STATE_UNKNOWN;
    sampler.Reset(); vertexShader.Reset(); pixelShader.Reset(); hudPixelShader.Reset(); constants.Reset(); rasterizer.Reset();
    copyRetirement.Reset(); copyGeneration = retirementGeneration = 0;
    ResetGpuPassQueries();
    // Do not disable protection on the host device: runtime worker threads and
    // other plug-ins can still hold it after this session is destroyed.
    isolatedState.Reset(); multithread.Reset(); context.Reset(); device.Reset();
}

void ProtectDevice() {
    const UINT flags = device->GetCreationFlags();
    Log("D3D11 creation flags=" + std::to_string(flags));
    if (flags & D3D11_CREATE_DEVICE_SINGLETHREADED)
        throw std::runtime_error("Unity D3D11 device is single-threaded; cannot share it safely with VDXR");
    ComPtr<ID3D11DeviceContext> immediate;
    device->GetImmediateContext(&immediate);
    Hr(immediate.As(&context), "ID3D11DeviceContext1");
    Hr(immediate.As(&multithread), "ID3D11Multithread");
    const BOOL previous = multithread->SetMultithreadProtected(TRUE);
    if (!multithread->GetMultithreadProtected())
        throw std::runtime_error("D3D11 multithread protection could not be enabled");
    Log(std::string("D3D11 multithread protection ENABLED before xrCreateSession; previously ") +
        (previous ? "on" : "off"));
}
// The plug-in event runs on Unity's render thread. A D3D11.1 context state
// isolates ALL pipeline state, including shader class instances and UAV slots.
// Protect the complete save/draw/restore sequence, not just individual calls.
// Never hold this lock while waiting for OpenXR; a compositor worker may need it.
class ContextScope {
    ComPtr<ID3DDeviceContextState> previous;
public:
    ContextScope() {
        multithread->Enter();
        context->SwapDeviceContextState(isolatedState.Get(), &previous);
    }
    ~ContextScope() {
        context->SwapDeviceContextState(previous.Get(), nullptr);
        multithread->Leave();
    }
};
void CreateBlitter() {
    ComPtr<ID3D11Device1> newer;
    Hr(device.As(&newer), "ID3D11Device1");
    D3D_FEATURE_LEVEL feature = device->GetFeatureLevel(), selected{};
    Hr(newer->CreateDeviceContextState(0, &feature, 1, D3D11_SDK_VERSION,
        __uuidof(ID3D11Device), &selected, &isolatedState), "CreateDeviceContextState");
    const char* shader = R"(
cbuffer Params : register(b0) { float4 crop; float flip; float linearBlend; float sourceSrgb; float padding; };
Texture2D image : register(t0); SamplerState imageSampler : register(s0);
Texture2D whiteImage : register(t1);
struct Vertex { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
Vertex VS(uint index:SV_VertexID) {
    Vertex o; o.uv = float2((index << 1) & 2, index & 2);
    o.position = float4(o.uv.x * 2 - 1, 1 - o.uv.y * 2, 0, 1); return o;
}
float4 PS(Vertex input):SV_TARGET {
    float2 uv = lerp(crop.xy, crop.zw, input.uv);
    if (flip > 0.5) uv.y = 1 - uv.y;
    return image.Sample(imageSampler, uv);
}
float3 ToLinear(float3 v) { return lerp(v / 12.92, pow(max((v + .055) / 1.055, 0), 2.4), step(.04045, v)); }
float3 ToSrgb(float3 v) { return lerp(v * 12.92, 1.055 * pow(max(v, 0), 1.0 / 2.4) - .055, step(.0031308, v)); }
float4 HudPS(Vertex input):SV_TARGET {
    float2 uv = lerp(crop.xy, crop.zw, input.uv);
    if (flip > .5) uv.y = 1 - uv.y;
    float3 b = image.Sample(imageSampler, uv).rgb;
    float3 w = whiteImage.Sample(imageSampler, uv).rgb;
    // Recover coverage from RGB; the original UI shaders write alpha squared.
    if (linearBlend < .5 && sourceSrgb > .5) { b = ToSrgb(b); w = ToSrgb(w); }
    float3 difference = saturate(w - b);
    float a = saturate(1 - max(difference.r, max(difference.g, difference.b)));
    if (linearBlend < .5) b = a > .00001 ? ToLinear(saturate(b / a)) * a : ToLinear(b);
    return float4(b, a); // Premultiplied linear RGB for the sRGB destination.
})";
    ComPtr<ID3DBlob> vs, ps, messages;
    Hr(D3DCompile(shader, strlen(shader), "RTMaquetaBlit", nullptr, nullptr,
        "VS", "vs_5_0", D3DCOMPILE_ENABLE_STRICTNESS, 0, &vs, &messages), "compile VS");
    Hr(D3DCompile(shader, strlen(shader), "RTMaquetaBlit", nullptr, nullptr,
        "PS", "ps_5_0", D3DCOMPILE_ENABLE_STRICTNESS, 0, &ps, &messages), "compile PS");
    Hr(device->CreateVertexShader(vs->GetBufferPointer(), vs->GetBufferSize(), nullptr, &vertexShader), "CreateVertexShader");
    Hr(device->CreatePixelShader(ps->GetBufferPointer(), ps->GetBufferSize(), nullptr, &pixelShader), "CreatePixelShader");
    ComPtr<ID3DBlob> hudPs;
    Hr(D3DCompile(shader, strlen(shader), "RTMaquetaHud", nullptr, nullptr, "HudPS", "ps_5_0",
        D3DCOMPILE_ENABLE_STRICTNESS, 0, &hudPs, &messages), "compile HUD PS");
    Hr(device->CreatePixelShader(hudPs->GetBufferPointer(), hudPs->GetBufferSize(), nullptr, &hudPixelShader), "Create HUD PixelShader");
    D3D11_SAMPLER_DESC sd{}; sd.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sd.AddressU = sd.AddressV = sd.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP; sd.MaxLOD = D3D11_FLOAT32_MAX;
    Hr(device->CreateSamplerState(&sd, &sampler), "CreateSamplerState");
    D3D11_BUFFER_DESC bd{}; bd.ByteWidth = 32; bd.Usage = D3D11_USAGE_DEFAULT; bd.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    Hr(device->CreateBuffer(&bd, nullptr, &constants), "CreateBuffer");
    D3D11_RASTERIZER_DESC rd{}; rd.FillMode = D3D11_FILL_SOLID; rd.CullMode = D3D11_CULL_NONE; rd.DepthClipEnable = TRUE;
    Hr(device->CreateRasterizerState(&rd, &rasterizer), "CreateRasterizerState");
}
uint32_t SpatialMipCount(uint32_t width, uint32_t height) {
    uint32_t levels = 1;
    // Stop before the two atlas islands can collapse into a shared texel.
    while (levels < 6 && width >= 64 && height >= 64) {
        width >>= 1; height >>= 1; ++levels;
    }
    return levels;
}
void CreateChainMipViews(Chain& chain) {
    if (chain.mipCount <= 1) return;
    chain.mipTargets.resize(chain.images.size()); chain.mipSources.resize(chain.images.size());
    for (size_t image = 0; image < chain.images.size(); ++image) {
        D3D11_TEXTURE2D_DESC actual{}; chain.images[image].texture->GetDesc(&actual);
        if (actual.MipLevels < chain.mipCount || actual.SampleDesc.Count != 1)
            throw std::runtime_error("Spatial swapchain does not expose the requested resolved mip levels");
        auto& targets = chain.mipTargets[image]; auto& sources = chain.mipSources[image];
        targets.resize(chain.mipCount - 1); sources.resize(chain.mipCount - 1);
        for (uint32_t mip = 1; mip < chain.mipCount; ++mip) {
            D3D11_RENDER_TARGET_VIEW_DESC target{}; target.Format = static_cast<DXGI_FORMAT>(colorFormat);
            target.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D; target.Texture2D.MipSlice = mip;
            Hr(device->CreateRenderTargetView(chain.images[image].texture, &target, &targets[mip - 1]), "Create spatial mip RTV");
            D3D11_SHADER_RESOURCE_VIEW_DESC source{}; source.Format = target.Format;
            source.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
            source.Texture2D.MostDetailedMip = mip - 1; source.Texture2D.MipLevels = 1;
            Hr(device->CreateShaderResourceView(chain.images[image].texture, &source, &sources[mip - 1]), "Create spatial mip SRV");
        }
    }
}
void CreateChain(Chain& chain, uint32_t width, uint32_t height, bool spatialFiltering = false) {
    DestroyChain(chain);
    XrSwapchainCreateInfo info{XR_TYPE_SWAPCHAIN_CREATE_INFO};
    info.usageFlags = XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | XR_SWAPCHAIN_USAGE_SAMPLED_BIT;
    info.format = colorFormat; info.sampleCount = info.faceCount = info.arraySize = 1;
    info.mipCount = spatialFiltering ? SpatialMipCount(width, height) : 1;
    info.width = width; info.height = height;
    const XrResult created = xrCreateSwapchain(session, &info, &chain.handle);
    if (XR_FAILED(created) && spatialFiltering &&
        (created == XR_ERROR_FEATURE_UNSUPPORTED || created == XR_ERROR_VALIDATION_FAILURE ||
         created == XR_ERROR_SWAPCHAIN_FORMAT_UNSUPPORTED || created == XR_ERROR_RUNTIME_FAILURE)) {
        // Older runtimes may accept only one level. Keep the independent
        // original-resolution UI operational instead of disabling the wheel.
        Log("Spatial mip chain unsupported by runtime; keeping full-resolution base level");
        CreateChain(chain, width, height, false); return;
    }
    Check(created, "xrCreateSwapchain");
    chain.width = width; chain.height = height; chain.mipCount = info.mipCount;
    uint32_t count = 0;
    Check(xrEnumerateSwapchainImages(chain.handle, 0, &count, nullptr), "xrEnumerateSwapchainImages(count)");
    chain.images.resize(count, {XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR});
    Check(xrEnumerateSwapchainImages(chain.handle, count, &count,
        reinterpret_cast<XrSwapchainImageBaseHeader*>(chain.images.data())), "xrEnumerateSwapchainImages");
    chain.targets.resize(count);
    D3D11_RENDER_TARGET_VIEW_DESC vd{}; vd.Format = static_cast<DXGI_FORMAT>(colorFormat); vd.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D;
    for (uint32_t i=0; i<count; ++i)
        Hr(device->CreateRenderTargetView(chain.images[i].texture, &vd, &chain.targets[i]), "CreateRenderTargetView");
    if (spatialFiltering) {
        try { CreateChainMipViews(chain); }
        catch (const std::exception&) {
            Log("Spatial mip views unavailable; keeping full-resolution base level");
            CreateChain(chain, width, height, false);
        }
        Log("Spatial UI minification: " + std::to_string(chain.mipCount) + " mip levels; base resolution retained");
    }
}
uint32_t ScaledSize(uint32_t recommended, uint32_t maximum, float scale) {
    return ResolutionPolicy80::Scaled(recommended, maximum, scale);
}
DXGI_FORMAT SourceFormat(DXGI_FORMAT format) {
    switch (format) {
    case DXGI_FORMAT_R8G8B8A8_TYPELESS: return DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    case DXGI_FORMAT_B8G8R8A8_TYPELESS: return DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
    case DXGI_FORMAT_R16G16B16A16_TYPELESS: return DXGI_FORMAT_R16G16B16A16_FLOAT;
    default: return format;
    }
}
void DrawChainMips(Chain& chain, uint32_t image) {
    if (chain.mipCount <= 1) return;
    // The base UI has already reconstructed coverage in premultiplied linear
    // RGB. Filter THAT result: filtering black/white separately would mix
    // coverage with the backdrop and darken thin text/portrait borders.
    const float data[8] = {0, 0, 1, 1, 0, 1, 1, 0};
    context->UpdateSubresource(constants.Get(), 0, nullptr, data, 0, 0);
    context->PSSetShader(pixelShader.Get(), nullptr, 0);
    for (uint32_t mip = 1; mip < chain.mipCount; ++mip) {
        ID3D11ShaderResourceView* noSource = nullptr;
        context->PSSetShaderResources(0, 1, &noSource);
        ID3D11RenderTargetView* target = chain.mipTargets[image][mip - 1].Get();
        context->OMSetRenderTargets(1, &target, nullptr);
        D3D11_VIEWPORT viewport{0, 0, static_cast<float>(std::max(1u, chain.width >> mip)),
            static_cast<float>(std::max(1u, chain.height >> mip)), 0, 1};
        context->RSSetViewports(1, &viewport);
        ID3D11ShaderResourceView* source = chain.mipSources[image][mip - 1].Get();
        context->PSSetShaderResources(0, 1, &source);
        context->Draw(3, 0);
    }
    ID3D11ShaderResourceView* noSource = nullptr;
    context->PSSetShaderResources(0, 1, &noSource);
    context->OMSetRenderTargets(0, nullptr, nullptr);
}
void DrawTexture(Chain& chain, uint32_t index, ID3D11Texture2D* source, Crop crop, bool flip,
    ID3D11Texture2D* whiteSource = nullptr, bool linearBlend = true) {
        D3D11_TEXTURE2D_DESC desc{}; source->GetDesc(&desc);
        if (desc.SampleDesc.Count != 1) throw std::runtime_error("MSAA input is not supported");
        D3D11_SHADER_RESOURCE_VIEW_DESC sv{}; sv.Format = SourceFormat(desc.Format);
        sv.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D; sv.Texture2D.MipLevels = 1;
        if (chain.cachedSource.Get() != source || !chain.cachedSourceView) {
            ComPtr<ID3D11ShaderResourceView> nextView;
            Hr(device->CreateShaderResourceView(source, &sv, &nextView), "CreateShaderResourceView");
            chain.cachedSource = source; chain.cachedSourceView = std::move(nextView);
            ++sourceViewsCreated;
        }
        if (whiteSource && (chain.cachedWhiteSource.Get() != whiteSource || !chain.cachedWhiteView)) {
            D3D11_TEXTURE2D_DESC whiteDesc{}; whiteSource->GetDesc(&whiteDesc);
            if (whiteDesc.Width != desc.Width || whiteDesc.Height != desc.Height ||
                whiteDesc.SampleDesc.Count != 1 || SourceFormat(whiteDesc.Format) != sv.Format)
                throw std::runtime_error("HUD black/white surfaces must have the same resolved format and size");
            ComPtr<ID3D11ShaderResourceView> next;
            Hr(device->CreateShaderResourceView(whiteSource,&sv,&next),"Create HUD white source view");
            chain.cachedWhiteSource=whiteSource; chain.cachedWhiteView=std::move(next); ++sourceViewsCreated;
        }
        const bool sourceSrgb = sv.Format == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB || sv.Format == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
        const float data[8] = {crop.left, crop.top, crop.right, crop.bottom, flip ? 1.f : 0.f,
            linearBlend ? 1.f : 0.f, sourceSrgb ? 1.f : 0.f,0};
        context->UpdateSubresource(constants.Get(), 0, nullptr, data, 0, 0);
        ID3D11RenderTargetView* target = chain.targets[index].Get();
        context->OMSetRenderTargets(1, &target, nullptr);
        context->OMSetBlendState(nullptr, nullptr, 0xffffffff);
        context->OMSetDepthStencilState(nullptr, 0);
        context->RSSetState(rasterizer.Get());
        D3D11_VIEWPORT viewport{0,0,static_cast<float>(chain.width),static_cast<float>(chain.height),0,1};
        context->RSSetViewports(1, &viewport);
        context->IASetInputLayout(nullptr); context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(vertexShader.Get(), nullptr, 0);
        context->PSSetShader(whiteSource ? hudPixelShader.Get() : pixelShader.Get(), nullptr, 0);
        context->HSSetShader(nullptr, nullptr, 0); context->DSSetShader(nullptr, nullptr, 0); context->GSSetShader(nullptr, nullptr, 0);
        ID3D11ShaderResourceView* resources[2]{chain.cachedSourceView.Get(), whiteSource ? chain.cachedWhiteView.Get() : nullptr};
        auto* sampling = sampler.Get(); auto* buffer = constants.Get();
        context->PSSetShaderResources(0, 2, resources); context->PSSetSamplers(0, 1, &sampling);
        context->PSSetConstantBuffers(0, 1, &buffer);
        // Invalidate a completed retirement event before issuing another copy.
        // A timed-out/cancelled resize can never authorize a later destruction.
        ++copyGeneration;
        context->Draw(3, 0);
        resources[0] = resources[1] = nullptr; context->PSSetShaderResources(0, 2, resources);
        context->OMSetRenderTargets(0, nullptr, nullptr);
        DrawChainMips(chain, index);
}
struct BlitItem {
    Chain* chain; ID3D11Texture2D* source; Crop crop; bool flip;
    uint32_t index = 0; bool ready = false;
    ID3D11Texture2D* whiteSource = nullptr; bool linearBlend = true;
};
void BlitBatch(BlitItem* items, size_t count) {
    bool issued = false, flushed = false;
    const bool measure = activeRenderTiming.serial != 0 && diagnosticsEnabled.load(std::memory_order_relaxed);
    auto phaseStart = measure ? TimingClock::now() : TimingClock::time_point{};
    try {
        // Acquire and wait outside the D3D lock, before drawing either eye.
        // OpenXR worker threads can require the same immediate context.
        for (size_t i = 0; i < count; ++i) {
            auto& item = items[i];
            XrSwapchainImageAcquireInfo acquire{XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO};
            Check(swapchainApi.acquire(item.chain->handle, &acquire, &item.index), "xrAcquireSwapchainImage");
            XrSwapchainImageWaitInfo wait{XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO}; wait.timeout = 1000000000;
            const XrResult ready = swapchainApi.wait(item.chain->handle, &wait);
            if (ready == XR_TIMEOUT_EXPIRED) throw std::runtime_error("OpenXR swapchain wait timed out; session restart required");
            Check(ready, "xrWaitSwapchainImage"); item.ready = true;
        }
        if (measure) { activeRenderTiming.acquireWaitMs += MillisecondsSince(phaseStart); phaseStart=TimingClock::now(); }
        {
            ContextScope isolated;
            for (size_t i = 0; i < count; ++i) {
                auto& item = items[i]; issued = true;
                DrawTexture(*item.chain, item.index, item.source, item.crop, item.flip, item.whiteSource, item.linearBlend);
            }
        }
        // One submission after BOTH copies, while each image is still acquired.
        // This changes the copy transport only, never Waaagh's per-camera Submit.
        context->Flush(); ++blitFlushes; flushed = true;
        if (measure) { activeRenderTiming.drawFlushMs += MillisecondsSince(phaseStart); phaseStart=TimingClock::now(); }
        for (size_t i = 0; i < count; ++i) {
            XrSwapchainImageReleaseInfo release{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};
            auto& item = items[i];
            const auto result = swapchainApi.release(item.chain->handle, &release);
            item.ready = false; Check(result, "xrReleaseSwapchainImage");
        }
        if (measure) { activeRenderTiming.releaseMs += MillisecondsSince(phaseStart); activeRenderTiming.images += static_cast<int32_t>(count); }
    } catch (...) {
        // A failure after a partial draw must submit those writes before release.
        // A failed wait is not a releasable image: the caller aborts the session.
        if (issued && !flushed) { context->Flush(); ++blitFlushes; }
        for (size_t i = 0; i < count; ++i) if (items[i].ready) {
            XrSwapchainImageReleaseInfo release{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};
            swapchainApi.release(items[i].chain->handle, &release); items[i].ready = false;
        }
        throw;
    }
}
void Blit(Chain& chain, ID3D11Texture2D* source, Crop crop, bool flip) {
    BlitItem item{&chain, source, crop, flip}; BlitBatch(&item, 1);
}
void BlitPair(Chain& first, ID3D11Texture2D* firstSource, Crop firstCrop, bool firstFlip,
    Chain& second, ID3D11Texture2D* secondSource, Crop secondCrop, bool secondFlip) {
    BlitItem items[2]{{&first, firstSource, firstCrop, firstFlip}, {&second, secondSource, secondCrop, secondFlip}};
    BlitBatch(items, 2);
}
void BlitHud(Chain& chain, ID3D11Texture2D* black, ID3D11Texture2D* white, bool flip, bool linear) {
    if (!black || !white) throw std::runtime_error("HUD requires both completed background captures");
    BlitItem item{&chain,black,{0,0,1,1},flip}; item.whiteSource=white; item.linearBlend=linear;
    BlitBatch(&item,1);
}
XrSwapchainSubImage SubImage(const Chain& chain) {
    return {chain.handle, {{0,0},{static_cast<int32_t>(chain.width),static_cast<int32_t>(chain.height)}}, 0};
}
Pose Pack(const XrPosef& pose);
XrPosef OffsetPose(const XrPosef& source, float x, float y, float z) {
    const auto& q = source.orientation;
    XrPosef pose = source;
    pose.position.x += (1 - 2*(q.y*q.y + q.z*q.z))*x + 2*(q.x*q.y - q.w*q.z)*y + 2*(q.x*q.z + q.w*q.y)*z;
    pose.position.y += 2*(q.x*q.y + q.w*q.z)*x + (1 - 2*(q.x*q.x + q.z*q.z))*y + 2*(q.y*q.z - q.w*q.x)*z;
    pose.position.z += 2*(q.x*q.z - q.w*q.y)*x + 2*(q.y*q.z + q.w*q.x)*y + (1 - 2*(q.x*q.x + q.y*q.y))*z;
    return pose;
}
// The original desktop quad remains the exact interaction plane. Optional hand
// geometry is a second stereo layer; it never becomes a gameplay stereo pair.
struct FrameLayers {
    XrCompositionLayerQuad flat{XR_TYPE_COMPOSITION_LAYER_QUAD};
    XrCompositionLayerQuad hud{XR_TYPE_COMPOSITION_LAYER_QUAD};
    XrCompositionLayerQuad spatial{XR_TYPE_COMPOSITION_LAYER_QUAD};
    XrCompositionLayerQuad information{XR_TYPE_COMPOSITION_LAYER_QUAD};
    XrCompositionLayerProjection projection{XR_TYPE_COMPOSITION_LAYER_PROJECTION};
    XrCompositionLayerProjectionView projected[2]{{XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW},{XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW}};
    std::array<XrCompositionLayerQuad,6> uiQuads81{};
    const XrCompositionLayerBaseHeader* layers[11]{};
    uint32_t count = 0;
    bool hasFlat = false;
    void AddUiRegions81(const Chain& chain,XrSpace space,const XrPosef& headPose,const UiRegion81* regions,int number) {
        if(!regions||number<1||number>6||count+number>11)throw std::runtime_error("UI atlas layer budget exceeded");
        for(int i=0;i<number;++i) {
            const auto& r=regions[i];auto& quad=uiQuads81[i];
            if(r.x<0||r.y<0||r.pixelsWide<=0||r.pixelsHigh<=0||uint64_t(r.x)+r.pixelsWide>chain.width||uint64_t(r.y)+r.pixelsHigh>chain.height)
                throw std::runtime_error("UI atlas region outside texture");
            if(!std::isfinite(r.width)||!std::isfinite(r.height)||!std::isfinite(r.distance)||!std::isfinite(r.offsetX)||!std::isfinite(r.offsetY)||r.width<=0||r.height<=0||r.distance<=0)
                throw std::runtime_error("UI atlas invalid physical geometry");
            quad={XR_TYPE_COMPOSITION_LAYER_QUAD};quad.space=space;quad.eyeVisibility=XR_EYE_VISIBILITY_BOTH;
            quad.layerFlags=XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;quad.subImage=SubImage(chain);
            quad.subImage.imageRect={{r.x,r.y},{r.pixelsWide,r.pixelsHigh}};
            quad.pose=OffsetPose(headPose,r.offsetX,r.offsetY,-r.distance);quad.size={r.width,r.height};
            layers[count++]=reinterpret_cast<const XrCompositionLayerBaseHeader*>(&quad);
        }
    }
    void AddSpatial(const Chain& chain, XrSpace space, const XrPosef& pose, float width, float height) {
        spatial.space = space; spatial.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
        spatial.layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;
        spatial.subImage = SubImage(chain); spatial.pose = pose; spatial.size = {width, height};
        layers[count++] = reinterpret_cast<const XrCompositionLayerBaseHeader*>(&spatial);
    }
    void AddHud(const Chain& chain, XrSpace space, const XrPosef& headPose,
                float width, float height, float distance, float x, float y) {
        hud.space = space; hud.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
        hud.layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;
        hud.subImage = SubImage(chain); hud.pose = OffsetPose(headPose, x, y, -distance);
        hud.size = {width, height};
        layers[count++] = reinterpret_cast<const XrCompositionLayerBaseHeader*>(&hud);
    }
    void AddSpatialAtlas(const Chain& chain, XrSpace space, const XrPosef& wheelPose, float width, float height,
        bool showInfo, const XrPosef& infoPose, float infoSize) {
        AddSpatial(chain, space, wheelPose, width, height);
        // The black/white UI blit produces top-left oriented texture content:
        // original info occupies its top half, the physical wheel the bottom.
        spatial.subImage.imageRect.offset.y = static_cast<int32_t>(chain.height / 2);
        spatial.subImage.imageRect.extent.height = static_cast<int32_t>(chain.height / 2);
        if (!showInfo) return;
        information.space = space; information.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
        information.layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;
        information.subImage = SubImage(chain); information.subImage.imageRect.extent.height = static_cast<int32_t>(chain.height / 2);
        information.pose = infoPose; // Preserve rectangular atlas proportions; legacy square halves are unchanged.
        information.size = {infoSize, infoSize * height / width};
        layers[count++] = reinterpret_cast<const XrCompositionLayerBaseHeader*>(&information);
    }
    void AddFlat(const Chain& chain, XrSpace space, const XrPosef& pose, float distance, float widthRatio, float aspect = 0) {
        flat.space = space; flat.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
        flat.subImage = SubImage(chain); flat.pose = pose;
        flat.size.width = distance * widthRatio;
        flat.size.height = aspect > 0 ? flat.size.width / aspect : flat.size.width * chain.height / chain.width;
        layers[count++] = reinterpret_cast<const XrCompositionLayerBaseHeader*>(&flat);
        hasFlat = true;
    }
    void AddProjection(const Chain* chains, XrSpace space, const std::array<XrView, 2>& eyeViews) {
        for (int i = 0; i < 2; ++i) {
            projected[i].pose = eyeViews[i].pose; projected[i].fov = eyeViews[i].fov;
            projected[i].subImage = SubImage(chains[i]);
        }
        projection.space = space; projection.viewCount = 2; projection.views = projected;
        // Hand textures contain premultiplied RGB and alpha, including the
        // transparent clear. Source-alpha composition leaves the quad visible.
        projection.layerFlags = hasFlat ? XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT : 0;
        layers[count++] = reinterpret_cast<const XrCompositionLayerBaseHeader*>(&projection);
    }
    FlatPanel Panel(uint64_t serial, bool flip) const {
        FlatPanel shown{};
        if (hasFlat) {
            shown.serial = serial; shown.pose = Pack(flat.pose);
            shown.width = flat.size.width; shown.height = flat.size.height;
            shown.valid = 1; shown.flipVertical = flip ? 1 : 0;
        }
        return shown;
    }
};
bool PrepareHud(ID3D11Texture2D* black, ID3D11Texture2D* white) {
    if (hudFailed || !black || !white) return false;
    D3D11_TEXTURE2D_DESC b{}, w{}; black->GetDesc(&b); white->GetDesc(&w);
    if (b.Width < 256 || b.Height < 256 || b.Width > uint32_t(uiMaxWidth81>0?uiMaxWidth81:4096) || b.Height > uint32_t(uiMaxHeight81>0?uiMaxHeight81:4096) ||
        b.Width != w.Width || b.Height != w.Height || b.Format != w.Format ||
        b.SampleDesc.Count != 1 || w.SampleDesc.Count != 1)
        throw std::runtime_error("HUD background pair must have identical single-sample dimensions and format");
    if (hudChain.handle && hudChain.width == b.Width && hudChain.height == b.Height) return true;
    if (hudChain.handle && !CopiesComplete()) return false;
    CreateChain(hudChain, b.Width, b.Height); return true;
}
bool ValidFlatHandDimensions(const D3D11_TEXTURE2D_DESC& desc) {
    return desc.Width >= 16 && desc.Height >= 16 && desc.Width <= 4096 && desc.Height <= 4096 && desc.SampleDesc.Count == 1;
}
bool PrepareSpatial(ID3D11Texture2D* black, ID3D11Texture2D* white) {
    D3D11_TEXTURE2D_DESC b{}, w{}; black->GetDesc(&b); white->GetDesc(&w);
    if (!ValidFlatHandDimensions(b) || !ValidFlatHandDimensions(w) || b.Width != w.Width || b.Height != w.Height || b.Format != w.Format)
        throw std::runtime_error("Spatial UI requires a matching resolved background pair");
    if (spatialChain.handle && spatialChain.width == b.Width && spatialChain.height == b.Height) return true;
    if (spatialChain.handle && !CopiesComplete()) return false;
    CreateChain(spatialChain, b.Width, b.Height, true); return true;
}
bool PrepareFlatHands(ID3D11Texture2D* left, ID3D11Texture2D* right) {
    if (flatHandsFailed || !left || !right) return false;
    D3D11_TEXTURE2D_DESC desc[2]{}; left->GetDesc(&desc[0]); right->GetDesc(&desc[1]);
    for (const auto& item : desc)
        if (!ValidFlatHandDimensions(item))
            throw std::runtime_error("Invalid optional flat-hand texture dimensions or sample count");
    bool replace = false, haveChains = false;
    for (int i = 0; i < 2; ++i) {
        haveChains |= flatHands[i].handle != XR_NULL_HANDLE;
        replace |= !flatHands[i].handle || flatHands[i].width != desc[i].Width || flatHands[i].height != desc[i].Height;
    }
    if (!replace) return true;
    if (haveChains && !CopiesComplete()) return false;
    Chain replacement[2];
    try {
        CreateChain(replacement[0], desc[0].Width, desc[0].Height);
        CreateChain(replacement[1], desc[1].Width, desc[1].Height);
    } catch (...) { DestroyChain(replacement[0]); DestroyChain(replacement[1]); throw; }
    for (int i = 0; i < 2; ++i) { std::swap(flatHands[i], replacement[i]); DestroyChain(replacement[i]); }
    return true;
}
void Poll() {
    XrEventDataBuffer event{XR_TYPE_EVENT_DATA_BUFFER};
    XrResult result;
    while ((result = xrPollEvent(instance, &event)) == XR_SUCCESS) {
        if (event.type == XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED) {
            auto& change = reinterpret_cast<XrEventDataSessionStateChanged&>(event);
            state = change.state; stats.state = static_cast<int32_t>(state);
            Log("session state=" + std::to_string(state));
            if (state == XR_SESSION_STATE_READY && !running) {
                XrSessionBeginInfo info{XR_TYPE_SESSION_BEGIN_INFO};
                info.primaryViewConfigurationType = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
                Check(xrBeginSession(session, &info), "xrBeginSession"); running = true;
            } else if (state == XR_SESSION_STATE_STOPPING && running) {
                EndEmpty(); Check(xrEndSession(session), "xrEndSession"); running = false;
            } else if (state == XR_SESSION_STATE_EXITING || state == XR_SESSION_STATE_LOSS_PENDING) running = false;
        } else if (event.type == XR_TYPE_EVENT_DATA_INTERACTION_PROFILE_CHANGED && touchActions.set) {
            for (int hand80=0; hand80<2; ++hand80) {
                XrInteractionProfileState profile80{XR_TYPE_INTERACTION_PROFILE_STATE};
                if (XR_SUCCEEDED(xrGetCurrentInteractionProfile(session,touchActions.hands[hand80],&profile80))) {
                    char path80[XR_MAX_PATH_LENGTH]{}; uint32_t length80=0;
                    if (profile80.interactionProfile && XR_SUCCEEDED(xrPathToString(instance,profile80.interactionProfile,sizeof(path80),&length80,path80)))
                        Log(std::string("Controller profile ")+(hand80?"right: ":"left: ")+path80);
                }
            }
        } else if (event.type == XR_TYPE_EVENT_DATA_INSTANCE_LOSS_PENDING) {
            state = XR_SESSION_STATE_LOSS_PENDING; running = false;
        } else if (event.type == XR_TYPE_EVENT_DATA_REFERENCE_SPACE_CHANGE_PENDING) {
            const auto& change = reinterpret_cast<const XrEventDataReferenceSpaceChangePending&>(event);
            if (change.session == session && change.referenceSpaceType == XR_REFERENCE_SPACE_TYPE_LOCAL)
                pendingLocalSpaceChanges.push_back(change.changeTime);
            // This event is announced BEFORE the coordinate change. Reanchoring
            // here would capture the old origin and then strand the panel below
            // the player when the future changeTime is reached.
        }
        event = {XR_TYPE_EVENT_DATA_BUFFER};
    }
    if (result != XR_EVENT_UNAVAILABLE) Check(result, "xrPollEvent");
}
Pose Pack(const XrPosef& pose) {
    return {pose.orientation.x,pose.orientation.y,pose.orientation.z,pose.orientation.w,pose.position.x,pose.position.y,pose.position.z};
}
View Pack(const XrView& view) { return {Pack(view.pose), {view.fov.angleLeft,view.fov.angleRight,view.fov.angleUp,view.fov.angleDown}}; }
XrPath TouchPath(const char* path) {
    XrPath result = XR_NULL_PATH; Check(xrStringToPath(instance, path, &result), "Touch xrStringToPath"); return result;
}
void CreateTouchAction(XrAction& action, const char* name, const char* localized, XrActionType type, bool leftOnly = false) {
    XrActionCreateInfo info{XR_TYPE_ACTION_CREATE_INFO};
    strcpy_s(info.actionName, name); strcpy_s(info.localizedActionName, localized);
    info.actionType = type; info.countSubactionPaths = leftOnly ? 1 : 2; info.subactionPaths = touchActions.hands;
    Check(xrCreateAction(touchActions.set, &info, &action), "Touch xrCreateAction");
}
void CreateTouchActions() {
    try {
        touchActions.hands[0] = TouchPath("/user/hand/left");
        touchActions.hands[1] = TouchPath("/user/hand/right");
        XrActionSetCreateInfo set{XR_TYPE_ACTION_SET_CREATE_INFO};
        strcpy_s(set.actionSetName, "rtmaqueta_touch"); strcpy_s(set.localizedActionSetName, "RT Maqueta Touch");
        set.priority = 0;
        Check(xrCreateActionSet(instance, &set, &touchActions.set), "Touch xrCreateActionSet");
        CreateTouchAction(touchActions.aim, "aim_pose", "Aim pose", XR_ACTION_TYPE_POSE_INPUT);
        CreateTouchAction(touchActions.grip, "grip_pose", "Grip pose", XR_ACTION_TYPE_POSE_INPUT);
        CreateTouchAction(touchActions.trigger, "trigger", "Trigger", XR_ACTION_TYPE_FLOAT_INPUT);
        CreateTouchAction(touchActions.squeeze, "squeeze", "Grip squeeze", XR_ACTION_TYPE_FLOAT_INPUT);
        CreateTouchAction(touchActions.stick, "stick", "Thumbstick", XR_ACTION_TYPE_VECTOR2F_INPUT);
        CreateTouchAction(touchActions.primary, "primary", "Primary X or A", XR_ACTION_TYPE_BOOLEAN_INPUT);
        CreateTouchAction(touchActions.secondary, "secondary", "Secondary Y or B", XR_ACTION_TYPE_BOOLEAN_INPUT);
        CreateTouchAction(touchActions.menu, "menu", "Left menu", XR_ACTION_TYPE_BOOLEAN_INPUT, true);
        CreateTouchAction(touchActions.stickClick, "stick_click", "Thumbstick click", XR_ACTION_TYPE_BOOLEAN_INPUT);
        CreateTouchAction(touchActions.thumbrest, "thumbrest_touch", "Left thumbrest touch", XR_ACTION_TYPE_BOOLEAN_INPUT, true);
        std::vector<XrActionSuggestedBinding> bindings = {
            {touchActions.aim, TouchPath("/user/hand/left/input/aim/pose")},
            {touchActions.aim, TouchPath("/user/hand/right/input/aim/pose")},
            {touchActions.grip, TouchPath("/user/hand/left/input/grip/pose")},
            {touchActions.grip, TouchPath("/user/hand/right/input/grip/pose")},
            {touchActions.trigger, TouchPath("/user/hand/left/input/trigger/value")},
            {touchActions.trigger, TouchPath("/user/hand/right/input/trigger/value")},
            {touchActions.squeeze, TouchPath("/user/hand/left/input/squeeze/value")},
            {touchActions.squeeze, TouchPath("/user/hand/right/input/squeeze/value")},
            {touchActions.stick, TouchPath("/user/hand/left/input/thumbstick")},
            {touchActions.stick, TouchPath("/user/hand/right/input/thumbstick")},
            {touchActions.primary, TouchPath("/user/hand/left/input/x/click")},
            {touchActions.primary, TouchPath("/user/hand/right/input/a/click")},
            {touchActions.secondary, TouchPath("/user/hand/left/input/y/click")},
            {touchActions.secondary, TouchPath("/user/hand/right/input/b/click")},
            {touchActions.menu, TouchPath("/user/hand/left/input/menu/click")},
            {touchActions.stickClick, TouchPath("/user/hand/left/input/thumbstick/click")},
            {touchActions.stickClick, TouchPath("/user/hand/right/input/thumbstick/click")},
            {touchActions.thumbrest, TouchPath("/user/hand/left/input/thumbrest/touch")}
        };
        XrInteractionProfileSuggestedBinding suggest{XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING};
        suggest.interactionProfile = TouchPath("/interaction_profiles/oculus/touch_controller");
        suggest.countSuggestedBindings = static_cast<uint32_t>(bindings.size()); suggest.suggestedBindings = bindings.data();
        XrResult touchResult80 = xrSuggestInteractionProfileBindings(instance, &suggest);
        if (XR_FAILED(touchResult80)) {
            // Thumbrest capacitive input is optional, never a prerequisite for
            // movement/selection on controllers that emulate Touch.
            bindings.pop_back(); suggest.countSuggestedBindings = static_cast<uint32_t>(bindings.size());
            touchResult80 = xrSuggestInteractionProfileBindings(instance, &suggest);
        }
        std::vector<XrActionSuggestedBinding> indexBindings80;
        for (const char* hand : {"/user/hand/left", "/user/hand/right"}) {
            const std::string base(hand);
            for (const auto& item : std::vector<std::pair<XrAction, const char*>>{
                {touchActions.aim,"/input/aim/pose"},{touchActions.grip,"/input/grip/pose"},
                {touchActions.trigger,"/input/trigger/value"},{touchActions.squeeze,"/input/squeeze/value"},
                {touchActions.stick,"/input/thumbstick"},{touchActions.stickClick,"/input/thumbstick/click"},
                {touchActions.primary,"/input/a/click"},{touchActions.secondary,"/input/b/click"}})
                indexBindings80.push_back({item.first,TouchPath((base+item.second).c_str())});
        }
        XrInteractionProfileSuggestedBinding indexSuggest80{XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING};
        indexSuggest80.interactionProfile=TouchPath("/interaction_profiles/valve/index_controller");
        indexSuggest80.countSuggestedBindings=static_cast<uint32_t>(indexBindings80.size());indexSuggest80.suggestedBindings=indexBindings80.data();
        const XrResult indexResult80=xrSuggestInteractionProfileBindings(instance,&indexSuggest80);
        if(XR_FAILED(touchResult80)&&XR_FAILED(indexResult80))Check(touchResult80,"No supported Touch/Index action bindings");
        for (int i = 0; i < 2; ++i) {
            XrActionSpaceCreateInfo space{XR_TYPE_ACTION_SPACE_CREATE_INFO};
            space.subactionPath = touchActions.hands[i]; space.poseInActionSpace.orientation.w = 1;
            space.action = touchActions.aim;
            Check(xrCreateActionSpace(session, &space, &touchActions.aimSpaces[i]), "Touch aim action space");
            space.action = touchActions.grip;
            Check(xrCreateActionSpace(session, &space, &touchActions.gripSpaces[i]), "Touch grip action space");
        }
        XrSessionActionSetsAttachInfo attach{XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO};
        attach.countActionSets = 1; attach.actionSets = &touchActions.set;
        Check(xrAttachSessionActionSets(session, &attach), "Touch xrAttachSessionActionSets");
        touchActions.ready = true; touchActions.result = XR_SUCCESS;
        ClearTouchSnapshot();
        Log("[touch] Touch/Index suggestions attached before xrBeginSession; core pose, trigger/grip, sticks and face buttons; capacitive input optional; no system-menu binding for Index");
    } catch (const std::exception& error) {
        // A controller binding failure must not break already working stereo or
        // mouse/gamepad play. A later session recreates the complete action set.
        DestroyTouchActions(); touchActions.result = XR_ERROR_INITIALIZATION_FAILED;
        ClearTouchSnapshot(); Log(std::string("[touch] Input unavailable; stereo retained: ") + error.what());
    }
}
bool FiniteTouchPose(const XrPosef& pose) {
    const auto& q = pose.orientation; const auto& p = pose.position;
    const float norm = q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w;
    return std::isfinite(q.x) && std::isfinite(q.y) && std::isfinite(q.z) && std::isfinite(q.w) &&
        std::isfinite(p.x) && std::isfinite(p.y) && std::isfinite(p.z) && std::isfinite(norm) && norm > .5f && norm < 1.5f;
}
XrResult ReadTouchPose(XrAction action, XrSpace space, XrPath hand, XrTime time, TouchHand& output,
    uint32_t control, Pose& pose, uint32_t& flags) {
    XrActionStateGetInfo info{XR_TYPE_ACTION_STATE_GET_INFO}; info.action = action; info.subactionPath = hand;
    XrActionStatePose active{XR_TYPE_ACTION_STATE_POSE};
    XrResult result = touchApi.pose(session, &info, &active);
    if (result != XR_SUCCESS || !active.isActive) return result;
    output.activeControls |= control;
    XrSpaceLocation location{XR_TYPE_SPACE_LOCATION};
    result = touchApi.locate(space, localSpace, time, &location);
    if (result != XR_SUCCESS) return result;
    flags = static_cast<uint32_t>(location.locationFlags & 15u);
    if ((flags & 3u) == 3u && FiniteTouchPose(location.pose)) pose = Pack(location.pose);
    else flags &= ~3u; // An invalid pose never carries last frame's coordinates.
    return XR_SUCCESS;
}
XrResult ReadTouchFloat(XrAction action, XrPath hand, TouchHand& output, uint32_t control, float& value) {
    XrActionStateGetInfo info{XR_TYPE_ACTION_STATE_GET_INFO}; info.action = action; info.subactionPath = hand;
    XrActionStateFloat stateValue{XR_TYPE_ACTION_STATE_FLOAT};
    const XrResult result = touchApi.scalar(session, &info, &stateValue);
    if (result == XR_SUCCESS && stateValue.isActive && std::isfinite(stateValue.currentState)) {
        output.activeControls |= control; value = std::clamp(stateValue.currentState, 0.f, 1.f);
    }
    return result;
}
XrResult ReadTouchButton(XrAction action, XrPath hand, TouchHand& output, uint32_t control, uint32_t button) {
    XrActionStateGetInfo info{XR_TYPE_ACTION_STATE_GET_INFO}; info.action = action; info.subactionPath = hand;
    XrActionStateBoolean stateValue{XR_TYPE_ACTION_STATE_BOOLEAN};
    const XrResult result = touchApi.button(session, &info, &stateValue);
    if (result == XR_SUCCESS && stateValue.isActive) {
        output.activeControls |= control;
        if (stateValue.currentState) output.buttons |= button;
    }
    return result;
}
XrResult ReadTouchHand(int index, XrTime time, TouchHand& output) {
    const XrPath hand = touchActions.hands[index]; XrResult result;
    if ((result = ReadTouchPose(touchActions.aim, touchActions.aimSpaces[index], hand, time, output, TouchAim, output.aim, output.aimFlags)) != XR_SUCCESS) return result;
    if ((result = ReadTouchPose(touchActions.grip, touchActions.gripSpaces[index], hand, time, output, TouchGrip, output.grip, output.gripFlags)) != XR_SUCCESS) return result;
    if ((output.aimFlags & 3u) != 3u && (output.gripFlags & 3u) != 3u) { output = {}; return XR_SUCCESS; }
    if ((result = ReadTouchFloat(touchActions.trigger, hand, output, TouchTrigger, output.trigger)) != XR_SUCCESS) return result;
    if ((result = ReadTouchFloat(touchActions.squeeze, hand, output, TouchSqueeze, output.squeeze)) != XR_SUCCESS) return result;
    XrActionStateGetInfo info{XR_TYPE_ACTION_STATE_GET_INFO}; info.action = touchActions.stick; info.subactionPath = hand;
    XrActionStateVector2f vector{XR_TYPE_ACTION_STATE_VECTOR2F};
    if ((result = touchApi.vector(session, &info, &vector)) != XR_SUCCESS) return result;
    if (vector.isActive && std::isfinite(vector.currentState.x) && std::isfinite(vector.currentState.y)) {
        output.activeControls |= TouchStick;
        output.stickX = std::clamp(vector.currentState.x, -1.f, 1.f); output.stickY = std::clamp(vector.currentState.y, -1.f, 1.f);
    }
    if ((result = ReadTouchButton(touchActions.primary, hand, output, TouchPrimary, TouchPrimaryButton)) != XR_SUCCESS) return result;
    if ((result = ReadTouchButton(touchActions.secondary, hand, output, TouchSecondary, TouchSecondaryButton)) != XR_SUCCESS) return result;
    if ((result = ReadTouchButton(touchActions.stickClick, hand, output, TouchStickClick, TouchStickButton)) != XR_SUCCESS) return result;
    if (index == 0) {
        if ((result = ReadTouchButton(touchActions.menu, hand, output, TouchMenu, TouchMenuButton)) != XR_SUCCESS) return result;
        return ReadTouchButton(touchActions.thumbrest, hand, output, TouchThumbrest, TouchThumbrestButton);
    }
    return XR_SUCCESS;
}
void UpdateTouchSnapshot(uint64_t serial, XrTime time, bool allowInput) {
    TouchFrame value{}; value.serial = serial; value.predictedDisplayTime = time;
    value.state = state; value.ready = touchActions.ready ? 1 : 0; value.lastResult = touchActions.result;
    if (!touchActions.ready || !running || state != XR_SESSION_STATE_FOCUSED || !allowInput) { PublishTouch(value); return; }
    XrActiveActionSet active{touchActions.set, XR_NULL_PATH};
    XrActionsSyncInfo sync{XR_TYPE_ACTIONS_SYNC_INFO}; sync.countActiveActionSets = 1; sync.activeActionSets = &active;
    XrResult result = touchApi.sync(session, &sync);
    // XR_SESSION_NOT_FOCUSED is positive, so XR_SUCCEEDED is insufficient here.
    if (result == XR_SUCCESS) {
        result = ReadTouchHand(0, time, value.left);
        if (result == XR_SUCCESS) result = ReadTouchHand(1, time, value.right);
    }
    value.lastResult = result;
    if (result == XR_SUCCESS) value.focused = 1;
    else {
        value.left = {}; value.right = {};
        if (XR_FAILED(result) && touchErrorLogs++ < 4) Log("[touch] Neutral input after action error=" + std::to_string(result));
    }
    PublishTouch(value);
}
// Test adapter exercises the production action-query and snapshot path without
// creating an OpenXR instance, session or game. Never installed in a live session.
struct TouchFixture {
    bool left = true, right = true, invalidPose = false, invalidValues = false;
    XrResult syncResult = XR_SUCCESS, readResult = XR_SUCCESS;
    unsigned syncCalls = 0, stateCalls = 0, locateCalls = 0;
    XrTime expectedTime = 987654321;
    bool wrongTime = false;
} touchFixture;
bool TestHandActive(XrPath path) { return path == 101 ? touchFixture.left : touchFixture.right; }
XrResult XRAPI_PTR TestTouchSync(XrSession, const XrActionsSyncInfo* info) {
    ++touchFixture.syncCalls;
    if (info->countActiveActionSets != 1 || info->activeActionSets[0].subactionPath != XR_NULL_PATH) return XR_ERROR_VALIDATION_FAILURE;
    return touchFixture.syncResult;
}
XrResult XRAPI_PTR TestTouchPose(XrSession, const XrActionStateGetInfo* info, XrActionStatePose* value) {
    ++touchFixture.stateCalls; value->isActive = TestHandActive(info->subactionPath); return touchFixture.readResult;
}
XrResult XRAPI_PTR TestTouchFloat(XrSession, const XrActionStateGetInfo* info, XrActionStateFloat* value) {
    ++touchFixture.stateCalls; value->isActive = TestHandActive(info->subactionPath);
    value->currentState = touchFixture.invalidValues ? std::numeric_limits<float>::quiet_NaN() : info->subactionPath == 101 ? .75f : .25f;
    return touchFixture.readResult;
}
XrResult XRAPI_PTR TestTouchVector(XrSession, const XrActionStateGetInfo* info, XrActionStateVector2f* value) {
    ++touchFixture.stateCalls; value->isActive = TestHandActive(info->subactionPath);
    value->currentState = touchFixture.invalidValues ? XrVector2f{std::numeric_limits<float>::infinity(), .3f} : XrVector2f{-.6f, .8f};
    return touchFixture.readResult;
}
XrResult XRAPI_PTR TestTouchBoolean(XrSession, const XrActionStateGetInfo* info, XrActionStateBoolean* value) {
    ++touchFixture.stateCalls; value->isActive = TestHandActive(info->subactionPath);
    value->currentState = XR_TRUE; return touchFixture.readResult;
}
XrResult XRAPI_PTR TestTouchLocate(XrSpace space, XrSpace, XrTime time, XrSpaceLocation* value) {
    ++touchFixture.locateCalls; touchFixture.wrongTime |= time != touchFixture.expectedTime;
    value->locationFlags = touchFixture.invalidPose ? 0 : 15;
    value->pose = {{0,0,0,1}, {space == touchActions.aimSpaces[0] || space == touchActions.gripSpaces[0] ? -.25f : .35f, 1.2f, -.5f}};
    return XR_SUCCESS;
}
void UpdatePanelAnchor(XrPosef& anchor, XrPosef& pose, bool& anchored, float& previousDistance,
                       const XrPosef& currentHead, float distance, bool recenter) {
    if (!anchored || recenter) {
        anchor = currentHead;
        if (!recenter) {
            // Initial/menu-transition placement is upright at the measured eye
            // height. A player looking down while putting on the headset must
            // not permanently place the first menu on the floor. Explicit
            // recenter still follows the player's intentional viewing direction.
            const auto& q = currentHead.orientation;
            const float yaw = std::atan2(2.f * (q.x*q.z + q.w*q.y), 1.f - 2.f * (q.x*q.x + q.y*q.y));
            anchor.orientation = {0, std::sin(yaw * .5f), 0, std::cos(yaw * .5f)};
        }
    }
    else if (distance == previousDistance) return;
    pose = anchor;
    // Rotate (0,0,-distance) by the normalized HMD quaternion.
    const auto& q = anchor.orientation;
    pose.position.x -= distance * 2 * (q.x*q.z + q.w*q.y);
    pose.position.y -= distance * 2 * (q.y*q.z - q.w*q.x);
    pose.position.z -= distance * (1 - 2*(q.x*q.x + q.y*q.y));
    anchored = true; previousDistance = distance;
}
bool AdvanceTrackingOrigin(XrTime time, bool focused, bool tracked) {
    bool changed = false;
    for (auto item = pendingLocalSpaceChanges.begin(); item != pendingLocalSpaceChanges.end();) {
        if (time >= *item) { changed = true; item = pendingLocalSpaceChanges.erase(item); }
        else ++item;
    }
    if (changed) {
        // A runtime recenter may occur while the headset is not focused. Its
        // next stable coordinate origin must be captured when tracking/focus
        // return, rather than latching another inferred floor-height pose.
        firstFocusedTracking = false;
        panelAnchored = false; PublishFlat({});
    }
    if (focused && tracked && !firstFocusedTracking) {
        firstFocusedTracking = true; changed = true;
        panelAnchored = false; PublishFlat({});
        trackingOriginRevision.fetch_add(1, std::memory_order_release);
    }
    return changed;
}
void __stdcall RenderEvent(int eventId) {
    if (eventId != 1) return;
    std::lock_guard<std::mutex> lock(guard);
    if (!queued) return;
    if (context) { PollGpuPassQueries(); PollMonitor81(); }
    const bool measure = diagnosticsEnabled.load();
    const auto renderStarted = measure ? TimingClock::now() : TimingClock::time_point{};
    const auto flushesBefore = blitFlushes, viewsBefore = sourceViewsCreated;
    activeRenderTiming = {}; activeRenderTiming.serial = measure ? job.serial : 0;
    try {
        // A changing desktop resolution also replaces a swapchain. Finish this
        // frame empty until its previous GPU writes retire; do not stall Unity.
        if (begun && valid && running && job.flat && flatChain.handle) {
            D3D11_TEXTURE2D_DESC desc{}; job.flat->GetDesc(&desc);
            if ((flatChain.width != desc.Width || flatChain.height != desc.Height) && !CopiesComplete())
                valid = false;
        }
        // Test the retirement fence BEFORE this frame's world copies. Testing
        // after BlitPair would keep moving the fence and could starve HUD resize.
        bool hudReady = false, flatHandsReady = false, spatialReady = false;
        if (begun && valid && running && !job.flat && job.spatialSerial == job.serial && job.spatialBlack && job.spatialWhite && !spatialFailed) {
            try { spatialReady = PrepareSpatial(job.spatialBlack.Get(), job.spatialWhite.Get()); }
            catch (const std::exception& error) {
                spatialFailed = true; Log(std::string("[ui/spatial] Optional layer preparation disabled: ") + error.what());
            }
        }
        if (begun && valid && running && !job.flat && job.hudSerial == job.serial && job.hudBlack && job.hudWhite && !hudFailed) {
            try {
                hudReady = PrepareHud(job.hudBlack.Get(), job.hudWhite.Get());
                if (!hudReady) valid = false;
            }
            catch (const std::exception& error) {
                hudFailed = true;
                Log(std::string("[ui/full-resolution] HUD resize disabled; managed fallback requested: ") + error.what());
            }
        }
        if (begun && valid && running && job.flat && job.left && job.right && !flatHandsFailed) {
            try {
                flatHandsReady = PrepareFlatHands(job.left.Get(), job.right.Get());
                if (!flatHandsReady) valid = false;
            }
            catch (const std::exception& error) {
                flatHandsFailed = true;
                Log(std::string("[touch/hands] Flat visual resize disabled; menu retained: ") + error.what());
            }
        }
        if (begun && valid && running) {
            Trace("render event begin");
            XrFrameEndInfo end{XR_TYPE_FRAME_END_INFO};
            end.displayTime = displayTime; end.environmentBlendMode = XR_ENVIRONMENT_BLEND_MODE_OPAQUE;
            FrameLayers composition;
            if (job.flat) {
                D3D11_TEXTURE2D_DESC desc{}; job.flat->GetDesc(&desc);
                if (!flatChain.handle || flatChain.width != desc.Width || flatChain.height != desc.Height)
                    CreateChain(flatChain, desc.Width, desc.Height);
                UpdatePanelAnchor(panelHeadAnchor, panelPose, panelAnchored, anchoredPanelDistance, head, job.distance, job.recenter);
                // Valid-but-unfocused startup poses can be offset by the user's
                // floor height (observed -1.112 m). Show a provisional panel,
                // but don't keep that anchor once focused tracking becomes real.
                if (!firstFocusedTracking) panelAnchored = false;
                Blit(flatChain, job.flat.Get(), {0,0,1,1}, job.flipFlat);
                const float panelWidth = job.distance * job.flatWidthRatio;
                const float panelHeight = job.flatAspect > 0 ? panelWidth / job.flatAspect : panelWidth * flatChain.height / flatChain.width;
                const auto shownPose = OffsetPose(panelPose, panelWidth * job.flatOffsetX, panelHeight * job.flatOffsetY, 0);
                composition.AddFlat(flatChain, localSpace, shownPose, job.distance, job.flatWidthRatio, job.flatAspect);
                if (flatHandsReady) {
                    // A failure disables only cosmetic hands. A resize retires
                    // the preceding frame without new copies before reaching here.
                    try {
                        // This isolated Unity command-buffer path has one
                        // fixed texture-origin contract; the optional world
                        // camera inversion setting must not mirror the hands.
                        BlitPair(flatHands[0], job.left.Get(), job.crops[0], true,
                            flatHands[1], job.right.Get(), job.crops[1], true);
                        composition.AddProjection(flatHands, localSpace, views);
                    } catch (const std::exception& error) {
                        flatHandsFailed = true;
                        Log(std::string("[touch/hands] Flat visuals disabled for this session; menu retained: ") + error.what());
                    }
                }
            } else if (job.left && job.right) {
                // One simulation frame and one predicted display time for BOTH eyes.
                BlitPair(eyes[0], job.left.Get(), job.crops[0], job.flipEyes,
                    eyes[1], job.right.Get(), job.crops[1], job.flipEyes);
                composition.AddProjection(eyes, localSpace, views);
                if (hudReady) {
                    try {
                        BlitHud(hudChain, job.hudBlack.Get(), job.hudWhite.Get(), true, job.hudLinear);
                        if(job.uiRegionCount81)composition.AddUiRegions81(hudChain,localSpace,head,job.uiRegions81.data(),job.uiRegionCount81);
                        else composition.AddHud(hudChain, localSpace, head, job.hudWidth, job.hudHeight,
                            job.hudDistance, job.hudX, job.hudY);
                    } catch (const std::exception& error) {
                        hudFailed = true;
                        Log(std::string("[ui/full-resolution] HUD compositor disabled; managed HUD fallback requested: ") + error.what());
                    }
                }
                panelAnchored = false;
            }
            // Independent physical plane: it is never flattened onto the game
            // HUD and never sampled by a scene temporal upscaler. Optional
            // capture/resize failure must not discard an otherwise valid frame.
            if (spatialReady) {
                try {
                    BlitHud(spatialChain, job.spatialBlack.Get(), job.spatialWhite.Get(), true, job.spatialLinear);
                    if (job.spatialAtlas) composition.AddSpatialAtlas(spatialChain, localSpace, job.spatialPose,
                        job.spatialWidth, job.spatialHeight, job.spatialInfoVisible, job.spatialInfoPose, job.spatialInfoSize);
                    else composition.AddSpatial(spatialChain, localSpace, job.spatialPose, job.spatialWidth, job.spatialHeight);
                } catch (const std::exception& error) {
                    spatialFailed = true; Log(std::string("[ui/spatial] Optional layer disabled: ") + error.what());
                }
            }
            end.layerCount = composition.count; end.layers = composition.count ? composition.layers : nullptr;
            Trace("xrEndFrame begin");
            const auto endStarted = measure ? TimingClock::now() : TimingClock::time_point{};
            const auto result = xrEndFrame(session, &end);
            if (measure) activeRenderTiming.endFrameMs = MillisecondsSince(endStarted);
            begun = false; valid = false;
            Check(result, "xrEndFrame");
            PublishFlat(composition.Panel(job.serial, job.flipFlat));
            Trace("xrEndFrame complete");
            if (composition.hasFlat) ++stats.flatFrames;
            else if (composition.count) {
                ++stats.submittedPairs;
                stats.lastLeftSerial = stats.lastRightSerial = job.serial;
            } else ++stats.emptyFrames;
            stats.lastSerial = job.serial;
            activeRenderTiming.completed = 1;
            if (stats.submittedPairs == 1 && !job.flat && composition.count) Log("FIRST STEREO PAIR: left+right, same frame=" + std::to_string(job.serial));
            if (stats.flatFrames == 1 && composition.hasFlat) Log("FIRST FLAT FRAME: OpenXR quad (both eyes)");
        } else EndEmpty();
    } catch (const std::exception& error) {
        Error(error); ++stats.failedFrames; fatal = true; ClearTouchSnapshot(); PublishFlat({});
        try { EndEmpty(); } catch (...) {}
    }
    if (measure) {
        activeRenderTiming.totalMs = MillisecondsSince(renderStarted);
        activeRenderTiming.flushes = static_cast<int32_t>(blitFlushes - flushesBefore);
        activeRenderTiming.sourceViewsCreated = static_cast<int32_t>(sourceViewsCreated - viewsBefore);
        std::lock_guard<std::mutex> snapshotLock(snapshotGuard);
        renderTimingSnapshot = activeRenderTiming;
    }
    activeRenderTiming = {};
    job = {}; queued = false; stats.queued = 0;
    finished.notify_all();
}
} // namespace

int __cdecl RTX_SelectRuntime(int runtime) {
    std::lock_guard<std::mutex> lock(guard);
    if (!RuntimePolicy::Valid(runtime) || ((instance || session) && requestedRuntime != runtime)) return -1;
    requestedRuntime = runtime; return 1;
}
int __cdecl RTX_Init(void* texture, float scale, const wchar_t* path) {
    std::lock_guard<std::mutex> lock(guard);
    if (session) return 1;
    {
        std::lock_guard<std::mutex> logLock(logGuard);
        if (logfile && ownLogfile) fclose(logfile);
        logfile = nullptr; ownLogfile = false;
        logPath = path ? path : L"";
    }
    lastError.clear(); stats = {}; beginTiming = {}; activeRenderTiming = {}; touchErrorLogs = 0;
    { std::lock_guard<std::mutex> snapshotLock(snapshotGuard); renderTimingSnapshot = {}; }
    try {
        if (!texture) throw std::runtime_error("No Unity D3D11 texture supplied");
        reinterpret_cast<ID3D11Texture2D*>(texture)->GetDevice(&device);
        Log("RTMaquetaBridge 0.1.62 initialization / selected=" + std::string(requestedRuntime == 1 ? "Meta Quest Link" : "VDXR"));
        ProtectDevice();
        const char* extensions[] = {XR_KHR_D3D11_ENABLE_EXTENSION_NAME};
        XrInstanceCreateInfo create{XR_TYPE_INSTANCE_CREATE_INFO};
        strcpy_s(create.applicationInfo.applicationName, "Rogue Trader Maqueta OpenXR");
        strcpy_s(create.applicationInfo.engineName, "Unity Waaagh / UMM");
        create.applicationInfo.applicationVersion = 162;
        create.applicationInfo.apiVersion = XR_MAKE_VERSION(1,0,34);
        create.enabledExtensionCount = 1; create.enabledExtensionNames = extensions;
        Check(xrCreateInstance(&create, &instance), "xrCreateInstance");
        XrInstanceProperties runtime{XR_TYPE_INSTANCE_PROPERTIES};
        Check(xrGetInstanceProperties(instance, &runtime), "xrGetInstanceProperties");
        Log(std::string("runtime=") + runtime.runtimeName + " / OpenXR direct / D3D11");
        // Additional guard: a wrong runtime cannot silently send the game to SteamVR.
        if (!RuntimePolicy::Matches(requestedRuntime, runtime.runtimeName))
            throw std::runtime_error("The loaded OpenXR runtime does not match the selected runtime. Restart the game after changing it.");
        XrSystemGetInfo system{XR_TYPE_SYSTEM_GET_INFO}; system.formFactor = XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;
        const XrResult available = xrGetSystem(instance, &system, &systemId);
        if (available == XR_ERROR_FORM_FACTOR_UNAVAILABLE) { Log("Headset unavailable; retry later"); Destroy(); return 0; }
        Check(available, "xrGetSystem");
        XrSystemProperties uiProperties81{XR_TYPE_SYSTEM_PROPERTIES};
        Check(xrGetSystemProperties(instance,systemId,&uiProperties81),"xrGetSystemProperties UI budget");
        uiMaxLayers81=int(uiProperties81.graphicsProperties.maxLayerCount);
        uiMaxWidth81=int(std::min(uiProperties81.graphicsProperties.maxSwapchainImageWidth,8192u));
        uiMaxHeight81=int(std::min(uiProperties81.graphicsProperties.maxSwapchainImageHeight,8192u));
        PFN_xrGetD3D11GraphicsRequirementsKHR requirementsFunction = nullptr;
        Check(xrGetInstanceProcAddr(instance, "xrGetD3D11GraphicsRequirementsKHR", reinterpret_cast<PFN_xrVoidFunction*>(&requirementsFunction)), "get D3D11 requirements function");
        XrGraphicsRequirementsD3D11KHR requirements{XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR};
        Check(requirementsFunction(instance, systemId, &requirements), "xrGetD3D11GraphicsRequirementsKHR");
        ComPtr<IDXGIDevice> dxgi; ComPtr<IDXGIAdapter> adapter; DXGI_ADAPTER_DESC desc{};
        Hr(device.As(&dxgi), "IDXGIDevice"); Hr(dxgi->GetAdapter(&adapter), "GetAdapter"); Hr(adapter->GetDesc(&desc), "GetDesc");
        char unityName80[512]{};
        WideCharToMultiByte(CP_UTF8,0,desc.Description,-1,unityName80,sizeof(unityName80),nullptr,nullptr);
        if (memcmp(&desc.AdapterLuid, &requirements.adapterLuid, sizeof(LUID))) {
            char requiredName80[512]="Unknown adapter";
            ComPtr<IDXGIFactory> factory80;
            if(SUCCEEDED(adapter->GetParent(IID_PPV_ARGS(&factory80))))for(UINT i=0;;i++) {
                ComPtr<IDXGIAdapter> candidate80;DXGI_ADAPTER_DESC candidateDesc80{};
                if(factory80->EnumAdapters(i,&candidate80)!=S_OK)break;
                if(SUCCEEDED(candidate80->GetDesc(&candidateDesc80))&&!memcmp(&candidateDesc80.AdapterLuid,&requirements.adapterLuid,sizeof(LUID))) {
                    WideCharToMultiByte(CP_UTF8,0,candidateDesc80.Description,-1,requiredName80,sizeof(requiredName80),nullptr,nullptr);break;
                }
            }
            char details80[1600]{};
            snprintf(details80,sizeof(details80),"Graphics adapter mismatch: Unity=%s [%08X:%08X]; OpenXR=%s [%08X:%08X]; runtime=%s. Restart the game on the required GPU.",
                unityName80,static_cast<unsigned>(desc.AdapterLuid.HighPart),desc.AdapterLuid.LowPart,
                requiredName80,static_cast<unsigned>(requirements.adapterLuid.HighPart),requirements.adapterLuid.LowPart,runtime.runtimeName);
            throw std::runtime_error(details80);
        }
        if(device->GetFeatureLevel()<requirements.minFeatureLevel) {
            char details80[900]{};
            snprintf(details80,sizeof(details80),"D3D11 feature level insufficient: GPU=%s; current=0x%X; required=0x%X; runtime=%s. This device cannot satisfy the runtime graphics requirement.",
                unityName80,static_cast<unsigned>(device->GetFeatureLevel()),static_cast<unsigned>(requirements.minFeatureLevel),runtime.runtimeName);
            throw std::runtime_error(details80);
        }
        XrGraphicsBindingD3D11KHR binding{XR_TYPE_GRAPHICS_BINDING_D3D11_KHR}; binding.device = device.Get();
        XrSessionCreateInfo sessionInfo{XR_TYPE_SESSION_CREATE_INFO}; sessionInfo.next = &binding; sessionInfo.systemId = systemId;
        Trace("xrCreateSession begin");
        Check(xrCreateSession(instance, &sessionInfo, &session), "xrCreateSession");
        Trace("xrCreateSession complete");
        XrReferenceSpaceCreateInfo space{XR_TYPE_REFERENCE_SPACE_CREATE_INFO};
        space.poseInReferenceSpace.orientation.w = 1; space.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_LOCAL;
        Check(xrCreateReferenceSpace(session, &space, &localSpace), "create LOCAL space");
        space.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_VIEW;
        Check(xrCreateReferenceSpace(session, &space, &viewSpace), "create VIEW space");
        CreateTouchActions(); // Attach exactly once, before Poll can call xrBeginSession.
        uint32_t count = 0;
        Check(xrEnumerateViewConfigurationViews(instance, systemId, XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO, 0, &count, nullptr), "enumerate views count");
        if (count != 2) throw std::runtime_error("Runtime does not provide exactly two primary stereo views");
        XrViewConfigurationView config[2]{{XR_TYPE_VIEW_CONFIGURATION_VIEW},{XR_TYPE_VIEW_CONFIGURATION_VIEW}};
        Check(xrEnumerateViewConfigurationViews(instance, systemId, XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO, 2, &count, config), "enumerate views");
        scale = std::isfinite(scale) ? std::clamp(scale, 0.25f, 1.5f) : 1.f;
        recommendedWidth = std::max(config[0].recommendedImageRectWidth,config[1].recommendedImageRectWidth);
        recommendedHeight = std::max(config[0].recommendedImageRectHeight,config[1].recommendedImageRectHeight);
        const uint32_t textureLimit80 = device->GetFeatureLevel() >= D3D_FEATURE_LEVEL_11_0 ? 16384u : 8192u;
        maximumWidth = std::min({config[0].maxImageRectWidth,config[1].maxImageRectWidth,textureLimit80});
        maximumHeight = std::min({config[0].maxImageRectHeight,config[1].maxImageRectHeight,textureLimit80});
        if (!recommendedWidth || !recommendedHeight || !maximumWidth || !maximumHeight)
            throw std::runtime_error("OpenXR reported an invalid eye resolution");
        appliedRenderScale = scale;
        eyeWidth = ScaledSize(recommendedWidth, maximumWidth, scale);
        eyeHeight = ScaledSize(recommendedHeight, maximumHeight, scale);
        Log("Resolution: OpenXR runtime recommends " + std::to_string(recommendedWidth) + "x" + std::to_string(recommendedHeight) +
            "; mod scale=" + std::to_string(appliedRenderScale) + "; actual=" + std::to_string(eyeWidth) + "x" + std::to_string(eyeHeight));
        Check(xrEnumerateSwapchainFormats(session, 0, &count, nullptr), "swapchain formats count");
        std::vector<int64_t> formats(count);
        Check(xrEnumerateSwapchainFormats(session, count, &count, formats.data()), "swapchain formats");
        bool found = false;
        for (auto preferred : {DXGI_FORMAT_R8G8B8A8_UNORM_SRGB, DXGI_FORMAT_B8G8R8A8_UNORM_SRGB})
            if (std::find(formats.begin(), formats.end(), preferred) != formats.end()) { colorFormat = preferred; found = true; break; }
        if (!found) throw std::runtime_error("No compatible sRGB OpenXR swapchain format");
        CreateBlitter(); CreateChain(eyes[0], eyeWidth, eyeHeight); CreateChain(eyes[1], eyeWidth, eyeHeight);
        Log("Session created: " + std::to_string(eyeWidth) + "x" + std::to_string(eyeHeight) + " per eye; 6DoF LOCAL; awaiting READY");
        return 1;
    } catch (const std::exception& error) { const int result = Error(error); Destroy(); return result; }
}
int __cdecl RTX_Begin(Frame* frame) {
    if (!frame) return -1;
    const bool measureBegin = diagnosticsEnabled.load(std::memory_order_relaxed);
    const auto queueStarted = measureBegin ? TimingClock::now() : TimingClock::time_point{};
    std::unique_lock<std::mutex> lock(guard);
    beginTiming = {};
    *frame = {};
    ClearTouchSnapshot(); // A failed/hidden Begin cannot expose the previous pressed buttons.
    if (!session) return 0;
    // The previous event is normally complete at this point. Never replace its
    // pose/textures with those from a newer simulation frame.
    if (!finished.wait_for(lock, std::chrono::milliseconds(2000), [] { return !queued; })) {
        lastError = "Unity render event has not completed; keeping prior frame ownership"; return -2;
    }
    beginTiming.queueWaitMs = measureBegin ? MillisecondsSince(queueStarted) : 0;
    try {
        if (fatal) return -3;
        EndEmpty(); Poll();
        if (state == XR_SESSION_STATE_EXITING || state == XR_SESSION_STATE_LOSS_PENDING) return -3;
        frame->width = eyeWidth; frame->height = eyeHeight; frame->state = state;
        if (!running) return 0;
        XrFrameWaitInfo wait{XR_TYPE_FRAME_WAIT_INFO}; XrFrameState timing{XR_TYPE_FRAME_STATE};
        Trace("xrWaitFrame begin");
        const auto runtimeStarted = measureBegin ? TimingClock::now() : TimingClock::time_point{};
        Check(xrWaitFrame(session, &wait, &timing), "xrWaitFrame");
        beginTiming.runtimeWaitMs = measureBegin ? MillisecondsSince(runtimeStarted) : 0;
        beginTiming.predictedPeriodMs = static_cast<double>(timing.predictedDisplayPeriod) / 1000000.0;
        const auto locateStarted = measureBegin ? TimingClock::now() : TimingClock::time_point{};
        XrFrameBeginInfo begin{XR_TYPE_FRAME_BEGIN_INFO};
        Check(xrBeginFrame(session, &begin), "xrBeginFrame");
        begun = true; displayTime = timing.predictedDisplayTime;
        frame->serial = ++stats.begun; frame->shouldRender = timing.shouldRender;
        beginTiming.serial = frame->serial;
        Trace("xrBeginFrame complete");
        if (!timing.shouldRender) {
            UpdateTouchSnapshot(frame->serial, displayTime, false);
            beginTiming.beginLocateMs = measureBegin ? MillisecondsSince(locateStarted) : 0; return 1;
        }
        XrViewLocateInfo locate{XR_TYPE_VIEW_LOCATE_INFO};
        locate.viewConfigurationType = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO; locate.displayTime = displayTime; locate.space = localSpace;
        XrViewState viewState{XR_TYPE_VIEW_STATE}; uint32_t count = 0;
        for (auto& view : views) view = {XR_TYPE_VIEW};
        Check(xrLocateViews(session, &locate, &viewState, 2, &count, views.data()), "xrLocateViews");
        XrSpaceLocation position{XR_TYPE_SPACE_LOCATION};
        Check(xrLocateSpace(viewSpace, localSpace, displayTime, &position), "xrLocateSpace(head)");
        constexpr auto viewFlags = XR_VIEW_STATE_ORIENTATION_VALID_BIT | XR_VIEW_STATE_POSITION_VALID_BIT;
        constexpr auto headFlags = XR_SPACE_LOCATION_ORIENTATION_VALID_BIT | XR_SPACE_LOCATION_POSITION_VALID_BIT;
        valid = count == 2 && (viewState.viewStateFlags & viewFlags) == viewFlags && (position.locationFlags & headFlags) == headFlags;
        if (valid) {
            head = position.pose; frame->valid = 1; frame->head = Pack(head);
            frame->left = Pack(views[0]); frame->right = Pack(views[1]);
            constexpr auto trackedFlags = XR_SPACE_LOCATION_ORIENTATION_TRACKED_BIT | XR_SPACE_LOCATION_POSITION_TRACKED_BIT;
            AdvanceTrackingOrigin(displayTime, state == XR_SESSION_STATE_FOCUSED,
                (position.locationFlags & trackedFlags) == trackedFlags);
        }
        UpdateTouchSnapshot(frame->serial, displayTime, valid);
        beginTiming.beginLocateMs = measureBegin ? MillisecondsSince(locateStarted) : 0;
        return 1;
    } catch (const std::exception& error) { fatal = true; return Error(error); }
}
int __cdecl RTX_Queue(uint64_t serial, void* left, void* right, void* flat, Crop lc, Crop rc, int flipEyes, int flipFlat, float distance, int recenter) {
    std::lock_guard<std::mutex> lock(guard);
    if (!begun || queued || serial != stats.begun) return 0;
    if ((left == nullptr) != (right == nullptr)) { lastError = "A stereo frame must contain both eyes"; return 0; }
    // All three references are retained until the render event ends the frame.
    job.serial = serial;
    job.left = reinterpret_cast<ID3D11Texture2D*>(left); job.right = reinterpret_cast<ID3D11Texture2D*>(right);
    job.flat = reinterpret_cast<ID3D11Texture2D*>(flat); job.crops[0] = lc; job.crops[1] = rc;
    job.flipEyes = flipEyes != 0; job.flipFlat = flipFlat != 0;
    job.distance = std::clamp(distance, 0.5f, 5.f); job.recenter = recenter != 0;
    job.flatWidthRatio = requestedFlatWidthRatio.load(std::memory_order_relaxed);
    job.flatAspect = requestedFlatAspect.load(std::memory_order_relaxed);
    job.flatOffsetX = requestedFlatOffsetX.load(std::memory_order_relaxed);
    job.flatOffsetY = requestedFlatOffsetY.load(std::memory_order_relaxed);
    if (job.flat || job.hudSerial != serial) { job.hudBlack.Reset(); job.hudWhite.Reset(); job.hudSerial = 0; }
    if (job.flat || !job.left || !job.right || job.spatialSerial != serial) { job.spatialBlack.Reset(); job.spatialWhite.Reset(); job.spatialSerial = 0; }
    if (!job.flat || job.recenter || !panelAnchored) PublishFlat({});
    queued = true; stats.queued = 1; return 1;
}
void __cdecl RTX_SetFlatPanelWidth(float widthRatio) {
    requestedFlatWidthRatio.store(std::isfinite(widthRatio) ? std::clamp(widthRatio, .01f, 8.f) : 1.7f, std::memory_order_relaxed);
}
void __cdecl RTX_SetFlatPanelOffset(float x, float y) {
    requestedFlatOffsetX.store(std::isfinite(x) ? std::clamp(x, -.65f, .65f) : 0, std::memory_order_relaxed);
    requestedFlatOffsetY.store(std::isfinite(y) ? std::clamp(y, -.65f, .65f) : 0, std::memory_order_relaxed);
}
void __cdecl RTX_SetFlatPanelAspect(float aspect) {
    requestedFlatAspect.store(std::isfinite(aspect) && aspect > 0 ? std::clamp(aspect, .8f, 2.4f) : 0, std::memory_order_relaxed);
}
int __cdecl RTX_GetHudStatus() {
    return hudFailed.load(std::memory_order_relaxed) ? -1 : 1;
}
int __cdecl RTX_GetSpatialStatus() { return spatialFailed.load(std::memory_order_relaxed) ? -1 : 1; }
int __cdecl RTX_SetSpatialFrame(uint64_t serial, void* black, void* white, Pose pose, float width, float height, int linear) {
    std::lock_guard<std::mutex> lock(guard);
    if (!begun || queued || serial != stats.begun || spatialFailed) return 0;
    job.spatialSerial = 0; job.spatialBlack.Reset(); job.spatialWhite.Reset();
    job.spatialAtlas = false; job.spatialInfoVisible = false;
    const float* values = reinterpret_cast<const float*>(&pose);
    for (int i = 0; i < 7; ++i) if (!std::isfinite(values[i])) return 0;
    const float norm = pose.qx*pose.qx + pose.qy*pose.qy + pose.qz*pose.qz + pose.qw*pose.qw;
    if (!black || !white || !std::isfinite(width) || !std::isfinite(height) || width <= 0 || height <= 0 ||
        width > 10 || height > 10 || norm < .99f || norm > 1.01f) return 0;
    job.spatialBlack = reinterpret_cast<ID3D11Texture2D*>(black); job.spatialWhite = reinterpret_cast<ID3D11Texture2D*>(white);
    job.spatialPose = {{pose.qx,pose.qy,pose.qz,pose.qw},{pose.x,pose.y,pose.z}};
    job.spatialWidth = width; job.spatialHeight = height; job.spatialLinear = linear != 0; job.spatialSerial = serial;
    return 1;
}
int __cdecl RTX_SetSpatialInformation(uint64_t serial, Pose pose, float size, int visible) {
    std::lock_guard<std::mutex> lock(guard);
    if (!begun || queued || serial != stats.begun || serial != job.spatialSerial || spatialFailed) return 0;
    job.spatialInfoVisible = false;
    auto reject = []() {
        // Do not publish an unsplit atlas if its second pose is rejected.
        // The world/HUD job stays valid; only this optional surface is retired.
        job.spatialSerial = 0; job.spatialBlack.Reset(); job.spatialWhite.Reset();
        job.spatialAtlas = false; job.spatialInfoVisible = false; return 0;
    };
    if (visible) {
        const float* values = reinterpret_cast<const float*>(&pose);
        for (int i = 0; i < 7; ++i) if (!std::isfinite(values[i])) return reject();
        const float norm = pose.qx*pose.qx + pose.qy*pose.qy + pose.qz*pose.qz + pose.qw*pose.qw;
        if (!std::isfinite(size) || size <= 0 || size > 5 || norm < .99f || norm > 1.01f) return reject();
    }
    job.spatialAtlas = true; job.spatialInfoVisible = visible != 0; job.spatialInfoSize = size;
    job.spatialInfoPose = {{pose.qx,pose.qy,pose.qz,pose.qw},{pose.x,pose.y,pose.z}};
    return 1;
}
int __cdecl RTX_SetHudFrame(uint64_t serial, void* black, void* white, float width, float height,
                           float distance, float x, float y, int linear) {
    std::lock_guard<std::mutex> lock(guard);
    if (!begun || queued || serial != stats.begun || hudFailed) return 0;
    job.hudBlack.Reset(); job.hudWhite.Reset(); job.hudSerial = 0;
    job.uiRegionCount81=0;
    if (!black || !white || !std::isfinite(width) || !std::isfinite(height) || !std::isfinite(distance) ||
        !std::isfinite(x) || !std::isfinite(y) || width <= 0 || height <= 0 || distance <= 0) return 0;
    job.hudBlack = reinterpret_cast<ID3D11Texture2D*>(black);
    job.hudWhite = reinterpret_cast<ID3D11Texture2D*>(white);
    job.hudSerial = serial; job.hudWidth = width; job.hudHeight = height;
    job.hudDistance = distance; job.hudX = x; job.hudY = y; job.hudLinear = linear != 0;
    return 1;
}
uint64_t __cdecl RTX_GetTrackingOriginRevision() {
    return firstFocusedTracking.load(std::memory_order_acquire) ? trackingOriginRevision.load(std::memory_order_acquire) : 0;
}
int __cdecl RTX_PreviewSize(float scale, int* width, int* height) {
    std::lock_guard<std::mutex> lock(guard);
    if (!session || !width || !height || !std::isfinite(scale) || scale < .25f || scale > 1.5f) return 0;
    *width = ScaledSize(recommendedWidth, maximumWidth, scale);
    *height = ScaledSize(recommendedHeight, maximumHeight, scale);
    return 1;
}
int __cdecl RTX_TryResize(float scale) {
    std::lock_guard<std::mutex> lock(guard);
    if (!session || fatal || !std::isfinite(scale) || scale < .25f || scale > 1.5f) return -1;
    if (queued || begun) return 0;
    const int width = ScaledSize(recommendedWidth, maximumWidth, scale);
    const int height = ScaledSize(recommendedHeight, maximumHeight, scale);
    if (width == eyeWidth && height == eyeHeight) { appliedRenderScale = scale; return 1; }
    Chain replacement[2];
    try {
        if (!CopiesComplete()) return 0;
        // Allocate both first; a failed allocation leaves the existing stereo
        // pair usable. No session restart, pose recenter or D3D context lock.
        CreateChain(replacement[0], width, height); CreateChain(replacement[1], width, height);
        std::swap(eyes[0], replacement[0]); std::swap(eyes[1], replacement[1]);
        eyeWidth = width; eyeHeight = height; appliedRenderScale = scale;
        DestroyChain(replacement[0]); DestroyChain(replacement[1]);
        // The pair is committed. A diagnostic allocation failure must never
        // report -1 and make the managed caller retain mismatched old targets.
        try { Log("Live output resolution applied: " + std::to_string(width) + "x" + std::to_string(height)); } catch (...) {}
        return 1;
    } catch (const std::exception& error) {
        DestroyChain(replacement[0]); DestroyChain(replacement[1]);
        return Error(error);
    }
}
void* __cdecl RTX_GetRenderEvent() { return reinterpret_cast<void*>(&RenderEvent); }
void* __cdecl RTX_GetGpuPassEvent() { return reinterpret_cast<void*>(&GpuPassEvent); }
void* __cdecl RTX_GetMonitorEvent81() { return reinterpret_cast<void*>(&MonitorEvent81); }
int __cdecl RTX_ReadMonitorTimings81(MonitorGpuTiming81* samples,int capacity) {
    if(!samples||capacity<1)return 0;
    std::unique_lock<std::mutex> lock(monitorSnapshotGuard81,std::try_to_lock);
    if(!lock.owns_lock())return 0;
    unsigned count=std::min(monitorCount81,unsigned(capacity));
    for(unsigned i=0;i<count;++i)samples[i]=monitorReadings81[(monitorWrite81+monitorReadings81.size()-monitorCount81+i)%monitorReadings81.size()];
    monitorCount81-=count;return int(count);
}
int __cdecl RTX_GetUiLimits81(int* layers,int* width,int* height) {
    if(!layers||!width||!height)return 0;
    std::unique_lock<std::mutex> lock(guard,std::try_to_lock);
    if(!lock.owns_lock()||!session)return 0;
    *layers=uiMaxLayers81;*width=uiMaxWidth81;*height=uiMaxHeight81;return 1;
}
int __cdecl RTX_SetUiRegions81(uint64_t serial,const UiRegion81* regions,int count) {
    std::lock_guard<std::mutex> lock(guard);
    if(!begun||queued||serial!=job.hudSerial||!regions||count<1||count>6||count+3>uiMaxLayers81)return 0;
    for(int i=0;i<count;++i) {
        const auto& r=regions[i];
        if(r.x<0||r.y<0||r.pixelsWide<1||r.pixelsHigh<1||!std::isfinite(r.width)||!std::isfinite(r.height)||
            !std::isfinite(r.distance)||!std::isfinite(r.offsetX)||!std::isfinite(r.offsetY)||r.width<=0||r.height<=0||r.distance<=0)return 0;
    }
    std::copy(regions,regions+count,job.uiRegions81.begin());job.uiRegionCount81=count;return 1;
}
int __cdecl RTX_ReadGpuPassTimings(GpuPassTiming* timings, int capacity) {
    if (!timings || capacity < GpuPassCount) return 0;
    std::unique_lock<std::mutex> lock(gpuPassSnapshotGuard, std::try_to_lock);
    if (!lock.owns_lock()) return 0;
    std::copy(gpuPassPublished.begin(), gpuPassPublished.end(), timings);
    gpuPassPublished = {};
    return 1;
}
int __cdecl RTX_TestGpuPassTimings(void* testDevice) {
    // Standalone timestamp smoke test: the caller owns an isolated D3D11 device.
    // It never opens an XR runtime, loads a scene or changes graphics settings.
    if (!testDevice || device || instance || session) return 0;
    const bool previousDiagnostics = diagnosticsEnabled.exchange(true);
    try {
        device = reinterpret_cast<ID3D11Device*>(testDevice); ProtectDevice(); ResetGpuPassQueries();
        D3D11_TEXTURE2D_DESC desc{}; desc.Width = desc.Height = 256; desc.MipLevels = desc.ArraySize = 1;
        desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; desc.SampleDesc.Count = 1; desc.BindFlags = D3D11_BIND_RENDER_TARGET;
        ComPtr<ID3D11Texture2D> target; ComPtr<ID3D11RenderTargetView> rtv;
        Hr(device->CreateTexture2D(&desc, nullptr, &target), "timestamp test texture");
        Hr(device->CreateRenderTargetView(target.Get(), nullptr, &rtv), "timestamp test target");
        {
            std::lock_guard<std::mutex> lock(guard);
            GpuPassEvent(1); // Must try-lock and drop, not deadlock on guard.
        }
        if (gpuPassWork) throw std::runtime_error("Timestamp callback waited or recorded while bridge lock busy");
        GpuPassEvent(1);
        const float color[4]{0.25f, 0.5f, 0.75f, 1};
        for (int i = 0; i < 32; ++i) context->ClearRenderTargetView(rtv.Get(), color);
        GpuPassEvent(2);
        // The test has no host Present. This test-only Flush submits the fake
        // frame; the production marker/poll paths never call Flush or Map.
        context->Flush();
        PollGpuPassQueries(); PollGpuPassQueries();
        std::array<GpuPassTiming, GpuPassCount> readings{};
        RTX_ReadGpuPassTimings(readings.data(), GpuPassCount);
        if (readings[0].observations) throw std::runtime_error("Timestamp was read before the three-submission delay");
        bool observed = false;
        for (int attempt = 0; attempt < 1000; ++attempt) {
            PollGpuPassQueries(); RTX_ReadGpuPassTimings(readings.data(), GpuPassCount);
            if (readings[0].observations == 1 && readings[0].sumMs >= 0 && !readings[0].rejected) { observed = true; break; }
            Sleep(1); // Only the standalone test retries; runtime does one poll.
        }
        if (!observed) throw std::runtime_error("D3D11 timestamp sample unavailable in isolated test");
        printf("PASS D3D11 timestamps: %.6f ms, delayed three submissions, nonblocking lock, DONOTFLUSH reads\n", readings[0].sumMs);
        RTX_ReadGpuPassTimings(readings.data(), GpuPassCount);
        if (readings[0].observations) throw std::runtime_error("Timestamp observation counted twice");
        MonitorEvent81(3,reinterpret_cast<void*>(uintptr_t(8123)));
        for(int i=0;i<32;++i)context->ClearRenderTargetView(rtv.Get(),color);
        MonitorEvent81(4,reinterpret_cast<void*>(uintptr_t(8123)));context->Flush();
        MonitorGpuTiming81 monitorSamples[8]{};bool monitorObserved=false;
        for(int attempt=0;attempt<1000;++attempt) {
            PollMonitor81();int n=RTX_ReadMonitorTimings81(monitorSamples,8);
            if(n) {
                if(n!=1||monitorSamples[0].serial!=8123||monitorSamples[0].kind!=2||monitorSamples[0].valid!=1||monitorSamples[0].milliseconds<0)
                    throw std::runtime_error("Monitor timestamp lost serial/kind/validity");
                monitorObserved=true;break;
            }
            Sleep(1);
        }
        if(!monitorObserved||RTX_ReadMonitorTimings81(monitorSamples,8)!=0)throw std::runtime_error("Monitor timestamp missing or counted twice");
        printf("PASS Monitor81 GPU timestamp retains original serial, clear kind and one delayed observation\n");
        ResetGpuPassQueries(); Destroy(); diagnosticsEnabled.store(previousDiagnostics); return 1;
    }
    catch (const std::exception& error) { Error(error); Destroy(); diagnosticsEnabled.store(previousDiagnostics); return -1; }
}
int __cdecl RTX_Shutdown() {
    std::unique_lock<std::mutex> lock(guard);
    if (!finished.wait_for(lock, std::chrono::milliseconds(2000), [] { return !queued; })) return 0;
    try {
        if (!CopiesComplete(true)) return 0;
    } catch (const std::exception& error) {
        // A failed query on a live device does not prove completion. Keep its
        // swapchains and let the existing managed shutdown pump retry safely.
        Error(error); return 0;
    }
    Log("Shutdown; pairs=" + std::to_string(stats.submittedPairs) + " flat=" + std::to_string(stats.flatFrames) + " errors=" + std::to_string(stats.failedFrames));
    Destroy();
    {
        std::lock_guard<std::mutex> logLock(logGuard);
        if (logfile && ownLogfile) fclose(logfile);
        logfile = nullptr; ownLogfile = false; logPath.clear();
    }
    return 1;
}
void __cdecl RTX_GetStats(Stats* value) { std::lock_guard<std::mutex> lock(guard); if (value) { *value = stats; value->queued = queued; } }
void __cdecl RTX_GetBeginTiming(BeginTiming* value) { std::lock_guard<std::mutex> lock(guard); if (value) *value = session ? beginTiming : BeginTiming{}; }
int __cdecl RTX_GetRenderTiming(RenderTiming* value) {
    if (!value) return 0;
    *value = {};
    if (!diagnosticsEnabled.load()) return 0;
    std::unique_lock<std::mutex> lock(snapshotGuard, std::try_to_lock);
    if (!lock.owns_lock() || !renderTimingSnapshot.serial || !renderTimingSnapshot.completed) return 0;
    *value = renderTimingSnapshot; return 1;
}
void __cdecl RTX_SetDiagnosticsEnabled(int enabled) { diagnosticsEnabled.store(enabled != 0, std::memory_order_relaxed); }
extern "C" __declspec(dllexport) void __cdecl RTX_SetLogEnabled(int enabled) {
    logEnabled.store(enabled != 0, std::memory_order_relaxed);
    if (!enabled) {
        std::unique_lock<std::mutex> lock(logGuard, std::try_to_lock);
        if (lock.owns_lock() && logfile && ownLogfile) {
            fclose(logfile); logfile = nullptr; ownLogfile = false;
        }
    }
}
int __cdecl RTX_GetTouch(TouchFrame* value) {
    if (!value) return -1;
    *value = {};
    std::unique_lock<std::mutex> lock(snapshotGuard, std::try_to_lock);
    if (!lock.owns_lock()) return 0;
    *value = touchSnapshot; return value->ready ? 1 : 0;
}
int __cdecl RTX_GetFlatPanel(FlatPanel* value) {
    if (!value) return -1;
    *value = {};
    std::unique_lock<std::mutex> lock(snapshotGuard, std::try_to_lock);
    if (!lock.owns_lock()) return -1;
    *value = flatSnapshot; return value->valid ? 1 : 0;
}
void __cdecl RTX_GetSize(int* width, int* height) { std::lock_guard<std::mutex> lock(guard); if (width) *width = eyeWidth; if (height) *height = eyeHeight; }
void __cdecl RTX_GetResolutionInfo(int* width, int* height, float* scale) {
    std::lock_guard<std::mutex> lock(guard);
    if (width) *width = session ? recommendedWidth : 0;
    if (height) *height = session ? recommendedHeight : 0;
    if (scale) *scale = session ? appliedRenderScale : 0;
}
int __cdecl RTX_GetError(char* text, int capacity) {
    std::lock_guard<std::mutex> lock(guard);
    if (!text || capacity <= 0) return 0;
    strncpy_s(text, capacity, lastError.c_str(), _TRUNCATE); return static_cast<int>(strlen(text));
}

int __cdecl RTX_TestTouch() {
    std::lock_guard<std::mutex> lock(guard);
    if (instance || session || queued || touchActions.set) return -1;
    struct Restore {
        ~Restore() {
            touchActions = {}; touchApi = {}; touchFixture = {}; touchErrorLogs = 0;
            state = XR_SESSION_STATE_UNKNOWN; running = false;
            PublishTouch({}); PublishFlat({});
            firstFocusedTracking = false; trackingOriginRevision.store(0); pendingLocalSpaceChanges.clear();
        }
    } restore;
    try {
        const auto require = [](bool ok, const char* message) { if (!ok) throw std::runtime_error(message); };
        require(!AdvanceTrackingOrigin(100, false, true) && RTX_GetTrackingOriginRevision() == 0,
            "An unfocused startup pose must not establish the seated tracking origin");
        require(!AdvanceTrackingOrigin(101, true, false) && RTX_GetTrackingOriginRevision() == 0,
            "An inferred but untracked head must not establish the seated tracking origin");
        require(AdvanceTrackingOrigin(102, true, true) && RTX_GetTrackingOriginRevision() == 1,
            "First focused tracked pose must reset the provisional panel and reference");
        require(!AdvanceTrackingOrigin(103, true, true) && RTX_GetTrackingOriginRevision() == 1,
            "Ordinary head movement must not keep recentering the menu");
        pendingLocalSpaceChanges.push_back(200);
        require(!AdvanceTrackingOrigin(199, true, true) && RTX_GetTrackingOriginRevision() == 1,
            "Future reference-space event must not capture the old coordinate origin");
        require(AdvanceTrackingOrigin(200, true, true) && RTX_GetTrackingOriginRevision() == 2 && pendingLocalSpaceChanges.empty(),
            "Reference-space change is applied at its predicted display time");
        require(!AdvanceTrackingOrigin(201, true, true) && RTX_GetTrackingOriginRevision() == 2,
            "Reference-space change must not be applied twice");
        pendingLocalSpaceChanges.push_back(300);
        require(AdvanceTrackingOrigin(300, false, true) && RTX_GetTrackingOriginRevision() == 0,
            "A reference change while unfocused must wait for the next tracked user pose");
        require(AdvanceTrackingOrigin(301, true, true) && RTX_GetTrackingOriginRevision() == 3,
            "Regaining focus adopts the changed origin once, rather than preserving a provisional height");
        XrPosef initialLookingDown{{-.3007058f,0,0,.9537169f}, {.1f,1.25f,-.2f}}, levelAnchor{}, levelPanel{};
        bool levelAnchored = false; float levelDistance = 0;
        UpdatePanelAnchor(levelAnchor, levelPanel, levelAnchored, levelDistance, initialLookingDown, 2.f, false);
        require(levelAnchored && std::abs(levelPanel.position.y-1.25f) < .00001f && levelPanel.orientation.x == 0 && levelPanel.orientation.z == 0,
            "Automatic first menu stays upright at seated eye height when headset starts looking down");
        UpdatePanelAnchor(levelAnchor, levelPanel, levelAnchored, levelDistance, initialLookingDown, 2.f, true);
        require(std::abs(levelPanel.position.y-1.25f) > .5f && levelPanel.orientation.x < -.3f,
            "Explicit recenter still follows intentional head pitch");
        XrPosef anchor{}, placed{}, firstHead{{0,0,0,1}, {1,2,3}}, movedHead{{0,.70710678f,0,.70710678f}, {8,9,10}};
        bool anchored = false; float distance = 0;
        UpdatePanelAnchor(anchor, placed, anchored, distance, firstHead, 2.f, false);
        require(anchored && placed.position.x == 1 && placed.position.y == 2 && placed.position.z == 1, "Flat anchor initial position");
        const auto initialPanel = placed;
        UpdatePanelAnchor(anchor, placed, anchored, distance, movedHead, 2.f, false);
        require(memcmp(&placed, &initialPanel, sizeof(placed)) == 0, "Flat panel must not follow subsequent head motion");
        UpdatePanelAnchor(anchor, placed, anchored, distance, movedHead, 3.f, false);
        require(placed.position.x == 1 && placed.position.y == 2 && placed.position.z == 0 && placed.orientation.w == 1,
            "Changing flat distance must move along the original anchor, not the current head");
        UpdatePanelAnchor(anchor, placed, anchored, distance, movedHead, 2.f, true);
        require(std::abs(placed.position.x - 6) < .00001f && placed.position.y == 9 && std::abs(placed.position.z - 10) < .00001f,
            "Explicit flat recenter must adopt the current head orientation and position");
        {
            // Exercise the production Trace -> Log -> fflush path on a private
            // temporary stream, with no OpenXR instance, render work or game.
            struct TraceFixture {
                FILE* previousLog = logfile;
                bool previousOwned = ownLogfile;
                FILE* stream = nullptr;
                uint64_t previousBegun = stats.begun;
                bool previousEnabled = diagnosticsEnabled.load(std::memory_order_relaxed);
                bool previousLogEnabled = logEnabled.load(std::memory_order_relaxed);
                std::string previousError = lastError;
                TraceFixture() {
                    if (tmpfile_s(&stream) != 0 || !stream) throw std::runtime_error("Diagnostic test: temporary log unavailable");
                    logfile = stream; ownLogfile = false;
                }
                ~TraceFixture() {
                    logfile = previousLog; ownLogfile = previousOwned; fclose(stream); stats.begun = previousBegun;
                    diagnosticsEnabled.store(previousEnabled, std::memory_order_relaxed);
                    logEnabled.store(previousLogEnabled, std::memory_order_relaxed);
                    lastError.swap(previousError);
                }
            } traceFixture;
            RTX_SetLogEnabled(0);
            Log("suppressed by master"); Error(std::runtime_error("master-disabled error"));
            require(ftell(logfile) == 0 && lastError == "master-disabled error", "Master OFF must silence logs and preserve functional error state");
            RTX_SetLogEnabled(1);
            RTX_SetDiagnosticsEnabled(0);
            stats.begun = 1; Trace("suppressed startup");
            stats.begun = 300; Trace("suppressed periodic");
            require(ftell(logfile) == 0, "Diagnostic test: OFF wrote a startup/periodic breadcrumb");
            require(Error(std::runtime_error("diagnostic fixture error")) == -1 && ftell(logfile) > 0,
                "Diagnostic test: OFF suppressed an error");
            const long errorEnd = ftell(logfile);
            Log("diagnostic fixture runtime state change");
            require(ftell(logfile) > errorEnd, "Diagnostic test: OFF suppressed a runtime state log");
            const long stateEnd = ftell(logfile);
            RTX_SetDiagnosticsEnabled(1); Trace("enabled periodic");
            require(ftell(logfile) > stateEnd, "Diagnostic test: ON did not resume eligible periodic breadcrumbs");
            const long periodicEnd = ftell(logfile);
            stats.begun = 301; Trace("ordinary frame");
            require(ftell(logfile) == periodicEnd, "Diagnostic test: ON removed the periodic rate limit");
            RTX_SetDiagnosticsEnabled(0); stats.begun = 600; Trace("suppressed again");
            require(ftell(logfile) == periodicEnd, "Diagnostic test: second OFF retained tracing");
            RTX_SetDiagnosticsEnabled(-1); stats.begun = 1; Trace("enabled startup");
            require(ftell(logfile) > periodicEnd, "Diagnostic test: nonzero ON did not resume startup breadcrumbs");
        }
        printf("PASS Diagnostics: atomic OFF/ON gate, no startup/periodic writes while OFF, errors/state logs retained, original trace cadence, preference restored\n");
        touchApi = {TestTouchSync, TestTouchPose, TestTouchFloat, TestTouchVector, TestTouchBoolean, TestTouchLocate};
        touchActions.ready = true; touchActions.hands[0] = 101; touchActions.hands[1] = 102;
        touchActions.aimSpaces[0] = reinterpret_cast<XrSpace>(uintptr_t(501));
        touchActions.aimSpaces[1] = reinterpret_cast<XrSpace>(uintptr_t(502));
        touchActions.gripSpaces[0] = reinterpret_cast<XrSpace>(uintptr_t(503));
        touchActions.gripSpaces[1] = reinterpret_cast<XrSpace>(uintptr_t(504));
        running = true; state = XR_SESSION_STATE_FOCUSED;
        TouchFrame value{}; const TouchHand neutral{};
        UpdateTouchSnapshot(41, touchFixture.expectedTime, true);
        require(RTX_GetTouch(&value) == 1 && value.serial == 41 && value.predictedDisplayTime == touchFixture.expectedTime && value.focused == 1,
            "Touch test: snapshot serial/time/focus mismatch");
        require(value.left.activeControls == 1023 && value.right.activeControls == (511 ^ TouchMenu) &&
            value.left.buttons == 31 && value.right.buttons == (15 ^ TouchMenuButton), "Touch test: asymmetric X/Y/A/B/menu/thumbrest binding state");
        require(value.left.aimFlags == 15 && value.right.gripFlags == 15 && value.left.aim.x == -.25f && value.right.grip.x == .35f &&
            value.left.trigger == .75f && value.right.squeeze == .25f && value.right.stickX == -.6f && value.left.stickY == .8f,
            "Touch test: independent hand pose/analog fields");
        require(touchFixture.syncCalls == 1 && touchFixture.stateCalls == 18 && touchFixture.locateCalls == 4 && !touchFixture.wrongTime,
            "Touch test: unbounded or wrong-time action sampling");

        touchFixture.left = false;
        UpdateTouchSnapshot(42, touchFixture.expectedTime, true); RTX_GetTouch(&value);
        require(memcmp(&value.left, &neutral, sizeof(neutral)) == 0 && value.right.buttons != 0,
            "Touch test: disconnected hand retains a pressed control or clears the other hand");
        touchFixture = {}; touchFixture.invalidPose = true;
        UpdateTouchSnapshot(43, touchFixture.expectedTime, true); RTX_GetTouch(&value);
        require(memcmp(&value.left, &neutral, sizeof(neutral)) == 0 && memcmp(&value.right, &neutral, sizeof(neutral)) == 0,
            "Touch test: lost poses retain controls or last coordinates");

        touchFixture = {}; touchFixture.invalidValues = true;
        UpdateTouchSnapshot(44, touchFixture.expectedTime, true); RTX_GetTouch(&value);
        require(value.left.trigger == 0 && value.left.squeeze == 0 && value.right.stickX == 0 && value.right.stickY == 0 &&
            !(value.left.activeControls & (TouchTrigger | TouchSqueeze | TouchStick)), "Touch test: nonfinite analog input escaped");

        touchFixture = {}; touchFixture.syncResult = XR_SESSION_NOT_FOCUSED;
        UpdateTouchSnapshot(45, touchFixture.expectedTime, true); RTX_GetTouch(&value);
        require(value.focused == 0 && value.lastResult == XR_SESSION_NOT_FOCUSED && value.left.buttons == 0 && touchFixture.stateCalls == 0,
            "Touch test: positive NOT_FOCUSED result treated as active input");
        touchFixture = {}; touchFixture.readResult = XR_ERROR_RUNTIME_FAILURE;
        UpdateTouchSnapshot(46, touchFixture.expectedTime, true); RTX_GetTouch(&value);
        require(value.focused == 0 && value.lastResult == XR_ERROR_RUNTIME_FAILURE && value.left.activeControls == 0 && value.right.activeControls == 0,
            "Touch test: failed action query published a partially held hand");

        touchFixture = {}; state = XR_SESSION_STATE_VISIBLE;
        UpdateTouchSnapshot(47, touchFixture.expectedTime, true); RTX_GetTouch(&value);
        require(value.focused == 0 && value.left.buttons == 0 && touchFixture.syncCalls == 0, "Touch test: unfocused session synchronized actions");
        state = XR_SESSION_STATE_FOCUSED;
        UpdateTouchSnapshot(48, touchFixture.expectedTime, false); RTX_GetTouch(&value);
        require(value.focused == 0 && value.left.buttons == 0 && touchFixture.syncCalls == 0, "Touch test: unrenderable head frame retained input");
        UpdateTouchSnapshot(49, touchFixture.expectedTime, true); RTX_GetTouch(&value);
        require(value.focused == 1 && value.serial == 49 && value.left.buttons == 31, "Touch test: focus reconnect did not produce a fresh snapshot");

        FlatPanel shown{49, {0,0,0,1,.1f,1.2f,-2.f}, 3.4f, 1.9125f, 1, 1}, panel{};
        PublishFlat(shown);
        require(RTX_GetFlatPanel(&panel) == 1 && memcmp(&shown, &panel, sizeof(panel)) == 0, "Touch test: flat panel snapshot ABI mismatch");
        PublishFlat({});
        require(RTX_GetFlatPanel(&panel) == 0 && panel.serial == 0 && panel.valid == 0, "Touch test: flat panel invalidation retains old pose");
        require(RTX_GetTouch(nullptr) == -1 && RTX_GetFlatPanel(nullptr) == -1, "Touch test: null getter outputs");

        std::atomic<bool> snapshotHeld{false}, snapshotRead{false};
        std::thread holder([&] {
            std::lock_guard<std::mutex> snapshotLock(snapshotGuard);
            snapshotHeld = true;
            // Bounded even if a future getter mistakenly changes try-lock to a
            // blocking lock: that regression returns a valid snapshot and fails.
            const auto limit = TimingClock::now() + std::chrono::seconds(1);
            while (!snapshotRead.load() && TimingClock::now() < limit) std::this_thread::sleep_for(std::chrono::milliseconds(1));
        });
        while (!snapshotHeld.load()) std::this_thread::yield();
        const int touchBusy = RTX_GetTouch(&value), panelBusy = RTX_GetFlatPanel(&panel);
        snapshotRead = true;
        holder.join();
        require(touchBusy == 0 && panelBusy == -1 && value.serial == 0 && value.focused == 0 && panel.valid == 0,
            "Touch test: snapshot contention waits or retains stale output");
        printf("PASS Touch: 208/88-byte snapshot, serial/time, 17 state queries + 4 poses, focus/loss/reconnect, inactive hands, neutral errors, finite axes, nonblocking getters, 52-byte flat panel distinguishes busy -1 from invalid 0\n");
        return 1;
    } catch (const std::exception& error) { return Error(error); }
}

int __cdecl RTX_TestFlatHands() {
    std::lock_guard<std::mutex> lock(guard);
    if (instance || session || queued) return -1;
    const float savedWidth = requestedFlatWidthRatio.load();
    try {
        const auto require = [](bool ok, const char* message) { if (!ok) throw std::runtime_error(message); };
        Chain quad, handPair[2];
        quad.handle = reinterpret_cast<XrSwapchain>(static_cast<uintptr_t>(11)); quad.width = 1920; quad.height = 1080;
        handPair[0].handle = reinterpret_cast<XrSwapchain>(static_cast<uintptr_t>(12));
        handPair[1].handle = reinterpret_cast<XrSwapchain>(static_cast<uintptr_t>(13));
        for (auto& chain : handPair) chain.width = chain.height = 1024;
        XrPosef pose{{.1f, .2f, .3f, .9f}, {.4f, 1.1f, -2.2f}};
        const auto space = reinterpret_cast<XrSpace>(static_cast<uintptr_t>(21));
        std::array<XrView, 2> stereoViews{};
        stereoViews[0].pose = {{0,0,0,1}, {-.032f, 1.2f, -.1f}};
        stereoViews[1].pose = {{0,0,0,1}, {.032f, 1.2f, -.1f}};
        stereoViews[0].fov = {-1.f, .7f, .9f, -.8f}; stereoViews[1].fov = {-.7f, 1.f, .9f, -.8f};
        FrameLayers flatOnly;
        flatOnly.AddFlat(quad, space, pose, 2.f, 1.0069196f);
        const auto before = flatOnly.Panel(77, true);
        require(flatOnly.count == 1 && flatOnly.hasFlat && flatOnly.layers[0]->type == XR_TYPE_COMPOSITION_LAYER_QUAD,
            "Flat hand test: original flat-only layer changed");
        require(std::abs(before.width - 2.0138392f) < .00001f && std::abs(before.height - 1.1327846f) < .00001f &&
            before.serial == 77 && before.flipVertical == 1 && before.valid == 1,
            "Flat hand test: configured panel dimensions/snapshot mismatch");
        flatOnly.AddProjection(handPair, space, stereoViews);
        const auto after = flatOnly.Panel(77, true);
        require(memcmp(&before, &after, sizeof(before)) == 0 && flatOnly.count == 2 && flatOnly.hasFlat &&
            flatOnly.layers[0]->type == XR_TYPE_COMPOSITION_LAYER_QUAD && flatOnly.layers[1]->type == XR_TYPE_COMPOSITION_LAYER_PROJECTION,
            "Flat hand test: hand layer altered panel picking or composition order");
        require(flatOnly.projection.layerFlags == XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT &&
            flatOnly.projection.space == space && flatOnly.projection.viewCount == 2,
            "Flat hand test: transparent stereo layer contract mismatch");
        for (int i = 0; i < 2; ++i)
            require(memcmp(&flatOnly.projected[i].pose, &stereoViews[i].pose, sizeof(XrPosef)) == 0 &&
                memcmp(&flatOnly.projected[i].fov, &stereoViews[i].fov, sizeof(XrFovf)) == 0 &&
                flatOnly.projected[i].subImage.swapchain == handPair[i].handle && flatOnly.projected[i].subImage.imageRect.extent.width == 1024,
                "Flat hand test: asymmetric stereo pose/FOV or independent hand target mismatch");
        FrameLayers world; world.AddProjection(handPair, space, stereoViews);
        D3D11_TEXTURE2D_DESC newHandSize{}; newHandSize.Width = 2176; newHandSize.Height = 2304; newHandSize.SampleDesc.Count = 1;
        require(ValidFlatHandDimensions(newHandSize), "Full-resolution 2176x2304 resolved flat hands must be accepted");
        newHandSize.Width = newHandSize.Height = 4096;
        require(ValidFlatHandDimensions(newHandSize), "Flat hands accept the advertised4096 texture cap");
        newHandSize.SampleDesc.Count = 4;
        require(!ValidFlatHandDimensions(newHandSize), "MSAA hands must be resolved before native OpenXR copy");
        newHandSize.SampleDesc.Count = 1; newHandSize.Width = 4097;
        require(!ValidFlatHandDimensions(newHandSize), "Oversized hand textures remain bounded");
        const auto absent = world.Panel(78, false);
        require(world.count == 1 && !world.hasFlat && world.projection.layerFlags == 0 && !absent.valid && !absent.serial,
            "Flat hand test: original opaque stereo or panel invalidation changed");
        const XrPosef hudHead{{0,0,0,1},{.12f,1.5f,-.3f}};
        world.AddHud(quad, space, hudHead, 3.2f, 2.8f, 1.8f, .2f, -.4f);
        const auto hudPanel = world.Panel(79, true);
        require(world.count == 2 && !world.hasFlat && !hudPanel.valid && !hudPanel.serial &&
            world.layers[0]->type == XR_TYPE_COMPOSITION_LAYER_PROJECTION &&
            world.layers[1]->type == XR_TYPE_COMPOSITION_LAYER_QUAD,
            "HUD quad must follow the world pair without enabling flat-mode picking");
        require(world.hud.layerFlags == XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT &&
            !(world.hud.layerFlags & XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT) &&
            world.hud.eyeVisibility == XR_EYE_VISIBILITY_BOTH && world.hud.space == space &&
            world.hud.subImage.swapchain == quad.handle && world.hud.size.width == 3.2f && world.hud.size.height == 2.8f,
            "HUD must use premultiplied source alpha and its independent full-resolution surface");
        require(std::abs(world.hud.pose.position.x - .32f) < .00001f &&
            std::abs(world.hud.pose.position.y - 1.1f) < .00001f &&
            std::abs(world.hud.pose.position.z + 2.1f) < .00001f,
            "HUD head-relative geometry must retain vertical/horizontal placement and physical distance");
        const float sine45 = std::sqrt(.5f);
        UiRegion81 regions81[6]{};
        for(int i=0;i<6;++i)regions81[i]={int32_t(i%3*640+4),int32_t(i/3*540+4),632,532,1.2f,.8f,.5f+i*.25f,.1f*i,-.05f*i};
        FrameLayers atlas81;atlas81.AddProjection(handPair,space,stereoViews);
        atlas81.AddUiRegions81(quad,space,hudHead,regions81,6);
        require(sizeof(UiRegion81)==36&&atlas81.count==7,"UI atlas ABI and six independent planes");
        for(int i=0;i<6;++i){const auto& panel=atlas81.uiQuads81[i];const auto& r=regions81[i];
            require(panel.subImage.imageRect.offset.x==r.x&&panel.subImage.imageRect.offset.y==r.y&&panel.subImage.imageRect.extent.width==r.pixelsWide&&panel.subImage.imageRect.extent.height==r.pixelsHigh,"UI atlas region pixels preserved");
            require(std::abs(panel.pose.position.z-(hudHead.position.z-r.distance))<.00001f&&std::abs(panel.pose.position.x-(hudHead.position.x+r.offsetX))<.00001f&&panel.size.width==r.width&&panel.eyeVisibility==XR_EYE_VISIBILITY_BOTH,"UI atlas physical depth and offsets preserved");
            require(atlas81.layers[i+1]==reinterpret_cast<const XrCompositionLayerBaseHeader*>(&panel),"UI atlas layer order preserved");}
        bool badRegion81=false;regions81[0].x=1900;
        try{FrameLayers invalid;invalid.AddUiRegions81(quad,space,hudHead,regions81,1);}catch(const std::runtime_error&){badRegion81=true;}
        require(badRegion81,"UI atlas rejects out-of-texture rectangle");
        printf("PASS UI81: six atlas subrectangles, independent depths, offsets, ABI, layer order and invalid rectangle rejection\n");
        const XrPosef rotatedHead{{0,sine45,0,sine45},{0,1.5f,0}};
        FrameLayers rotatedHud;
        rotatedHud.AddHud(quad, space, rotatedHead, 3, 2, 2, .3f, -.5f);
        require(std::abs(rotatedHud.hud.pose.position.x + 2) < .00001f &&
            std::abs(rotatedHud.hud.pose.position.y - 1) < .00001f &&
            std::abs(rotatedHud.hud.pose.position.z + .3f) < .00001f &&
            rotatedHud.hud.pose.orientation.y == sine45,
            "HUD placement must rotate with the measured head, not with desktop or table orientation");
        const auto hudBeforeSpatial = world.hud;
        const auto projectionBeforeSpatial = world.projected[0];
        const XrPosef wheelPose{{0,sine45,0,sine45},{-.13f,.82f,-.72f}};
        world.AddSpatial(quad, space, wheelPose, .264f, .44f);
        require(world.count == 3 && world.layers[0]->type == XR_TYPE_COMPOSITION_LAYER_PROJECTION &&
            world.layers[1] == reinterpret_cast<const XrCompositionLayerBaseHeader*>(&world.hud) &&
            world.layers[2] == reinterpret_cast<const XrCompositionLayerBaseHeader*>(&world.spatial),
            "Spatial wheel must compose after world and HUD, never replace either eye or HUD");
        require(world.spatial.space == space && world.spatial.eyeVisibility == XR_EYE_VISIBILITY_BOTH &&
            world.spatial.layerFlags == XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT &&
            !(world.spatial.layerFlags & XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT),
            "Spatial layer requires both-eye geometry and premultiplied source alpha");
        require(memcmp(&world.spatial.pose, &wheelPose, sizeof(wheelPose)) == 0 &&
            world.spatial.size.width == .264f && world.spatial.size.height == .44f &&
            world.spatial.subImage.swapchain == quad.handle,
            "Spatial physical pose/dimensions must be exactly independent of HUD distance, aspect and offset");
        require(memcmp(&world.hud, &hudBeforeSpatial, sizeof(hudBeforeSpatial)) == 0 &&
            memcmp(&world.projected[0], &projectionBeforeSpatial, sizeof(projectionBeforeSpatial)) == 0 &&
            !world.hasFlat && !world.Panel(80, false).valid,
            "Spatial UI must preserve native stereo/HUD geometry and must not activate desktop picking");
        const auto movedFlat = OffsetPose(pose, .3f, -.7f, 0);
        FrameLayers atlas;
        const XrPosef infoPose{{0,0,0,1},{.1f,1.6f,-.72f}};
        atlas.AddSpatialAtlas(quad,space,wheelPose,.39f,.39f,true,infoPose,.66f);
        require(atlas.count==2 && atlas.spatial.subImage.swapchain==atlas.information.subImage.swapchain &&
            atlas.spatial.subImage.imageRect.offset.y==static_cast<int32_t>(quad.height/2) &&
            atlas.spatial.subImage.imageRect.extent.height==static_cast<int32_t>(quad.height/2) &&
            atlas.information.subImage.imageRect.offset.y==0 &&
            atlas.information.subImage.imageRect.extent.height==static_cast<int32_t>(quad.height/2),
            "Spatial atlas uses non-overlapping wheel-bottom and original-info-top rectangles of one swapchain");
        require(memcmp(&atlas.spatial.pose,&wheelPose,sizeof(wheelPose))==0 &&
            memcmp(&atlas.information.pose,&infoPose,sizeof(infoPose))==0 && atlas.information.size.width==.66f &&
            atlas.information.eyeVisibility==XR_EYE_VISIBILITY_BOTH &&
            atlas.information.layerFlags==XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT,
            "Original information has an independent centred pose and premultiplied coverage for both eyes");
        FrameLayers wideAtlas;
        wideAtlas.AddSpatialAtlas(quad,space,wheelPose,.52f,.39f,true,infoPose,.88f);
        require(std::abs(wideAtlas.information.size.width-.88f)<.00001f &&
            std::abs(wideAtlas.information.size.height-.66f)<.00001f &&
            std::abs(wideAtlas.spatial.size.width-.52f)<.00001f &&
            std::abs(wideAtlas.spatial.size.height-.39f)<.00001f,
            "Wider control atlas preserves native card and wheel height without stretching");
        FrameLayers wheelOnly;
        wheelOnly.AddSpatialAtlas(quad,space,wheelPose,.39f,.39f,false,infoPose,.66f);
        require(wheelOnly.count==1 && wheelOnly.spatial.subImage.imageRect.extent.height==static_cast<int32_t>(quad.height/2),
            "Closing information leaves only the fixed wheel half without a stale info layer");
        FrameLayers management;
        management.AddFlat(quad,space,pose,1.25f,1.2f,1.5f);
        auto managementPanel=management.Panel(91,false);
        require(std::abs(management.flat.size.width-1.5f)<.00001f && std::abs(management.flat.size.height-1.f)<.00001f &&
            managementPanel.width==management.flat.size.width && managementPanel.height==management.flat.size.height,
            "Manual management aspect and its native Touch picking snapshot share the exact displayed rectangle");
        const float savedAspect=requestedFlatAspect.load();
        RTX_SetFlatPanelAspect(.1f);require(requestedFlatAspect.load()==.8f,"Management aspect lower bound");
        RTX_SetFlatPanelAspect(9);require(requestedFlatAspect.load()==2.4f,"Management aspect upper bound");
        RTX_SetFlatPanelAspect(0);require(requestedFlatAspect.load()==0,"Original management aspect preserved");
        RTX_SetFlatPanelAspect(std::numeric_limits<float>::quiet_NaN());require(requestedFlatAspect.load()==0,"Invalid management aspect is original");
        requestedFlatAspect.store(savedAspect);
        const auto movedFlatAgain = OffsetPose(pose, .3f, -.7f, 0);
        require(memcmp(&movedFlat, &movedFlatAgain, sizeof(movedFlat)) == 0,
            "Flat adjustments must be derived from the stable anchor and never accumulate");
        const float savedX = requestedFlatOffsetX.load(), savedY = requestedFlatOffsetY.load();
        RTX_SetFlatPanelOffset(-3, 5);
        require(requestedFlatOffsetX.load() == -.65f && requestedFlatOffsetY.load() == .65f,
            "Flat displacement must allow the same expanded range as the HUD");
        RTX_SetFlatPanelOffset(std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::infinity());
        require(requestedFlatOffsetX.load() == 0 && requestedFlatOffsetY.load() == 0, "Invalid flat offset must become neutral");
        requestedFlatOffsetX.store(savedX); requestedFlatOffsetY.store(savedY);
        RTX_SetFlatPanelWidth(.001f); require(requestedFlatWidthRatio.load() == .01f, "Flat width lower bound");
        RTX_SetFlatPanelWidth(.3f); require(requestedFlatWidthRatio.load() == .3f, "Narrow fitted flat panel is not widened");
        RTX_SetFlatPanelWidth(4.f); require(requestedFlatWidthRatio.load() == 4.f, "Wide fitted flat panel is not clipped at ninety degrees");
        RTX_SetFlatPanelWidth(16.f); require(requestedFlatWidthRatio.load() == 8.f, "Flat width upper bound");
        RTX_SetFlatPanelWidth(std::numeric_limits<float>::quiet_NaN());
        require(requestedFlatWidthRatio.load() == 1.7f, "Invalid flat width fallback");
        requestedFlatWidthRatio.store(savedWidth);
        printf("PASS FlatHands: retained quad/picking, ordered alpha stereo overlay, eye poses/FOV, independent full-resolution premultiplied HUD, rotated HUD pose, bounded size/offset, stable flat anchor\n");
        return 1;
    } catch (const std::exception& error) { requestedFlatWidthRatio.store(savedWidth); return Error(error); }
}
int __cdecl RTX_TestBlitter(void* testDevice) {
    std::lock_guard<std::mutex> lock(guard);
    if (instance || session || queued || !testDevice) return -1;
    // Standalone GPU check using the production blitter. No runtime, game or
    // headset is involved; inputs and expected pixels are independent fixtures.
    try {
        device = reinterpret_cast<ID3D11Device*>(testDevice);
        ProtectDevice();
        CreateBlitter();
        D3D11_TEXTURE2D_DESC desc{};
        desc.Width = desc.Height = 8; desc.MipLevels = desc.ArraySize = 1;
        desc.SampleDesc.Count = 1; desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
        desc.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
        std::array<uint32_t,64> pixels{};
        const uint32_t red=0xff0000ff, green=0xff00ff00, blue=0xffff0000, gray=0xff808080;
        for (int y=0; y<8; ++y) for (int x=0; x<8; ++x)
            pixels[y*8+x] = y<4 ? (x<4 ? red : green) : (x<4 ? blue : gray);
        pixels[0] = 0; pixels[3*8+3] = 0x40402010; // Transparent clear and partial alpha survive the hand-layer copy.
        D3D11_SUBRESOURCE_DATA initial{pixels.data(),32,0};
        ComPtr<ID3D11Texture2D> source, targets[2], readback;
        Hr(device->CreateTexture2D(&desc, &initial, &source), "test source");
        Chain output[2];
        for (int i=0; i<2; ++i) {
            Hr(device->CreateTexture2D(&desc, nullptr, &targets[i]), "test target");
            output[i].width = output[i].height = 8; output[i].targets.resize(1);
            Hr(device->CreateRenderTargetView(targets[i].Get(), nullptr, &output[i].targets[0]), "test RTV");
        }
        desc.Usage = D3D11_USAGE_STAGING; desc.BindFlags = 0; desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        Hr(device->CreateTexture2D(&desc, nullptr, &readback), "test staging");
        D3D11_VIEWPORT original{7,9,31,27,0.2f,0.8f};
        context->RSSetViewports(1, &original);
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_LINESTRIP);
        ID3D11RenderTargetView* oldTarget = output[0].targets[0].Get();
        context->OMSetRenderTargets(1, &oldTarget, nullptr);
        {
            ContextScope isolate;
            DrawTexture(output[0], 0, source.Get(), {0,0,1,1}, false);
            DrawTexture(output[1], 0, source.Get(), {0,0,0.5f,1}, true);
        }
        uint32_t count = 1; D3D11_VIEWPORT restored{};
        context->RSGetViewports(&count, &restored);
        D3D11_PRIMITIVE_TOPOLOGY topology{}; context->IAGetPrimitiveTopology(&topology);
        ComPtr<ID3D11RenderTargetView> restoredTarget;
        context->OMGetRenderTargets(1, &restoredTarget, nullptr);
        if (count != 1 || memcmp(&original, &restored, sizeof(original)) ||
            topology != D3D11_PRIMITIVE_TOPOLOGY_LINESTRIP || restoredTarget.Get() != oldTarget)
            throw std::runtime_error("GPU test: caller's D3D11 state was not restored");
        struct BatchMock { int acquire=0, wait=0, release=0, failWait=0; };
        static BatchMock batchMock;
        batchMock = {};
        const auto savedSwapchainApi = swapchainApi;
        struct RestoreBatchApi { SwapchainApi previous; ~RestoreBatchApi() { swapchainApi=previous; } } restoreBatch{savedSwapchainApi};
        swapchainApi.acquire = [](XrSwapchain, const XrSwapchainImageAcquireInfo*, uint32_t* index) -> XrResult {
            ++batchMock.acquire; *index=0; return XR_SUCCESS;
        };
        swapchainApi.wait = [](XrSwapchain, const XrSwapchainImageWaitInfo*) -> XrResult {
            ++batchMock.wait;
            return batchMock.failWait && batchMock.wait == batchMock.failWait ? XR_TIMEOUT_EXPIRED : XR_SUCCESS;
        };
        swapchainApi.release = [](XrSwapchain, const XrSwapchainImageReleaseInfo*) -> XrResult {
            ++batchMock.release; return XR_SUCCESS;
        };
        const auto flushesBefore = blitFlushes, viewsBefore = sourceViewsCreated;
        activeRenderTiming = {}; activeRenderTiming.serial=100;
        {
            // A second user of the shared context must never observe our
            // temporary viewport/topology/targets, even between D3D11 calls.
            std::atomic<bool> done{false}, corrupt{false};
            std::atomic<unsigned> observations{0};
            std::thread observer([&] {
                while (!done.load()) {
                    multithread->Enter();
                    UINT n = 1; D3D11_VIEWPORT viewport{};
                    D3D11_PRIMITIVE_TOPOLOGY primitive{};
                    ComPtr<ID3D11RenderTargetView> target;
                    context->RSGetViewports(&n, &viewport);
                    context->IAGetPrimitiveTopology(&primitive);
                    context->OMGetRenderTargets(1, &target, nullptr);
                    if (n != 1 || memcmp(&original, &viewport, sizeof(original)) ||
                        primitive != D3D11_PRIMITIVE_TOPOLOGY_LINESTRIP || target.Get() != oldTarget)
                        corrupt = true;
                    context->Flush();
                    multithread->Leave();
                    ++observations;
                    std::this_thread::yield();
                }
            });
            struct JoinObserver {
                std::atomic<bool>& stop; std::thread& thread;
                ~JoinObserver() { stop = true; if (thread.joinable()) thread.join(); }
            } join{done, observer};
            while (!observations.load()) std::this_thread::yield();
            for (int frame=0; frame<1000; ++frame) {
                BlitPair(output[0],source.Get(),{0,0,1,1},false,output[1],source.Get(),{0,0,.5f,1},true);
                std::this_thread::yield();
            }
            done = true; observer.join();
            if (corrupt.load() || !multithread->GetMultithreadProtected())
                throw std::runtime_error("GPU test: concurrent user observed isolated blitter state");
            printf("PASS GPU concurrency: 1000 stereo pairs; %u protected context observations\n", observations.load());
        }
        if (batchMock.acquire != 2000 || batchMock.wait != 2000 || batchMock.release != 2000 ||
            blitFlushes-flushesBefore != 1000 || sourceViewsCreated != viewsBefore)
            throw std::runtime_error("GPU batch test: copies must use one Flush per pair and reuse both source views");
        if (activeRenderTiming.images!=2000 || activeRenderTiming.acquireWaitMs<0 ||
            activeRenderTiming.drawFlushMs<=0 || activeRenderTiming.releaseMs<0)
            throw std::runtime_error("GPU timing test: batch phases/images not recorded");
        {
            std::lock_guard<std::mutex> snapshotLock(snapshotGuard);
            renderTimingSnapshot=activeRenderTiming; renderTimingSnapshot.completed=1;
        }
        RenderTiming readTiming{};
        if (RTX_GetRenderTiming(&readTiming)!=1 || readTiming.serial!=100 || readTiming.images!=2000)
            throw std::runtime_error("GPU timing test: completed immutable snapshot missing");
        RTX_SetDiagnosticsEnabled(0);
        BlitPair(output[0],source.Get(),{0,0,1,1},false,output[1],source.Get(),{0,0,.5f,1},true);
        if (RTX_GetRenderTiming(&readTiming)!=0 || readTiming.serial || activeRenderTiming.images!=2000)
            throw std::runtime_error("GPU timing test: diagnostics OFF still records/returns measurements");
        RTX_SetDiagnosticsEnabled(1); activeRenderTiming={};
        printf("PASS GPU timing: completed serial snapshot, batch phases, diagnostics OFF no samples, ABI64\n");
        {
            const auto generation = copyGeneration, flushes = blitFlushes;
            batchMock = {}; batchMock.failWait=2;
            bool rejected=false;
            try { BlitPair(output[0],source.Get(),{0,0,1,1},false,output[1],source.Get(),{0,0,.5f,1},true); }
            catch (const std::runtime_error&) { rejected=true; }
            if (!rejected || copyGeneration!=generation || blitFlushes!=flushes || batchMock.release!=1)
                throw std::runtime_error("GPU batch test: failed second wait must draw neither eye and release the first ready image");
            batchMock = {};
            ComPtr<ID3D11Texture2D> replacement;
            D3D11_TEXTURE2D_DESC replacementDesc{}; source->GetDesc(&replacementDesc);
            Hr(device->CreateTexture2D(&replacementDesc,&initial,&replacement),"test replacement source");
            BlitPair(output[0],replacement.Get(),{0,0,1,1},false,output[1],replacement.Get(),{0,0,.5f,1},true);
            if (sourceViewsCreated!=viewsBefore+2 || output[0].cachedSource.Get()!=replacement.Get())
                throw std::runtime_error("GPU batch test: source replacement must invalidate both strong-reference caches");
            BlitPair(output[0],source.Get(),{0,0,1,1},false,output[1],source.Get(),{0,0,.5f,1},true);
            printf("PASS GPU batch: one Flush per pair, no repeated SRV creation, replacement invalidation, partial-acquire cleanup\n");
        }
        {
            const auto drain = [] {
                const auto deadline = TimingClock::now() + std::chrono::seconds(5);
                while (!CopiesComplete()) {
                    if (TimingClock::now() > deadline) throw std::runtime_error("GPU test: retirement event did not drain without host Flush/Present");
                    std::this_thread::yield();
                }
            };
            if (CopiesComplete()) throw std::runtime_error("GPU test: first retirement request did not arm an event");
            drain();
            if (!CopiesComplete()) throw std::runtime_error("GPU test: completed retirement was not reusable");
            {
                ContextScope isolate;
                DrawTexture(output[0], 0, source.Get(), {0,0,1,1}, false);
                DrawTexture(output[1], 0, source.Get(), {0,0,0.5f,1}, true);
            }
            if (CopiesComplete()) throw std::runtime_error("GPU test: new stereo copies reused a stale retirement event");
            drain();
            count = 1; context->RSGetViewports(&count, &restored);
            context->IAGetPrimitiveTopology(&topology);
            context->OMGetRenderTargets(1, &restoredTarget, nullptr);
            if (count != 1 || memcmp(&original, &restored, sizeof(original)) ||
                topology != D3D11_PRIMITIVE_TOPOLOGY_LINESTRIP || restoredTarget.Get() != oldTarget)
                throw std::runtime_error("GPU test: retirement changed caller pipeline state");
            printf("PASS GPU retirement: nonblocking event, no host Flush/Present, fresh event after both-eye copies, preserved context\n");
        }
        context->OMSetRenderTargets(0, nullptr, nullptr);
        const auto nearColor = [](uint32_t actual, uint32_t expected) {
            for (int shift=0; shift<32; shift+=8)
                if (std::abs(int((actual>>shift)&255) - int((expected>>shift)&255)) > 2) return false;
            return true;
        };
        for (int eye=0; eye<2; ++eye) {
            context->CopyResource(readback.Get(), targets[eye].Get());
            D3D11_MAPPED_SUBRESOURCE mapped{};
            Hr(context->Map(readback.Get(), 0, D3D11_MAP_READ, 0, &mapped), "test readback");
            auto pixel = [&](int x,int y) { return *reinterpret_cast<uint32_t*>(static_cast<char*>(mapped.pData)+y*mapped.RowPitch+x*4); };
            bool correct = eye==0 ? nearColor(pixel(1,1),red) && nearColor(pixel(6,1),green) &&
                nearColor(pixel(1,6),blue) && nearColor(pixel(6,6),gray) &&
                nearColor(pixel(0,0),0) && nearColor(pixel(3,3),0x40402010) :
                nearColor(pixel(1,1),blue) && nearColor(pixel(6,1),blue) && nearColor(pixel(1,6),red);
            context->Unmap(readback.Get(), 0);
            if (!correct) throw std::runtime_error("GPU test: crop, orientation, color or eye destination mismatch");
        }
        printf("PASS GPU hand alpha: transparent clear and partial RGBA retained by the production sRGB blitter\n");
        {
            // Independent compositing oracle: color blended over black/white in
            // Unity's active color space, with deliberately WRONG texture alpha
            // (the UI shader squares coverage). Reconstruction must ignore it.
            const auto encode = [](float v) { return v <= .0031308f ? v*12.92f : 1.055f*std::pow(v,1.f/2.4f)-.055f; };
            const auto decode = [](float v) { return v <= .04045f ? v/12.92f : std::pow((v+.055f)/1.055f,2.4f); };
            const auto pack = [](float r,float g,float b,float a) {
                auto byte=[](float v) { return static_cast<uint32_t>(std::lround(std::clamp(v,0.f,1.f)*255)); };
                return byte(r) | byte(g)<<8 | byte(b)<<16 | byte(a)<<24;
            };
            D3D11_TEXTURE2D_DESC hudDesc{}; source->GetDesc(&hudDesc);
            ComPtr<ID3D11Texture2D> black, white;
            Hr(device->CreateTexture2D(&hudDesc,nullptr,&black),"HUD black fixture");
            Hr(device->CreateTexture2D(&hudDesc,nullptr,&white),"HUD white fixture");
            std::array<uint32_t,64> blackPixels{}, whitePixels{}, expected{};
            for (bool linear : {true,false}) {
                for(int y=0;y<8;++y) for(int x=0;x<8;++x) {
                    const float alpha=x<2 ? 0.f : x<4 ? .25f : x<6 ? .65f : 1.f;
                    const float c[3]={y<4 ? .8f:.2f, .35f, y<4 ? .15f:.7f};
                    float b[3],w[3],out[3];
                    for(int k=0;k<3;++k) {
                        b[k]=alpha*c[k]; w[k]=b[k]+1-alpha;
                        out[k]=encode(alpha*(linear ? c[k] : decode(c[k])));
                        if(linear) {b[k]=encode(b[k]);w[k]=encode(w[k]);}
                    }
                    blackPixels[y*8+x]=pack(b[0],b[1],b[2],alpha*alpha);
                    whitePixels[y*8+x]=pack(w[0],w[1],w[2],.17f);
                    expected[y*8+x]=pack(out[0],out[1],out[2],alpha);
                }
                context->UpdateSubresource(black.Get(),0,nullptr,blackPixels.data(),32,0);
                context->UpdateSubresource(white.Get(),0,nullptr,whitePixels.data(),32,0);
                const auto cacheBefore=sourceViewsCreated;
                BlitHud(output[0],black.Get(),white.Get(),false,linear);
                BlitHud(output[1],black.Get(),white.Get(),true,linear);
                const auto warmed=sourceViewsCreated;
                BlitHud(output[0],black.Get(),white.Get(),false,linear);
                if (sourceViewsCreated!=warmed || (linear && warmed-cacheBefore!=4))
                    throw std::runtime_error("HUD GPU test: two source-view caches are not retained");
                for(int eye=0;eye<2;++eye) {
                    context->CopyResource(readback.Get(),targets[eye].Get());
                    D3D11_MAPPED_SUBRESOURCE mapped{};
                    Hr(context->Map(readback.Get(),0,D3D11_MAP_READ,0,&mapped),"HUD readback");
                    bool correct=true;
                    for(int y=0;y<8;++y) for(int x=0;x<8;++x) {
                        const auto actual=*reinterpret_cast<uint32_t*>(static_cast<char*>(mapped.pData)+y*mapped.RowPitch+x*4);
                        const auto want=expected[(eye ? 7-y:y)*8+x];
                        for(int shift=0;shift<32;shift+=8)
                            correct &= std::abs(int((actual>>shift)&255)-int((want>>shift)&255))<=4;
                    }
                    context->Unmap(readback.Get(),0);
                    if(!correct) throw std::runtime_error(linear ? "HUD linear coverage/color/orientation mismatch" : "HUD gamma coverage/color/orientation mismatch");
                }
            }
            printf("PASS GPU HUD: linear/gamma blending, black-white coverage despite squared alpha, opaque/clear/partial pixels, premultiplied sRGB color, both orientations, cached source views\n");
        }
        {
            // Deliberately high-frequency UI, independent of any game asset:
            // transparent/white alternating texels above, opaque blue below.
            // Every reduced level must preserve linear light, coverage and the
            // two atlas halves, with no shimmer-producing alternating samples.
            D3D11_TEXTURE2D_DESC mipDesc{}; source->GetDesc(&mipDesc);
            mipDesc.Width = 8; mipDesc.Height = 16; mipDesc.MipLevels = 1;
            std::array<uint32_t,128> checker{};
            for (int y=0;y<16;++y) for (int x=0;x<8;++x)
                checker[y*8+x] = y < 8 ? ((x+y)&1 ? 0xffffffff : 0) : blue;
            D3D11_SUBRESOURCE_DATA mipInitial{checker.data(),32,0};
            ComPtr<ID3D11Texture2D> mipSource, mipTexture, mipReadback;
            Hr(device->CreateTexture2D(&mipDesc,&mipInitial,&mipSource),"spatial mip test source");
            mipDesc.MipLevels = 3;
            Hr(device->CreateTexture2D(&mipDesc,nullptr,&mipTexture),"spatial mip test destination");
            Chain mipChain; mipChain.width=8; mipChain.height=16; mipChain.mipCount=3;
            mipChain.images.resize(1,{XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR});
            mipChain.images[0].texture=mipTexture.Get(); mipChain.targets.resize(1);
            Hr(device->CreateRenderTargetView(mipTexture.Get(),nullptr,&mipChain.targets[0]),"spatial mip test base RTV");
            CreateChainMipViews(mipChain);
            {
                ContextScope isolate;
                DrawTexture(mipChain,0,mipSource.Get(),{0,0,1,1},false);
            }
            mipDesc.Usage=D3D11_USAGE_STAGING; mipDesc.BindFlags=0; mipDesc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
            Hr(device->CreateTexture2D(&mipDesc,nullptr,&mipReadback),"spatial mip test staging");
            context->CopyResource(mipReadback.Get(),mipTexture.Get());
            for (int mip=1;mip<3;++mip) {
                D3D11_MAPPED_SUBRESOURCE mapped{};
                Hr(context->Map(mipReadback.Get(),mip,D3D11_MAP_READ,0,&mapped),"spatial mip test readback");
                bool correct=true; const int w=8>>mip,h=16>>mip;
                for(int y=0;y<h;++y) for(int x=0;x<w;++x) {
                    uint32_t actual=*reinterpret_cast<uint32_t*>(static_cast<char*>(mapped.pData)+y*mapped.RowPitch+x*4);
                    correct &= nearColor(actual,y<h/2 ? 0x80bcbcbc : blue);
                }
                context->Unmap(mipReadback.Get(),mip);
                if(!correct) throw std::runtime_error("Spatial mip filtering lost linear color, alpha coverage or atlas separation");
            }
            if(SpatialMipCount(2048,4096)!=6 || SpatialMipCount(16,16)!=1)
                throw std::runtime_error("Spatial mip bounds changed");
            printf("PASS GPU spatial mips: production generation, linear-light coverage, minified checker stability, atlas-island separation, no temporal history\n");
        }
        context->ClearState(); context->Flush();
        Destroy(); return 1;
    } catch (const std::exception& error) { Error(error); Destroy(); return -1; }
}
