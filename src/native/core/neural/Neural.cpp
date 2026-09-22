// Contains calls to the official NVIDIA NGX SDK; SDK code retains NVIDIA-LICENSE.txt.
#include "Neural.h"
#include "ColorTransfer.h"
#include "RuntimeIdentity.h"
#include <windows.h>
#include <d3d11_4.h>
#include <wrl/client.h>
#include <nvsdk_ngx.h>
#include <nvsdk_ngx_helpers.h>
#include <nvsdk_ngx_helpers_d3d.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <deque>
#include <filesystem>
#include <fstream>
#include <map>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>
using Microsoft::WRL::ComPtr;
namespace fs=std::filesystem;
namespace {
constexpr size_t MaxJobs=16, MaxReceipts=128;
constexpr char ProjectId[]="21fdbd19-75b2-4993-a664-a20151745504";
struct Error : std::runtime_error { uint32_t reason, ngx; Error(uint32_t r,const std::string& s,uint32_t n=0):std::runtime_error(s),reason(r),ngx(n){} };
struct Job { RTN_Job data{}; uint64_t ticket{}; std::array<ComPtr<ID3D11Texture2D>,5> textures; ComPtr<ID3D11ShaderResourceView> sourceView; ComPtr<ID3D11RenderTargetView> destinationView; };
struct Eye { NVSDK_NGX_Handle* feature{}; NVSDK_NGX_Parameter* parameters{}; ComPtr<ID3D11Texture2D> scratch,linearInput; ComPtr<ID3D11ShaderResourceView> scratchView; ComPtr<ID3D11RenderTargetView> linearInputView; uint64_t lastFrame=UINT64_MAX; uint32_t identifiedPreset=UINT32_MAX; bool presetConflict=false; };
thread_local Eye* creatingEye=nullptr;
thread_local uint32_t creatingMode=UINT32_MAX;
struct Generation {
 uint64_t id{}; uint32_t width{},height{},outWidth{},outHeight{},physicalWidth{},physicalHeight{},mode{},flags{}; DXGI_FORMAT format{};
 std::array<Eye,2> eyes; std::unique_ptr<rtn::ColorTransfer> transfer;
};
struct Retired { ComPtr<ID3D11Query> query; std::unique_ptr<Job> job; std::unique_ptr<Generation> generation; };
std::mutex mutex;
std::mutex logMutex;
std::atomic<uint32_t> ngxLogCount{0};
std::atomic<bool> diagnosticsEnabled{false};
RTN_Config config{};
std::wstring featureDirectory, logPath;
RTN_BackendStatus backend{sizeof(RTN_BackendStatus),RTN_ABI_VERSION};
std::map<uint64_t,std::unique_ptr<Job>> jobs;
std::deque<RTN_JobStatus> receipts;
std::deque<Retired> retired;
std::vector<ComPtr<ID3D11Query>> freeQueries;
uint64_t nextTicket=1, lastSubmitted[2]={UINT64_MAX,UINT64_MAX};
bool stopping=false, shutdownFlushed=false, ngxInitialized=false;
rtn::RuntimeIdentity runtimeIdentity;
ComPtr<ID3D11Device> device;
ComPtr<ID3D11DeviceContext1> context;
ComPtr<ID3D11Multithread> protection;
ComPtr<ID3DDeviceContextState> isolated;
NVSDK_NGX_Parameter* capabilities{};
std::unique_ptr<Generation> current;

void Log(const std::string& s) noexcept { if(!diagnosticsEnabled.load(std::memory_order_relaxed))return;try { std::lock_guard<std::mutex> lock(logMutex);if(diagnosticsEnabled.load(std::memory_order_relaxed)&&!logPath.empty()){std::ofstream f(fs::path(logPath),std::ios::app);f<<s<<'\n';} }catch(...){} }
void Hr(HRESULT hr,const char* name){if(FAILED(hr))throw Error(RTN_REASON_RESOURCE,std::string(name)+" HRESULT="+std::to_string(uint32_t(hr)));}
void Ngx(NVSDK_NGX_Result r,uint32_t reason,const char* name){if(NVSDK_NGX_FAILED(r))throw Error(reason,std::string(name)+" NGX="+std::to_string(uint32_t(r)),uint32_t(r));}
void CapturePreset(const char* s){
 // The SDK has no documented effective-preset getter. This is diagnostic evidence from the
 // verified runtime's own creation log, correlated to this synchronous eye Create call.
 // No log from another thread, RayReconstruction or a different quality mode is accepted.
 if(!s||!creatingEye||!strstr(s,"NgxDltss::FillCreationParams"))return;
 const char* modes[]={"Info: (DLAA) ","Info: (Quality) ","Info: (Balanced) ","Info: (Perf) ","Info: (UltraPerf) "};
 if(creatingMode>RTN_MODE_ULTRA_PERFORMANCE)return;auto line=strstr(s,modes[creatingMode]);if(!line)return;
 const auto end=line+strcspn(line,"\r\n");
 const char* marker="using title default Preset ";auto value=strstr(line,marker);if(value&&value>=end)value=nullptr;
 if(value)value+=strlen(marker);else{marker="Using App hint Preset ";value=strstr(line,marker);if(value&&value<end)value+=strlen(marker);else value=nullptr;}
 if(value&&value>=end)value=nullptr;
 if(!value||value[0]<'J'||value[0]>'M'||(value[1]&&value[1]!='\r'&&value[1]!='\n'&&value[1]!=' '))return;
 uint32_t selected=uint32_t(value[0]-'A'+1);auto& eye=*creatingEye;
 if(eye.identifiedPreset!=UINT32_MAX&&eye.identifiedPreset!=selected)eye.presetConflict=true;
 if(!eye.presetConflict)eye.identifiedPreset=selected;else eye.identifiedPreset=UINT32_MAX;
 Log(std::string("PRESET_RUNTIME_EVIDENCE ")+s);
}
void NVSDK_CONV NgxLogger(const char* s,NVSDK_NGX_Logging_Level,NVSDK_NGX_Feature feature){try{runtimeIdentity.ObserveLog(s,feature==NVSDK_NGX_Feature_SuperSampling);CapturePreset(s);if(s&&ngxLogCount.fetch_add(1)<128)Log(std::string("NGX ")+s);}catch(...){}}
struct PresetCaptureScope { PresetCaptureScope(Eye& e,uint32_t mode){creatingEye=&e;creatingMode=mode;} ~PresetCaptureScope(){creatingEye=nullptr;creatingMode=UINT32_MAX;} };
void RefreshRuntimeIdentity() noexcept {
 try{runtimeIdentity.Refresh();const auto identity=runtimeIdentity.Snapshot();
  backend.runtimeMajor=identity.versionMajor;backend.runtimeMinor=identity.versionMinor;backend.runtimePatch=identity.versionPatch;backend.runtimeBuild=identity.versionBuild;
  Log("Runtime identity="+std::to_string(identity.identity)+" source="+std::to_string(identity.source)+" flags="+std::to_string(identity.flags)+
      " version="+std::to_string(identity.versionMajor)+"."+std::to_string(identity.versionMinor)+"."+std::to_string(identity.versionPatch)+"."+std::to_string(identity.versionBuild)+
      " path="+fs::path(identity.path).u8string()+"; identity/version are advisory, NGX decides support");
 }catch(...){}
}
void Fault(uint32_t reason,uint64_t frame,const std::string& text) noexcept {if(backend.state==RTN_STATE_FAULTED)return;backend.state=RTN_STATE_FAULTED;backend.reason=reason;backend.lastFailureFrame=frame;runtimeIdentity.Error(reason,text);RefreshRuntimeIdentity();runtimeIdentity.End();try{Log("FAULT reason="+std::to_string(reason)+" frame="+std::to_string(frame)+" "+text);}catch(...){}}
int Reject(uint32_t reason,const std::string& text) noexcept {runtimeIdentity.Error(reason,text);try{Log("REJECT reason="+std::to_string(reason)+" "+text);}catch(...){}return 0;}
void Receipt(const Job& j,uint32_t state,uint32_t reason,uint32_t ngx,double ms) noexcept {try{
 receipts.push_back({sizeof(RTN_JobStatus),RTN_ABI_VERSION,j.ticket,j.data.generation,j.data.frame,j.data.eye,state,reason,ngx,ms});
 if(receipts.size()>MaxReceipts)receipts.pop_front();
 }catch(...){} // Diagnostic allocation failure cannot change GPU job lifetime.
}
void DeviceFrom(Job& j){
 ComPtr<ID3D11Device> supplied;j.textures[0]->GetDevice(&supplied);
 if(device){if(device.Get()!=supplied.Get())throw Error(RTN_REASON_RESOURCE,"Unity device changed; stop/drain before reinitializing");return;}
 if(supplied->GetCreationFlags()&D3D11_CREATE_DEVICE_SINGLETHREADED)throw Error(RTN_REASON_UNSUPPORTED,"Single-threaded D3D11 device unsupported");
 ComPtr<ID3D11DeviceContext> immediate;supplied->GetImmediateContext(&immediate);
 ComPtr<ID3D11DeviceContext1> newContext;ComPtr<ID3D11Multithread> newProtection;ComPtr<ID3D11Device1> newer;ComPtr<ID3DDeviceContextState> newState;
 Hr(immediate.As(&newContext),"D3D11 context1");Hr(immediate.As(&newProtection),"D3D11 multithread");Hr(supplied.As(&newer),"D3D11 device1");
 D3D_FEATURE_LEVEL level=supplied->GetFeatureLevel(), selected{};
 Hr(newer->CreateDeviceContextState(0,&level,1,D3D11_SDK_VERSION,__uuidof(ID3D11Device),&selected,&newState),"Create isolated state");
 BOOL wasProtected=newProtection->SetMultithreadProtected(TRUE);
 if(!newProtection->GetMultithreadProtected())throw Error(RTN_REASON_UNSUPPORTED,"D3D11 multithread protection unavailable");
 device=supplied;context=newContext;protection=newProtection;isolated=newState;
 Log(std::string("D3D11 multithread protection enabled; prior=")+(wasProtected?"on":"off")+"; shared-device protection will not be disabled on stop");
}
struct ContextScope {
 ComPtr<ID3DDeviceContextState> saved;
 ContextScope(){protection->Enter();context->SwapDeviceContextState(isolated.Get(),&saved);context->ClearState();}
 ~ContextScope(){context->ClearState();context->SwapDeviceContextState(saved.Get(),nullptr);protection->Leave();}
};
ComPtr<ID3D11Query> Query(){if(!freeQueries.empty()){auto q=freeQueries.back();freeQueries.pop_back();return q;}ComPtr<ID3D11Query> q;D3D11_QUERY_DESC d{D3D11_QUERY_EVENT,0};Hr(device->CreateQuery(&d,&q),"Create retirement query");return q;}
void ReleaseGeneration(std::unique_ptr<Generation>& g){if(!g)return;for(auto& e:g->eyes){if(e.feature){auto handle=e.feature;for(auto& other:g->eyes)if(other.feature==handle)other.feature=nullptr;Log("ReleaseFeature="+std::to_string(uint32_t(NVSDK_NGX_D3D11_ReleaseFeature(handle))));}if(e.parameters){NVSDK_NGX_D3D11_DestroyParameters(e.parameters);e.parameters=nullptr;}}g.reset();}
void PollRetired(){
 while(!retired.empty()){
  auto& r=retired.front();HRESULT hr=context->GetData(r.query.Get(),nullptr,0,D3D11_ASYNC_GETDATA_DONOTFLUSH);
  if(hr==S_FALSE)break;if(FAILED(hr))throw Error(RTN_REASON_DEVICE_LOST,"Retirement query failed");
  ReleaseGeneration(r.generation);freeQueries.push_back(r.query);retired.pop_front();
 }
}
void RetireCurrent(){if(!current)return;auto q=Query();context->End(q.Get());Retired r;r.query=q;r.generation=std::move(current);retired.push_back(std::move(r));}
void InitializeNgx(){
 if(ngxInitialized)return;
 // A preloaded feature DLL does not prove another NGX device/session owner.
 // Let the documented Init/capability/Create calls establish support. Only
 // successful Init is balanced by our existing Shutdown1; never FreeLibrary.
 runtimeIdentity.Begin();
 const wchar_t* paths[]={featureDirectory.c_str()};NVSDK_NGX_FeatureCommonInfo common{};
 common.PathListInfo.Path=paths;common.PathListInfo.Length=1;
 common.LoggingInfo.LoggingCallback=NgxLogger;common.LoggingInfo.MinimumLoggingLevel=NVSDK_NGX_LOGGING_LEVEL_ON;
 common.LoggingInfo.DisableOtherLoggingSinks=true;
 fs::path data=logPath.empty()?fs::path(featureDirectory):(fs::path(logPath).parent_path());
 auto r=NVSDK_NGX_D3D11_Init_with_ProjectID(ProjectId,NVSDK_NGX_ENGINE_TYPE_CUSTOM,"RTMaquetaXR-native-v1",data.c_str(),device.Get(),&common);
 Ngx(r,RTN_REASON_INIT,"InitWithProjectID");ngxInitialized=true;
 Ngx(NVSDK_NGX_D3D11_GetCapabilityParameters(&capabilities),RTN_REASON_INIT,"GetCapabilityParameters");
 int available=0;Ngx(capabilities->Get(NVSDK_NGX_Parameter_SuperSampling_Available,&available),RTN_REASON_INIT,"SuperSampling.Available");
 if(!available)throw Error(RTN_REASON_UNSUPPORTED,"SuperSampling unavailable");
 RefreshRuntimeIdentity();
}
int CreationFlags(){int f=0;auto flags=config.featureFlags;
 if(flags&RTN_FEATURE_HDR)f|=NVSDK_NGX_DLSS_Feature_Flags_IsHDR;
 if(flags&RTN_FEATURE_DEPTH_INVERTED)f|=NVSDK_NGX_DLSS_Feature_Flags_DepthInverted;
 if(flags&RTN_FEATURE_MV_LOW_RES)f|=NVSDK_NGX_DLSS_Feature_Flags_MVLowRes;
 if(flags&RTN_FEATURE_MV_JITTERED)f|=NVSDK_NGX_DLSS_Feature_Flags_MVJittered;
 if(flags&RTN_FEATURE_AUTO_EXPOSURE)f|=NVSDK_NGX_DLSS_Feature_Flags_AutoExposure;return f;
}
NVSDK_NGX_PerfQuality_Value Quality(){switch(config.mode){case RTN_MODE_DLAA:return NVSDK_NGX_PerfQuality_Value_DLAA;case RTN_MODE_QUALITY:return NVSDK_NGX_PerfQuality_Value_MaxQuality;case RTN_MODE_BALANCED:return NVSDK_NGX_PerfQuality_Value_Balanced;case RTN_MODE_PERFORMANCE:return NVSDK_NGX_PerfQuality_Value_MaxPerf;case RTN_MODE_ULTRA_PERFORMANCE:return NVSDK_NGX_PerfQuality_Value_UltraPerformance;default:throw Error(RTN_REASON_CONFIG,"Invalid mode");}}
std::array<D3D11_TEXTURE2D_DESC,5> ValidateResources(Job& j){
 std::array<D3D11_TEXTURE2D_DESC,5>d{};
 for(size_t i=0;i<5;++i){ComPtr<ID3D11Device> owner;j.textures[i]->GetDevice(&owner);if(owner.Get()!=device.Get())throw Error(RTN_REASON_RESOURCE,"Texture belongs to a different device");j.textures[i]->GetDesc(&d[i]);if(d[i].SampleDesc.Count!=1||d[i].ArraySize!=1||d[i].MipLevels!=1)throw Error(RTN_REASON_RESOURCE,"Only non-MSAA single mip 2D textures supported");}
 const auto& a=j.data;
 auto fits=[](const D3D11_TEXTURE2D_DESC& t,uint32_t x,uint32_t y,uint32_t w,uint32_t h){return uint64_t(x)+w<=t.Width&&uint64_t(y)+h<=t.Height;};
 if(!fits(d[0],a.colorX,a.colorY,a.renderWidth,a.renderHeight)||!fits(d[1],a.depthX,a.depthY,a.renderWidth,a.renderHeight)||!fits(d[2],a.motionX,a.motionY,(config.featureFlags&RTN_FEATURE_MV_LOW_RES)?a.renderWidth:a.outputWidth,(config.featureFlags&RTN_FEATURE_MV_LOW_RES)?a.renderHeight:a.outputHeight)||!fits(d[3],a.outputX,a.outputY,a.outputWidth,a.outputHeight))throw Error(RTN_REASON_RESOURCE,"Active subrect exceeds texture");
 if(d[3].Width!=d[4].Width||d[3].Height!=d[4].Height||d[3].Format!=d[4].Format)throw Error(RTN_REASON_RESOURCE,"Destination and current-frame raw image fallback are incompatible");
 if(!(d[0].BindFlags&D3D11_BIND_SHADER_RESOURCE)||!(d[1].BindFlags&D3D11_BIND_SHADER_RESOURCE)||!(d[2].BindFlags&D3D11_BIND_SHADER_RESOURCE))throw Error(RTN_REASON_RESOURCE,"Inputs must support shader-resource binding");
 if(a.destination==a.color||a.destination==a.depth||a.destination==a.motion)throw Error(RTN_REASON_RESOURCE,"Destination aliases an input");
 if(config.mode==RTN_MODE_DLAA&&(a.renderWidth!=a.outputWidth||a.renderHeight!=a.outputHeight))throw Error(RTN_REASON_RESOURCE,"DLAA requires equal active input/output sizes");
 if(!(d[3].BindFlags&D3D11_BIND_RENDER_TARGET))throw Error(RTN_REASON_RESOURCE,"Destination must support render-target binding");
 // Texture2D native descriptors can be TYPELESS: the explicit managed semantic then selects
 // the sRGB view. Never pass gamma-encoded pixels straight to a linear/HDR NGX feature.
 const bool semanticSrgb=(config.featureFlags&RTN_FEATURE_COLOR_SRGB)!=0;
 try{rtn::ColorViewFormat(d[0].Format,semanticSrgb);rtn::ColorViewFormat(d[3].Format,semanticSrgb);}catch(const std::exception& e){throw Error(RTN_REASON_RESOURCE,e.what());}
 UINT support=0;Hr(device->CheckFormatSupport(DXGI_FORMAT_R16G16B16A16_FLOAT,&support),"Linear scratch format support");
 if(!(support&D3D11_FORMAT_SUPPORT_TYPED_UNORDERED_ACCESS_VIEW))throw Error(RTN_REASON_RESOURCE,"Linear RGBA16F scratch lacks typed UAV support");
 if(!(config.featureFlags&RTN_FEATURE_HDR))throw Error(RTN_REASON_CONFIG,"Linear scratch backend requires HDR feature semantics");
 return d;
}
void EnsureGeneration(const Job& j,const D3D11_TEXTURE2D_DESC& destination){
 const auto& a=j.data;
 if(current&&current->id!=a.generation)RetireCurrent();
 if(current){if(current->width!=a.renderWidth||current->height!=a.renderHeight||current->outWidth!=a.outputWidth||current->outHeight!=a.outputHeight||current->physicalWidth!=destination.Width||current->physicalHeight!=destination.Height||current->format!=destination.Format)throw Error(RTN_REASON_CONFIG,"Dimensions/format changed without a generation change");return;}
 auto g=std::make_unique<Generation>();g->id=a.generation;g->width=a.renderWidth;g->height=a.renderHeight;g->outWidth=a.outputWidth;g->outHeight=a.outputHeight;g->physicalWidth=destination.Width;g->physicalHeight=destination.Height;g->format=destination.Format;g->mode=config.mode;g->flags=config.featureFlags;
 // Install ownership before creating: a partial failure is still retired behind a GPU query.
 current=std::move(g);
 current->transfer=std::make_unique<rtn::ColorTransfer>(device.Get());
 for(auto& e:current->eyes){
  Ngx(NVSDK_NGX_D3D11_AllocateParameters(&e.parameters),RTN_REASON_CREATE,"Allocate eye parameters");
  if(config.featureFlags&RTN_FEATURE_FORCE_PRESET_K){
   for(const char* key:{NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_DLAA,NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_Quality,NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_Balanced,NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_Performance,NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_UltraPerformance})e.parameters->Set(key,uint32_t(NVSDK_NGX_DLSS_Hint_Render_Preset_K));
  }
  NVSDK_NGX_DLSS_Create_Params create{};create.Feature.InWidth=a.renderWidth;create.Feature.InHeight=a.renderHeight;
  create.Feature.InTargetWidth=a.outputWidth;create.Feature.InTargetHeight=a.outputHeight;create.Feature.InPerfQualityValue=Quality();create.InFeatureCreateFlags=CreationFlags();
  create.InEnableOutputSubrects=false; // Private output is exactly the active output dimensions.
  {PresetCaptureScope capture(e,config.mode);Ngx(NGX_D3D11_CREATE_DLSS_EXT(context.Get(),&e.feature,e.parameters,&create),RTN_REASON_CREATE,"Create eye feature");}
  auto d=destination;d.Width=a.outputWidth;d.Height=a.outputHeight;d.Format=DXGI_FORMAT_R16G16B16A16_FLOAT;d.Usage=D3D11_USAGE_DEFAULT;d.CPUAccessFlags=0;d.MiscFlags=0;d.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_UNORDERED_ACCESS|D3D11_BIND_RENDER_TARGET;
  Hr(device->CreateTexture2D(&d,nullptr,&e.scratch),"Create private NGX output");
  e.scratchView=rtn::ColorSrv(device.Get(),e.scratch.Get(),d.Format);
  d.Width=a.renderWidth;d.Height=a.renderHeight;d.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET;
  Hr(device->CreateTexture2D(&d,nullptr,&e.linearInput),"Create private linear input");
  e.linearInputView=rtn::ColorRtv(device.Get(),e.linearInput.Get(),d.Format);
 }
 if(current->eyes[0].feature==current->eyes[1].feature)throw Error(RTN_REASON_CREATE,"NGX returned shared eye feature");
 RefreshRuntimeIdentity();runtimeIdentity.End();backend.state=RTN_STATE_READY;backend.reason=RTN_REASON_NONE;
 Log("Two independent features ready, generation="+std::to_string(a.generation));
 Log(std::string("Preset requested=")+(config.featureFlags&RTN_FEATURE_FORCE_PRESET_K?"K":"Auto")+"; identifiedLeft="+std::to_string(current->eyes[0].identifiedPreset)+" identifiedRight="+std::to_string(current->eyes[1].identifiedPreset)+"; UINT32_MAX=not identified; evidence is runtime creation log, not echoed hints");
}
void Evaluate(Job& j,const std::array<D3D11_TEXTURE2D_DESC,5>& d){
 const auto& a=j.data;auto& e=current->eyes[a.eye];
 if(e.lastFrame!=UINT64_MAX&&a.frame<=e.lastFrame)throw Error(RTN_REASON_DUPLICATE,"Duplicate/out-of-order eye frame");
 bool discontinuity=e.lastFrame==UINT64_MAX||a.frame!=e.lastFrame+1;
 const bool semanticSrgb=(config.featureFlags&RTN_FEATURE_COLOR_SRGB)!=0;
 const auto sourceFormat=rtn::ColorViewFormat(d[0].Format,semanticSrgb),destinationFormat=rtn::ColorViewFormat(d[3].Format,semanticSrgb);
 // Create every publication resource before any evaluation: view creation failure cannot touch the raw fallback.
 j.destinationView=rtn::ColorRtv(device.Get(),j.textures[3].Get(),destinationFormat);
 const bool convertInput=rtn::IsSrgb(sourceFormat)||rtn::IsTypelessColor(d[0].Format);
 if(convertInput){
  j.sourceView=rtn::ColorSrv(device.Get(),j.textures[0].Get(),sourceFormat);
  current->transfer->Draw(context.Get(),j.sourceView.Get(),e.linearInputView.Get(),d[0].Width,d[0].Height,a.colorX,a.colorY,a.renderWidth,a.renderHeight,0,0,a.renderWidth,a.renderHeight);
 }
 NVSDK_NGX_D3D11_DLSS_Eval_Params p{};p.Feature.pInColor=convertInput?e.linearInput.Get():j.textures[0].Get();p.Feature.pInOutput=e.scratch.Get();
 p.pInDepth=j.textures[1].Get();p.pInMotionVectors=j.textures[2].Get();p.InJitterOffsetX=a.jitterX;p.InJitterOffsetY=a.jitterY;
 p.InRenderSubrectDimensions={a.renderWidth,a.renderHeight};p.InReset=((a.flags&RTN_JOB_RESET)||discontinuity)?1:0;
 p.InMVScaleX=a.mvScaleX;p.InMVScaleY=a.mvScaleY;p.InPreExposure=a.preExposure;p.InExposureScale=a.exposureScale;p.InFrameTimeDeltaInMsec=a.frameTimeMs;
 p.InColorSubrectBase=convertInput?NVSDK_NGX_Coordinates{0,0}:NVSDK_NGX_Coordinates{a.colorX,a.colorY};p.InDepthSubrectBase={a.depthX,a.depthY};p.InMVSubrectBase={a.motionX,a.motionY};p.InOutputSubrectBase={0,0};
 Ngx(NGX_D3D11_EVALUATE_DLSS_EXT(context.Get(),e.feature,e.parameters,&p),RTN_REASON_EVALUATE,"Evaluate eye");
 e.lastFrame=a.frame;
 // NGX can leave SRVs/UAVs bound. Unbind inside the isolated state before the publication copy.
 context->ClearState();
 if(a.flags&RTN_JOB_PRESENT)current->transfer->Draw(context.Get(),e.scratchView.Get(),j.destinationView.Get(),a.outputWidth,a.outputHeight,0,0,a.outputWidth,a.outputHeight,a.outputX,a.outputY,a.outputWidth,a.outputHeight,a.sharpness);
 if(FAILED(device->GetDeviceRemovedReason()))throw Error(RTN_REASON_DEVICE_LOST,"D3D11 device removed during evaluation");
}
void Maintenance(){
 if(!context){if(stopping){backend.state=RTN_STATE_IDLE;stopping=false;}return;}
 bool destroyDevice=false;
 {
  ContextScope scope;
  const bool lost=FAILED(device->GetDeviceRemovedReason());
  if(lost){
   if(!stopping){Fault(RTN_REASON_DEVICE_LOST,0,"Device removed; awaiting explicit stop");return;}
   // A removed device cannot complete its queries. Its GPU work is invalidated; do not wait forever.
   Log("Removed-device teardown: discarding unusable fences after confirmed GetDeviceRemovedReason failure");
   ReleaseGeneration(current);for(auto& r:retired)ReleaseGeneration(r.generation);retired.clear();
  }else PollRetired();
  if(stopping){RetireCurrent();if(!shutdownFlushed){context->Flush();shutdownFlushed=true;}if(!lost)PollRetired();if(retired.empty()){
   if(capabilities){NVSDK_NGX_D3D11_DestroyParameters(capabilities);capabilities=nullptr;}
   if(ngxInitialized){auto shutdown=NVSDK_NGX_D3D11_Shutdown1(device.Get());Log("NGX shutdown="+std::to_string(uint32_t(shutdown)));ngxInitialized=false;}
   runtimeIdentity.End();freeQueries.clear();backend.state=RTN_STATE_IDLE;stopping=false;destroyDevice=true;
  }}
 }
 if(destroyDevice){isolated.Reset();protection.Reset();context.Reset();device.Reset();}
}
void __stdcall RenderEvent(int eventId,void* eventData) noexcept {
 std::lock_guard<std::mutex> lock(mutex);
 try{
  if(eventId==RTN_EVENT_MAINTENANCE){Maintenance();return;}
  if(eventId!=RTN_EVENT_EVALUATE)return;
  uint64_t ticket=uint64_t(reinterpret_cast<uintptr_t>(eventData));auto found=jobs.find(ticket);if(found==jobs.end())return;
  auto job=std::move(found->second);jobs.erase(found);const bool measure=diagnosticsEnabled.load(std::memory_order_relaxed);const auto start=measure?std::chrono::steady_clock::now():std::chrono::steady_clock::time_point{};
  uint32_t result=RTN_JOB_FALLBACK,reason=RTN_REASON_NONE,ngx=0;ComPtr<ID3D11Query> done;bool fenceIssued=false;
  try{
   if(stopping||job->data.generation!=config.generation)throw Error(RTN_REASON_STALE,"Stale/cancelled generation");
   if(backend.state==RTN_STATE_FAULTED)throw Error(backend.reason,"Backend faulted; preserving current-frame raw image");
   DeviceFrom(*job);ContextScope scope;done=Query();PollRetired();
   try{
    const auto descriptors=ValidateResources(*job);InitializeNgx();EnsureGeneration(*job,descriptors[3]);Evaluate(*job,descriptors);
    result=(job->data.flags&RTN_JOB_PRESENT)?RTN_JOB_PRESENTED:RTN_JOB_SHADOW;
   }catch(...){context->End(done.Get());fenceIssued=true;throw;}
   context->End(done.Get());fenceIssued=true;
  }catch(const Error& e){reason=e.reason;ngx=e.ngx;if(reason!=RTN_REASON_STALE)Fault(reason,job->data.frame,e.what());}
   catch(const std::exception& e){reason=RTN_REASON_EVALUATE;Fault(reason,job->data.frame,e.what());}
  if(job->data.eye==0)backend.lastLeftFrame=job->data.frame;else backend.lastRightFrame=job->data.frame;
  double ms=measure&&diagnosticsEnabled.load(std::memory_order_relaxed)?std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count():0;Receipt(*job,result,reason,ngx,ms);
  if(fenceIssued&&done){Retired r;r.query=done;r.job=std::move(job);retired.push_back(std::move(r));}
  else if(done)freeQueries.push_back(done);
 }catch(const std::exception& e){Fault(RTN_REASON_EVALUATE,0,e.what());}
 catch(...){Fault(RTN_REASON_EVALUATE,0,"Unknown native exception; raw destination was prefilled");}
}
bool ValidJob(const RTN_Job& a){
 if(a.size!=sizeof(a)||a.abi!=RTN_ABI_VERSION||a.eye>1||a.flags&~uint32_t(RTN_JOB_RESET|RTN_JOB_PRESENT)||!a.generation)return false;
 if(!a.color||!a.depth||!a.motion||!a.destination||!a.fallback||!a.renderWidth||!a.renderHeight||!a.outputWidth||!a.outputHeight)return false;
 for(float v:{a.jitterX,a.jitterY,a.mvScaleX,a.mvScaleY,a.preExposure,a.exposureScale,a.frameTimeMs,a.sharpness})if(!std::isfinite(v))return false;
 return a.preExposure>0&&a.exposureScale>0&&a.frameTimeMs>0&&a.sharpness>=0&&a.sharpness<=1;
}
}

void __cdecl RTN_SetDiagnosticsEnabled(int enabled){diagnosticsEnabled.store(enabled!=0,std::memory_order_relaxed);}

int __cdecl RTN_Configure(const RTN_Config* value){
 std::lock_guard<std::mutex> lock(mutex);
 try{
  if(!value||value->size!=sizeof(*value)||value->abi!=RTN_ABI_VERSION)return Reject(RTN_REASON_ABI,"Invalid neural configuration ABI");
  runtimeIdentity.Configure(value->generation,value->featureDirectory?value->featureDirectory:L"");
  if(!value->generation||value->mode>RTN_MODE_ULTRA_PERFORMANCE||value->featureFlags&~uint32_t(127)||!value->featureDirectory)return Reject(RTN_REASON_CONFIG,"Invalid neural configuration generation, mode, flags or search directory");
  if(stopping)return Reject(RTN_REASON_CANCELLED,"Neural resources are still draining; retry configuration after Idle");
  if(backend.state!=RTN_STATE_IDLE&&value->generation<=config.generation)return Reject(RTN_REASON_STALE,"Neural configuration generation is not newer than the active generation");
  auto path=fs::path(value->featureDirectory).lexically_normal();
  if(!path.is_absolute())return Reject(RTN_REASON_CONFIG,"Neural runtime search-directory hint must be absolute");
  // The directory is a hint. A local DLL is optional when the NVIDIA driver
  // supplies an override; initialization reports any actual missing runtime.
  if(ngxInitialized&&path!=fs::path(featureDirectory))return Reject(RTN_REASON_CONFIG,"Stop and drain NGX before changing its runtime search-directory hint");
  config=*value;featureDirectory=path.wstring();{std::lock_guard<std::mutex> logLock(logMutex);logPath=value->logPath?value->logPath:L"";}config.featureDirectory=nullptr;config.logPath=nullptr;
  lastSubmitted[0]=lastSubmitted[1]=UINT64_MAX;backend.generation=config.generation;backend.state=RTN_STATE_CONFIGURED;backend.reason=RTN_REASON_NONE;backend.lastFailureFrame=0;
  Log("Configured generation="+std::to_string(config.generation)+"; current-frame image without AA required; no driver profile changes");return 1;
 }catch(const std::exception& e){return Reject(RTN_REASON_CONFIG,std::string("Neural configuration exception: ")+e.what());}
 catch(...){return Reject(RTN_REASON_CONFIG,"Unknown neural configuration exception");}
}
int __cdecl RTN_SubmitJob(const RTN_Job* data,void** eventData,uint64_t* ticket){
 if(eventData)*eventData=nullptr;if(ticket)*ticket=0;if(!data||!eventData||!ticket)return Reject(RTN_REASON_ABI,"Temporal submission requires job, event and ticket storage");
 std::lock_guard<std::mutex> lock(mutex);
 try{
  // Preserve the first GPU/NGX failure; a later eye rejection must not replace
  // it with a generic "backend faulted" message in the user-visible status.
  if(backend.state==RTN_STATE_FAULTED)return 0;
  if(!ValidJob(*data))return Reject(RTN_REASON_ABI,"Invalid temporal job ABI, eye, dimensions, pointers or numeric parameters");
  if(stopping)return Reject(RTN_REASON_CANCELLED,"Temporal submission rejected while neural resources are draining");
  if(backend.state==RTN_STATE_IDLE)return Reject(RTN_REASON_CONFIG,"Temporal submission requires a configured neural backend");
  if(data->generation!=config.generation)return Reject(RTN_REASON_STALE,"Temporal job belongs to a different neural generation");
  if(jobs.size()+retired.size()>=MaxJobs)return Reject(RTN_REASON_QUEUE_FULL,"Temporal queue is full (16 pending or GPU-retiring batches)");
  auto eye=data->eye;if(lastSubmitted[eye]!=UINT64_MAX&&data->frame<=lastSubmitted[eye])return Reject(RTN_REASON_DUPLICATE,"Duplicate or out-of-order temporal frame for this eye");
  auto j=std::make_unique<Job>();j->data=*data;j->ticket=nextTicket++;
  std::array<void*,5> ptrs={data->color,data->depth,data->motion,data->destination,data->fallback};
  for(size_t i=0;i<ptrs.size();++i)j->textures[i]=static_cast<ID3D11Texture2D*>(ptrs[i]);
  auto id=j->ticket;jobs.emplace(id,std::move(j));lastSubmitted[eye]=data->frame;
  *eventData=reinterpret_cast<void*>(uintptr_t(id));*ticket=id;return 1;
 }catch(const std::exception& e){return Reject(RTN_REASON_RESOURCE,std::string("Temporal submission exception: ")+e.what());}
 catch(...){return Reject(RTN_REASON_RESOURCE,"Unknown temporal submission exception");}
}
void* __cdecl RTN_GetRenderEventAndData(){return reinterpret_cast<void*>(&RenderEvent);}
int __cdecl RTN_CancelJob(uint64_t ticket){std::lock_guard<std::mutex> lock(mutex);auto it=jobs.find(ticket);if(it==jobs.end())return 0;Receipt(*it->second,RTN_JOB_CANCELLED,RTN_REASON_CANCELLED,0,0);jobs.erase(it);return 1;}
int __cdecl RTN_GetJobStatus(uint64_t ticket,RTN_JobStatus* status){
 if(!status||status->size!=sizeof(*status)||status->abi!=RTN_ABI_VERSION)return 0;
 std::lock_guard<std::mutex> lock(mutex);auto pending=jobs.find(ticket);
 if(pending!=jobs.end()){auto& j=*pending->second;*status={sizeof(*status),RTN_ABI_VERSION,ticket,j.data.generation,j.data.frame,j.data.eye,RTN_JOB_QUEUED,0,0,0};return 1;}
 for(auto it=receipts.rbegin();it!=receipts.rend();++it)if(it->ticket==ticket){*status=*it;return 1;}return 0;
}
int __cdecl RTN_GetBackendStatus(RTN_BackendStatus* status){if(!status||status->size!=sizeof(*status)||status->abi!=RTN_ABI_VERSION)return 0;std::lock_guard<std::mutex> lock(mutex);backend.pendingJobs=uint32_t(jobs.size());backend.retiredBatches=uint32_t(retired.size());*status=backend;return 1;}
int __cdecl RTN_GetPresetStatus(RTN_PresetStatus* status){if(!status||status->size!=sizeof(*status)||status->abi!=RTN_ABI_VERSION)return 0;std::lock_guard<std::mutex> lock(mutex);*status={sizeof(*status),RTN_ABI_VERSION,config.generation,(config.featureFlags&RTN_FEATURE_FORCE_PRESET_K)?11u:0u,UINT32_MAX,UINT32_MAX,0};if(current&&current->id==config.generation&&current->eyes[0].feature&&current->eyes[1].feature){status->identifiedLeft=current->eyes[0].identifiedPreset;status->identifiedRight=current->eyes[1].identifiedPreset;if(status->identifiedLeft!=UINT32_MAX&&status->identifiedRight!=UINT32_MAX)status->evidence=1;}return 1;}
int __cdecl RTN_GetRuntimeStatus(RTN_RuntimeStatus* status){if(!status||status->size!=sizeof(*status)||status->abi!=RTN_ABI_VERSION)return 0;*status=runtimeIdentity.Snapshot();return 1;}
void __cdecl RTN_RequestShutdown(){try{std::lock_guard<std::mutex> lock(mutex);if(stopping)return;stopping=true;shutdownFlushed=false;backend.state=RTN_STATE_STOPPING;for(auto& entry:jobs)Receipt(*entry.second,RTN_JOB_CANCELLED,RTN_REASON_CANCELLED,0,0);jobs.clear();}catch(...){}}
