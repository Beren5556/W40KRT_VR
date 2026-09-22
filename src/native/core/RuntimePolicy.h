#pragma once
#include <algorithm>
#include <cctype>
#include <string>
namespace RuntimePolicy {
inline bool Valid(int runtime) { return runtime == 0 || runtime == 1; }
inline bool Matches(int runtime, std::string name) {
    if (!Valid(runtime)) return false;
    std::transform(name.begin(), name.end(), name.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    if (name.find("steam") != std::string::npos || name.find("opencomposite") != std::string::npos) return false;
    if (runtime == 0) return name.find("virtualdesktop") != std::string::npos || name.find("virtual desktop") != std::string::npos || name == "vdxr";
    return name.find("oculus") != std::string::npos || name.find("meta quest") != std::string::npos || name.find("meta openxr") != std::string::npos;
}
}
