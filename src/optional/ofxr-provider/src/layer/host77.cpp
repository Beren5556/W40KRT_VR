// RTMaquetaXR private adapter contract. No registry, file preference or GPU waits.
#include "xrfg/host77.hpp"
#include "xrfg/bridge_flight_logger.hpp"
#include <windows.h>
#include <atomic>
#include <mutex>
namespace xrfg::host77 {
namespace {
std::mutex mutex;
Frame current;
Status status;
bool initialized{};
// Read-only ABI from RTNeural 1. The provider queries it on the render thread,
// after both real eye jobs, so a same-frame NGX failure cannot be interpolated.
struct NeuralStatus {
    std::uint32_t size{sizeof(NeuralStatus)},abi{1},state{},reason{};
    std::uint64_t generation{},failure{},left{},right{};
    std::uint32_t pending{},retired{},major{},minor{},patch{},build{};
};
static_assert(sizeof(NeuralStatus)==72);
}
Frame snapshot() noexcept {
    std::scoped_lock lock(mutex);
    return initialized?current:Frame{};
}
bool eligible(const Frame& frame,std::int64_t display_time) noexcept {
    return frame.size==sizeof(Frame)&&frame.version==abi&&frame.epoch&&frame.serial&&
        frame.display_time>0&&frame.display_time==display_time&&(frame.flags&1)!=0&&(frame.flags&~3u)==0;
}
bool neural_compatible(const Frame& frame) noexcept {
    if((frame.flags&2)==0)return true;
    HMODULE module=GetModuleHandleW(L"RTNeural.dll");
    if(!module)return false;
    using Read=int(__cdecl*)(NeuralStatus*);
    auto read=reinterpret_cast<Read>(GetProcAddress(module,"RTN_GetBackendStatus"));
    NeuralStatus result;
    return read&&read(&result)==1&&result.state==2&&result.reason==0&&
        result.generation==frame.neural_generation&&result.left==frame.game_frame&&result.right==frame.game_frame;
}
void report(std::uint32_t state,std::uint32_t reason) noexcept {
    std::scoped_lock lock(mutex);status.state=state;status.reason=reason;status.epoch=current.epoch;
    if(state==3)++status.submitted_pairs;
}
}
extern "C" int RTW77_Initialize(std::uint32_t version) noexcept {
    if(version!=xrfg::host77::abi)return 0;
    xrfg::initialize_bridge_flight_logger();
    std::scoped_lock lock(xrfg::host77::mutex);
    xrfg::host77::initialized=true;xrfg::host77::status.state=1;return 1;
}
extern "C" int RTW77_PublishFrame(const xrfg::host77::Frame* frame) noexcept {
    std::scoped_lock lock(xrfg::host77::mutex);
    if(!xrfg::host77::initialized||!frame||frame->size!=sizeof(*frame)||frame->version!=xrfg::host77::abi||(frame->flags&~3u)!=0) {
        xrfg::host77::current={};return 0;
    }
    xrfg::host77::current=*frame;return 1;
}
extern "C" int RTW77_GetStatus(xrfg::host77::Status* output) noexcept {
    if(!output||output->size!=sizeof(*output)||output->version!=xrfg::host77::abi)return 0;
    std::scoped_lock lock(xrfg::host77::mutex);*output=xrfg::host77::status;return 1;
}
extern "C" void RTW77_SetDiagnostics(int enabled) noexcept {
    xrfg::initialize_bridge_flight_logger();xrfg::bridge_flight_logger().set_enabled(enabled!=0);
}
