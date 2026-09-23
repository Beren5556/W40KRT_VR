#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
namespace ResolutionPolicy80 {
// Recommendations are from OpenXR, not the headset's physical panel. Keep the
// exact 100% recommendation; cap every other size before integer conversion.
inline uint32_t Scaled(uint32_t recommended, uint32_t maximum, float scale) {
    if (!recommended || !maximum) return 0;
    scale = std::isfinite(scale) ? std::clamp(scale, .25f, 1.5f) : 1.f;
    if (scale == 1.f) return std::min(recommended, maximum);
    const double requested = std::max(64., std::ceil(static_cast<double>(recommended) * scale / 2.) * 2.);
    return static_cast<uint32_t>(std::min(static_cast<double>(maximum), requested));
}
}
