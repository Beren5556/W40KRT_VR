#include "xrfg/bridge_flight_logger.hpp"

#include <windows.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cstdio>
#include <cwchar>
#include <limits>
#include <mutex>
#include <string>
#include <system_error>

namespace xrfg {
namespace {

#ifndef XRFG_IMPLEMENTATION_VERSION
#define XRFG_IMPLEMENTATION_VERSION 0
#endif

constexpr wchar_t kConfigurationFileName[] = L"ofxr_bridge.ini";
constexpr wchar_t kConfigurationSection[] = L"diagnostics";
constexpr std::uint64_t kMegabyte = 1024ull * 1024ull;

[[nodiscard]] const char* operation_name(
    BridgeFlightOperation operation) noexcept {
    switch (operation) {
    case BridgeFlightOperation::logger: return "logger";
    case BridgeFlightOperation::negotiation: return "negotiation";
    case BridgeFlightOperation::instance_create: return "instance_create";
    case BridgeFlightOperation::instance_destroy: return "instance_destroy";
    case BridgeFlightOperation::session_create: return "session_create";
    case BridgeFlightOperation::session_destroy: return "session_destroy";
    case BridgeFlightOperation::session_begin: return "session_begin";
    case BridgeFlightOperation::session_end: return "session_end";
    case BridgeFlightOperation::application_wait_frame: return "app_wait_frame";
    case BridgeFlightOperation::application_begin_frame: return "app_begin_frame";
    case BridgeFlightOperation::application_end_frame: return "app_end_frame";
    case BridgeFlightOperation::application_swapchain_acquire:
        return "app_swapchain_acquire";
    case BridgeFlightOperation::application_swapchain_wait:
        return "app_swapchain_wait";
    case BridgeFlightOperation::application_swapchain_release:
        return "app_swapchain_release";
    case BridgeFlightOperation::private_swapchain_acquire:
        return "private_swapchain_acquire";
    case BridgeFlightOperation::private_swapchain_wait:
        return "private_swapchain_wait";
    case BridgeFlightOperation::private_swapchain_release:
        return "private_swapchain_release";
    case BridgeFlightOperation::synthesis_initialize: return "synthesis_initialize";
    case BridgeFlightOperation::synthesis_prime: return "synthesis_prime";
    case BridgeFlightOperation::synthesis_pair: return "synthesis_pair";
    case BridgeFlightOperation::downstream_first_end_frame:
        return "downstream_first_end_frame";
    case BridgeFlightOperation::internal_wait_frame: return "internal_wait_frame";
    case BridgeFlightOperation::internal_begin_frame: return "internal_begin_frame";
    case BridgeFlightOperation::internal_end_frame: return "internal_end_frame";
    case BridgeFlightOperation::continuity_reset: return "continuity_reset";
    case BridgeFlightOperation::gpu_drain: return "gpu_drain";
    case BridgeFlightOperation::swapchain_create: return "swapchain_create";
    case BridgeFlightOperation::swapchain_eligibility: return "swapchain_eligibility";
    case BridgeFlightOperation::projection_mapping: return "projection_mapping";
    case BridgeFlightOperation::generation_prepare: return "generation_prepare";
    case BridgeFlightOperation::session_binding: return "session_binding";
    case BridgeFlightOperation::swapchain_image: return "swapchain_image";
    case BridgeFlightOperation::d3d11_capture: return "d3d11_capture";
    case BridgeFlightOperation::d3d11_publish: return "d3d11_publish";
    case BridgeFlightOperation::runtime_identity: return "runtime_identity";
    case BridgeFlightOperation::presenter_submission:
        return "presenter_submission";
    case BridgeFlightOperation::presenter_transition:
        return "presenter_transition";
    case BridgeFlightOperation::nvidia_gpu_stages:
        return "nvidia_gpu_stages";
    case BridgeFlightOperation::nvidia_gpu_total:
        return "nvidia_gpu_total";
    case BridgeFlightOperation::presenter_pace:
        return "presenter_pace";
    case BridgeFlightOperation::synthesis_frame_start_wait:
        return "synthesis_frame_start_wait";
    case BridgeFlightOperation::embedded_configuration: return "embedded_configuration";
    }
    return "unknown";
}

[[nodiscard]] std::filesystem::path fallback_log_directory() {
    std::array<wchar_t, 32768> buffer{};
    const DWORD length = GetEnvironmentVariableW(
        L"LOCALAPPDATA", buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) {
        return {};
    }
    return std::filesystem::path(buffer.data()) / L"OFXR Bridge" / L"Logs";
}

[[nodiscard]] std::wstring log_file_name() {
    SYSTEMTIME time{};
    GetLocalTime(&time);
    std::array<wchar_t, 128> name{};
    _snwprintf_s(
        name.data(),
        name.size(),
        _TRUNCATE,
        L"ofxr-bridge-flight-%04u%02u%02u-%02u%02u%02u-pid%lu.log",
        static_cast<unsigned>(time.wYear),
        static_cast<unsigned>(time.wMonth),
        static_cast<unsigned>(time.wDay),
        static_cast<unsigned>(time.wHour),
        static_cast<unsigned>(time.wMinute),
        static_cast<unsigned>(time.wSecond),
        static_cast<unsigned long>(GetCurrentProcessId()));
    return name.data();
}

[[nodiscard]] HANDLE create_log_file(
    const std::filesystem::path& directory,
    std::filesystem::path* output_path) noexcept {
    try {
        if (directory.empty() || output_path == nullptr) {
            return INVALID_HANDLE_VALUE;
        }
        std::error_code error;
        std::filesystem::create_directories(directory, error);
        if (error) {
            return INVALID_HANDLE_VALUE;
        }
        const auto path = directory / log_file_name();
        HANDLE file = CreateFileW(
            path.c_str(),
            GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr,
            CREATE_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
        if (file != INVALID_HANDLE_VALUE) {
            *output_path = path;
        }
        return file;
    } catch (...) {
        return INVALID_HANDLE_VALUE;
    }
}

[[nodiscard]] std::filesystem::path current_module_directory() noexcept {
    try {
        HMODULE module = nullptr;
        if (!GetModuleHandleExW(
                GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                    GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(&initialize_bridge_flight_logger),
                &module)) {
            return {};
        }
        std::array<wchar_t, 32768> path{};
        const DWORD length = GetModuleFileNameW(
            module, path.data(), static_cast<DWORD>(path.size()));
        if (length == 0 || length >= path.size()) {
            return {};
        }
        return std::filesystem::path(path.data()).parent_path();
    } catch (...) {
        return {};
    }
}

} // namespace


// Host77 keeps one stable logger object for the module lifetime. OFF stops
// producers immediately and closes a file after any already-running write.
struct BridgeFlightLogger::Impl {
    HANDLE file{INVALID_HANDLE_VALUE};
    LARGE_INTEGER frequency{}, origin{};
    std::mutex mutex;
    std::atomic<std::uint64_t> next_sequence{1};
    std::atomic<bool> active{false};
    std::filesystem::path directory,path;
    std::uint64_t bytes_written{},maximum_bytes{32*kMegabyte};
    void close() noexcept {
        if(file!=INVALID_HANDLE_VALUE){CloseHandle(file);file=INVALID_HANDLE_VALUE;}
    }
    void write(std::uint64_t sequence,std::int64_t counter,char phase,BridgeFlightOperation operation,
        std::int64_t result,std::uint64_t duration,std::uint64_t a,std::uint64_t b,std::uint64_t c) noexcept {
        if(!active.load(std::memory_order_acquire))return;
        try {
            std::scoped_lock lock(mutex);
            if(!active.load(std::memory_order_acquire))return;
            if(file==INVALID_HANDLE_VALUE)file=create_log_file(directory,&path);
            if(file==INVALID_HANDLE_VALUE)return;
            const double elapsed=frequency.QuadPart>0?double(counter-origin.QuadPart)*1000.0/double(frequency.QuadPart):0;
            std::array<char,384> line{};
            int length=std::snprintf(line.data(),line.size(),"seq=%llu ms=%.3f tid=%lu phase=%c op=%s result=%lld dur_us=%llu a=%llu b=%llu c=%llu\r\n",
                static_cast<unsigned long long>(sequence),elapsed,static_cast<unsigned long>(GetCurrentThreadId()),phase,operation_name(operation),
                static_cast<long long>(result),static_cast<unsigned long long>(duration),static_cast<unsigned long long>(a),static_cast<unsigned long long>(b),static_cast<unsigned long long>(c));
            if(length>0&&static_cast<size_t>(length)<line.size()) {
                if(bytes_written+length>maximum_bytes){LARGE_INTEGER zero{};if(SetFilePointerEx(file,zero,nullptr,FILE_BEGIN)&&SetEndOfFile(file))bytes_written=0;}
                DWORD written{};if(WriteFile(file,line.data(),static_cast<DWORD>(length),&written,nullptr))bytes_written+=written;
            }
            if(!active.load(std::memory_order_acquire))close();
        } catch(...) {}
    }
};
BridgeFlightLogger::BridgeFlightLogger() noexcept : impl_(new(std::nothrow) Impl()) {}
BridgeFlightLogger::~BridgeFlightLogger(){shutdown();}
void BridgeFlightLogger::initialize(const std::filesystem::path& directory) noexcept {
    try {if(!impl_)return;std::scoped_lock lock(impl_->mutex);impl_->directory=directory;
        QueryPerformanceFrequency(&impl_->frequency);QueryPerformanceCounter(&impl_->origin);
    } catch(...) {}
}
void BridgeFlightLogger::set_enabled(bool value) noexcept {
    if(!impl_)return;
    impl_->active.store(value,std::memory_order_release);
    if(!value){std::unique_lock lock(impl_->mutex,std::try_to_lock);if(lock.owns_lock())impl_->close();}
}
void BridgeFlightLogger::shutdown() noexcept {
    if(!impl_)return;impl_->active.store(false,std::memory_order_release);
    std::scoped_lock lock(impl_->mutex);impl_->close();
}
bool BridgeFlightLogger::enabled() const noexcept{return impl_&&impl_->active.load(std::memory_order_acquire);}
std::filesystem::path BridgeFlightLogger::log_path() const {
    if(!impl_)return {};std::scoped_lock lock(impl_->mutex);return impl_->path;
}

BridgeFlightToken BridgeFlightLogger::begin(
    BridgeFlightOperation operation,
    std::uint64_t a,
    std::uint64_t b,
    std::uint64_t c) noexcept {
    if (!enabled()) {
        return {};
    }
    LARGE_INTEGER now{};
    if (!QueryPerformanceCounter(&now)) {
        return {};
    }
    const BridgeFlightToken token{
        impl_->next_sequence.fetch_add(1, std::memory_order_relaxed),
        now.QuadPart};
    impl_->write(
        token.sequence, token.start_counter, 'B', operation, 0, 0, a, b, c);
    return token;
}

void BridgeFlightLogger::end(
    BridgeFlightToken token,
    BridgeFlightOperation operation,
    std::int64_t result,
    std::uint64_t a,
    std::uint64_t b,
    std::uint64_t c) noexcept {
    if (!enabled() || token.sequence == 0) {
        return;
    }
    LARGE_INTEGER now{};
    if (!QueryPerformanceCounter(&now)) {
        return;
    }
    const std::uint64_t duration = impl_->frequency.QuadPart > 0 &&
            now.QuadPart >= token.start_counter
        ? static_cast<std::uint64_t>(
              (now.QuadPart - token.start_counter) * 1000000ll /
              impl_->frequency.QuadPart)
        : 0;
    impl_->write(
        token.sequence, now.QuadPart, 'E', operation, result, duration, a, b, c);
}

void BridgeFlightLogger::event(
    BridgeFlightOperation operation,
    std::int64_t result,
    std::uint64_t a,
    std::uint64_t b,
    std::uint64_t c) noexcept {
    if (!enabled()) {
        return;
    }
    LARGE_INTEGER now{};
    if (!QueryPerformanceCounter(&now)) {
        return;
    }
    impl_->write(
        impl_->next_sequence.fetch_add(1, std::memory_order_relaxed),
        now.QuadPart,
        'I',
        operation,
        result,
        0,
        a,
        b,
        c);
}

BridgeFlightLogger& bridge_flight_logger() noexcept {
    static BridgeFlightLogger logger;
    return logger;
}

void initialize_bridge_flight_logger() noexcept {
    static std::once_flag once;
    try {
        std::call_once(once, [] {
            bridge_flight_logger().initialize(current_module_directory());
        });
    } catch (...) {
    }
}

} // namespace xrfg
