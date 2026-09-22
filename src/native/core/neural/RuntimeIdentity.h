#pragma once
#include "Neural.h"
#include <windows.h>
#include <psapi.h>
#include <algorithm>
#include <array>
#include <filesystem>
#include <mutex>
#include <string>
#include <vector>

namespace rtn {
// Inspection is restricted to initialization/feature creation. A missing log,
// unfamiliar filename, file metadata or loader race only lowers confidence.
// This helper never loads/unloads modules and never calls NGX or a driver API.
struct RuntimeModule { HMODULE handle{}; std::wstring path; };
inline std::wstring RuntimeWide(const std::string& value) {
 if(value.empty())return {};
 int count=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,value.data(),int(value.size()),nullptr,0);
 if(count<=0)return {};
 std::wstring result(size_t(count),L'\0');
 if(!MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,value.data(),int(value.size()),result.data(),count))return {};
 return result;
}
inline std::wstring RuntimeModulePath(HMODULE module) {
 std::wstring path(32768,L'\0');DWORD count=GetModuleFileNameW(module,path.data(),DWORD(path.size()));
 if(!count||count>=path.size())return {};path.resize(count);return path;
}
inline std::wstring RuntimePathKey(std::wstring path) {
 std::replace(path.begin(),path.end(),L'/',L'\\');
 try{path=std::filesystem::path(path).lexically_normal().wstring();}catch(...){}
 if(!path.empty())CharLowerBuffW(path.data(),DWORD(path.size()));
 return path;
}
inline bool RuntimePathEqual(const std::wstring& a,const std::wstring& b) {
 return !a.empty()&&!b.empty()&&RuntimePathKey(a)==RuntimePathKey(b);
}
inline bool RuntimeDlssCandidate(const std::wstring& path) {
 auto lower=RuntimePathKey(path);
 return lower.find(L"nvngx_dlss.dll")!=std::wstring::npos||lower.find(L"\\ngx\\models\\dlss\\")!=std::wstring::npos;
}
inline std::vector<RuntimeModule> RuntimeModules() {
 std::vector<HMODULE> handles(512);DWORD needed=0;
 if(!EnumProcessModules(GetCurrentProcess(),handles.data(),DWORD(handles.size()*sizeof(HMODULE)),&needed))return {};
 if(needed>handles.size()*sizeof(HMODULE)){
  if(needed>8192*sizeof(HMODULE))return {};handles.resize((needed+sizeof(HMODULE)-1)/sizeof(HMODULE));
  if(!EnumProcessModules(GetCurrentProcess(),handles.data(),DWORD(handles.size()*sizeof(HMODULE)),&needed)||needed>handles.size()*sizeof(HMODULE))return {};
 }
 std::vector<RuntimeModule> result;result.reserve(needed/sizeof(HMODULE));
 for(size_t i=0;i<needed/sizeof(HMODULE);++i){auto path=RuntimeModulePath(handles[i]);if(!path.empty())result.push_back({handles[i],std::move(path)});}
 return result;
}
inline bool RuntimeVersion(const RuntimeModule& module,std::array<uint32_t,4>& version) {
 // Read the loaded image's version resource. Looking up the pathname instead
 // could report a replacement file's version while the old image stays mapped.
 if(!module.handle)return false;
 HRSRC resource=FindResourceW(module.handle,MAKEINTRESOURCEW(1),RT_VERSION);if(!resource)return false;
 DWORD size=SizeofResource(module.handle,resource);if(!size||size>1024*1024)return false;
 HGLOBAL loaded=LoadResource(module.handle,resource);if(!loaded)return false;
 const void* bytes=LockResource(loaded);if(!bytes)return false;
 std::vector<unsigned char> data(size);std::copy_n(static_cast<const unsigned char*>(bytes),size,data.data());
 VS_FIXEDFILEINFO* value=nullptr;UINT length=0;
 if(!VerQueryValueW(data.data(),L"\\",reinterpret_cast<void**>(&value),&length)||!value||length<sizeof(*value))return false;
 version={HIWORD(value->dwFileVersionMS),LOWORD(value->dwFileVersionMS),HIWORD(value->dwFileVersionLS),LOWORD(value->dwFileVersionLS)};return true;
}
template<size_t N> inline bool RuntimeText(wchar_t(&target)[N],const std::wstring& value) {
 const size_t count=std::min(value.size(),N-1);std::copy_n(value.data(),count,target);target[count]=0;return count<value.size();
}
class RuntimeIdentity {
 std::mutex guard;
 RTN_RuntimeStatus status{sizeof(RTN_RuntimeStatus),RTN_ABI_VERSION};
 std::wstring directory,observedPath;
 std::vector<RuntimeModule> before;
 bool observing=false,overrideObserved=false,conflicting=false;
 void Describe() {
  if(status.lastErrorReason)return;
  if(status.identity==RTN_IDENTITY_UNKNOWN)RuntimeText(status.message,L"Effective DLSS runtime unidentified; NGX evaluation remains allowed.");
  else if(!(status.flags&RTN_RUNTIME_VERSION_KNOWN))RuntimeText(status.message,L"DLSS runtime observed; version metadata unavailable. NGX evaluation remains allowed.");
  else if(status.flags&RTN_RUNTIME_TESTED)RuntimeText(status.message,L"DLSS 310.9.1.0 matches the tested build; runtime location is diagnostic only.");
  else RuntimeText(status.message,L"DLSS version differs from tested 310.9.1.0; NGX evaluation remains allowed.");
 }
public:
 void Configure(uint64_t generation,const std::wstring& requested) noexcept {
  try{std::lock_guard<std::mutex> lock(guard);status.generation=generation;status.lastErrorReason=0;directory=requested;Describe();}catch(...){}
 }
 void Begin() noexcept {
  try{
   auto modules=RuntimeModules();std::lock_guard<std::mutex> lock(guard);
   uint64_t generation=status.generation;status={sizeof(RTN_RuntimeStatus),RTN_ABI_VERSION};status.generation=generation;
   before=std::move(modules);observedPath.clear();overrideObserved=conflicting=false;observing=true;
   for(const auto& module:before)if(RuntimeDlssCandidate(module.path))status.flags|=RTN_RUNTIME_PRELOADED;
   Describe();
  }catch(...){std::lock_guard<std::mutex> lock(guard);observing=true;}
 }
 void ObserveLog(const char* line,bool dlssFeature) noexcept {
  if(!line)return;
  try{
   std::lock_guard<std::mutex> lock(guard);if(!observing)return;
   std::string text(line);
   if(text.find("Feature dlss override enabled")!=std::string::npos)overrideObserved=true;
   const std::string marker="Loaded from path \"";size_t begin=text.find(marker);if(begin==std::string::npos)return;
   begin+=marker.size();size_t end=text.find('"',begin);if(end==std::string::npos||end==begin)return;
   auto path=RuntimeWide(text.substr(begin,end-begin));if(path.empty()||(!dlssFeature&&!RuntimeDlssCandidate(path)))return;
   if(!observedPath.empty()&&!RuntimePathEqual(observedPath,path))conflicting=true;
   observedPath=std::move(path);
  }catch(...){}
 }
 // Resolve accepts a loader inventory so identity logic can be tested without
 // initializing NGX. Passing an empty inventory is an ordinary unknown result.
 void Resolve(const std::vector<RuntimeModule>& modules) noexcept {
  try{
   std::lock_guard<std::mutex> lock(guard);
   uint32_t keep=status.flags&RTN_RUNTIME_PRELOADED;status.identity=RTN_IDENTITY_UNKNOWN;status.source=RTN_SOURCE_UNKNOWN;
   status.flags=keep;status.path[0]=0;status.versionMajor=status.versionMinor=status.versionPatch=status.versionBuild=0;
   if(overrideObserved)status.flags|=RTN_RUNTIME_OVERRIDE_OBSERVED;
   std::wstring selected;bool verified=false;RuntimeModule selectedModule;
   if(!conflicting&&!observedPath.empty()){
    selected=observedPath;for(const auto& module:modules)if(RuntimePathEqual(module.path,selected)){selected=module.path;selectedModule=module;verified=true;break;}
   }else if(!conflicting){
    for(const auto& module:modules){
     if(!RuntimeDlssCandidate(module.path))continue;
     bool existed=false;for(const auto& prior:before)if(prior.handle==module.handle&&RuntimePathEqual(prior.path,module.path)){existed=true;break;}
     if(existed)continue;if(!selected.empty()){selected.clear();verified=false;break;}selected=module.path;selectedModule=module;verified=true;
    }
   }
   if(!selected.empty()){
    status.identity=verified?RTN_IDENTITY_MODULE:RTN_IDENTITY_LOG;
    std::wstring parent;try{parent=std::filesystem::path(selected).parent_path().wstring();}catch(...){}
    status.source=overrideObserved?RTN_SOURCE_OVERRIDE:RuntimePathEqual(parent,directory)?RTN_SOURCE_CONFIGURED:RTN_SOURCE_EXTERNAL;
    if(RuntimeText(status.path,selected))status.flags|=RTN_RUNTIME_PATH_TRUNCATED;
    std::array<uint32_t,4> version{};
    // File metadata is attributed only when the loader inventory corroborates
    // the log; a log-only path is reported without claiming its effective DLL.
    if(verified&&RuntimeVersion(selectedModule,version)){
     status.flags|=RTN_RUNTIME_VERSION_KNOWN;status.versionMajor=version[0];status.versionMinor=version[1];status.versionPatch=version[2];status.versionBuild=version[3];
     if(version==std::array<uint32_t,4>{310,9,1,0})status.flags|=RTN_RUNTIME_TESTED;
    }
   }
   Describe();
  }catch(...){}
 }
 void Refresh() noexcept {try{Resolve(RuntimeModules());}catch(...){} }
 void End() noexcept {try{std::lock_guard<std::mutex> lock(guard);observing=false;before.clear();}catch(...){} }
 void Error(uint32_t reason,const std::string& text) noexcept {
  try{std::lock_guard<std::mutex> lock(guard);status.lastErrorReason=reason;RuntimeText(status.message,RuntimeWide(text));}catch(...){}
 }
 RTN_RuntimeStatus Snapshot() noexcept {
  try{std::lock_guard<std::mutex> lock(guard);return status;}catch(...){return RTN_RuntimeStatus{sizeof(RTN_RuntimeStatus),RTN_ABI_VERSION};}
 }
};
}
