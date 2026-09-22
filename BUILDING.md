# Building

The release archive is the supported way to install the beta. Building from source is intended for developers who already have the required SDKs and a legitimate Steam installation of *Warhammer 40,000: Rogue Trader*.

The repository contains three source areas:

- `src/managed/RTMaquetaXR`: managed C# game, UI, input, camera, and rendering integration.
- `src/native/core`: the native OpenXR and NVIDIA NGX/DLSS bridges.
- `src/optional/ofxr-provider`: the optional LGPL OFXR provider and its own build documentation.

The managed assembly targets the .NET/Mono environment used by the game and references assemblies from the user's game installation. Those assemblies are not redistributed. The native components require Visual Studio 2022 C++ tools, CMake, the Windows SDK, Khronos OpenXR SDK 1.1.63, and the relevant NVIDIA SDK headers and libraries. The OFXR provider additionally requires the upstream AMD FidelityFX SDK revision identified in its `THIRD_PARTY.md`.

Paths to proprietary or locally installed SDKs must be provided by the developer. Do not commit game files, NVIDIA SDK packages, compiled binaries, personal paths, or generated build directories.

Exact release binaries are provided in GitHub Releases. A locally compiled build is not byte-identical to the signed and verified beta package unless it uses the same toolchains, SDK revisions, and inputs.
