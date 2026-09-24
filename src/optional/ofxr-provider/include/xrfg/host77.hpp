#pragma once
#include <cstdint>
#include "../../../../../native/neural/Neural.h"
namespace xrfg::host77 {
constexpr std::uint32_t abi = 77;
struct Frame {
    std::uint32_t size{sizeof(Frame)}, version{abi};
    std::uint64_t epoch{};
    std::int64_t display_time{};
    std::uint64_t serial{}, neural_generation{}, game_frame{};
    std::uint32_t flags{}, reserved{}; // 1: eligible opaque stereo; 2: NVIDIA image expected
};
static_assert(sizeof(Frame)==56);
struct Status {
    std::uint32_t size{sizeof(Status)}, version{abi}, state{}, reason{};
    std::uint64_t epoch{}, submitted_pairs{};
};
Frame snapshot() noexcept;
bool eligible(const Frame& frame, std::int64_t display_time) noexcept;
bool read_neural_status(RTN_BackendStatus& result) noexcept;
bool neural_compatible(const Frame& frame) noexcept;
void report(std::uint32_t state,std::uint32_t reason=0) noexcept;
}
extern "C" __declspec(dllexport) int RTW77_Initialize(std::uint32_t version) noexcept;
extern "C" __declspec(dllexport) int RTW77_PublishFrame(const xrfg::host77::Frame* frame) noexcept;
extern "C" __declspec(dllexport) int RTW77_GetStatus(xrfg::host77::Status* status) noexcept;
extern "C" __declspec(dllexport) void RTW77_SetDiagnostics(int enabled) noexcept;
