# Warhammer 40K: Rogue Trader VR

**W40KRT_VR — The depth of Rogue Trader, on your tabletop.**

> **First PC beta · OpenXR · English and Spanish**

W40KRT_VR turns *Warhammer 40,000: Rogue Trader* into a virtual-reality experience built around a three-dimensional tabletop. Move around the scene, bring it closer, and manipulate it with Touch controllers as if the adventure were laid out in front of you.

The approach is reminiscent of **Demeo**, while preserving the original game's campaign, rules, combat, and decisions. Watch the action from above, use closer character views, or enter first person.

## Main features

- **Manipulable VR tabletop:** move, rotate, tilt, and scale the scene with controller gestures.
- **Full headset tracking:** inspect the world from different physical positions and angles.
- **Multiple perspectives:** switch between tabletop, character framing, and first-person views.
- **Touch controls:** select characters, issue orders, and use VR action and participant wheels.
- **Spatial interface:** use dialogue, character sheets, weapons, status information, inventory, and management screens inside the headset.
- **Game-context coverage:** On-foot exploration, Ground combat, Space combat, Star map, Galactic map, Management screens, and Dialogues and cinematics.
- **Configurable image quality:** saved profiles, adjustable resolution, TAA, and per-eye DLSS/DLAA for compatible NVIDIA GPUs.
- **Experimental OFXR:** optional frame generation for the VR output.
- **Comfort options:** panel size and distance, camera movement, tabletop controls, and independent layouts for different screens.
- **English and Spanish help:** contextual reminders and control guidance adapt to the current game context.
- **VR-oriented exploration interactions** and optional combat visibility settings intended to reduce rendering load.

The **Star map** is the warp-route and travel interface. The **Galactic map** shows planets within a system. Orders, abilities, movement rules, and game data remain part of the original game.

## Compatibility

| Component | First-beta support |
| --- | --- |
| Game | Steam edition of *Warhammer 40,000: Rogue Trader*, using the build stated in the release |
| System | 64-bit Windows 10 or 11 |
| Graphics API | DirectX 11 |
| VR standard | OpenXR |
| Tested headset and controllers | Meta Quest 3 with Touch controllers |
| Virtual Desktop | VDXR |
| Meta connection | Meta Quest Link or Air Link |
| Mod languages | English and Spanish |

W40KRT_VR uses OpenXR directly and does not require SteamVR. Other headsets, storefronts, and mod combinations are not considered supported until they have been tested.

The mod's own interface is available in English and Spanish. Native game text and data retain the language selected in *Rogue Trader*.

## Install

> **Why are there two installers?** Both install exactly the same mod. Use **INSTALL-VDXR.cmd** when you play through Virtual Desktop/VDXR, or **INSTALL-META.cmd** when you use Meta Quest Link/Air Link. The only difference is the initial OpenXR runtime selected in the mod settings. You can change it later from the mod menu and restart the game.

1. Download the beta ZIP from [Releases](../../releases).
2. Extract the entire archive into a new folder outside the game directory.
3. Close *Rogue Trader* completely.
4. Run **INSTALL-VDXR.cmd** for Virtual Desktop/VDXR or **INSTALL-META.cmd** for Meta Quest Link/Air Link.
5. If the installer cannot find the game, select the folder containing `WH40KRT.exe`.
6. Connect the headset through the selected provider.
7. Launch *Rogue Trader* from Steam using **DirectX 11** and load a save.

The package includes installation and removal tools. You do not need to compile the source, install RTVR separately, or add an external mod manager.

To update, run the installer from the new package while the game is closed. Existing settings and a user-selected DLSS DLL are preserved. To remove this beta, use **UNINSTALL.cmd** from the matching package.

## Selecting the OpenXR runtime

The two installers select the initial runtime:

- **INSTALL-VDXR.cmd** for Virtual Desktop/VDXR.
- **INSTALL-META.cmd** for Meta Quest Link/Air Link.

You can later change **OpenXR runtime** in the mod settings. The new choice is applied after fully restarting the game. It affects only the game process and does not change the default OpenXR runtime in Windows or modify NVIDIA profiles.

## Experimental OFXR

OFXR is an optional frame-generation mode for the VR output. It can improve perceived smoothness, but results depend on the PC, scene, and configuration, and it may introduce visual artifacts or latency.

To try it:

1. Disable **Selective engine cadence**, because the two modes are incompatible.
2. Enable **Experimental OFXR** on the main settings page or under **Advanced settings > Performance**.
3. Fully restart the game.

Disable the option and restart again to return to normal rendering. OFXR works over either VDXR or Meta Quest Link. It is off by default and does not replace DLSS/DLAA.

## Performance guidance

An **NVIDIA GeForce RTX 4080, 5080, or 4090** is recommended for the intended high-quality configuration. This is guidance rather than a minimum requirement or a guarantee of constant performance in every scene.

On less powerful systems:

1. Start with the mod's **Performance** profile.
2. Reduce **Draw distance** under Advanced settings when more headroom is needed.
3. Try either OFXR or the interpolation provided by Virtual Desktop or Meta Quest Link.
4. Select 90 or 100 Hz only when that refresh rate is available for the headset and connection method.

Compare settings in the same scene. Test interpolation methods separately before combining them. Interpolation can improve perceived smoothness, but it does not increase the real rate at which the game calculates its simulation.

## Touch control summary

Controls adapt to the current game context.

| Action | Control |
| --- | --- |
| Point and select | Aim with the right skull and press the right trigger or **A** |
| Frame a character, enemy, or ship | Double-click the target |
| Enter first person | Fully hold the right trigger for 2 seconds over a character or ship |
| Move and enter first person | Hold over a valid destination for 2 seconds in exploration or 3 seconds in Ground combat |
| Leave first person | Press **B**, or bring both gripped hands together |
| Select a group | Hold the right trigger and drag a selection rectangle |
| Move the party during exploration | Right stick, relative to the selected character's heading |
| Move the tabletop | Hold either grip and move that hand |
| Rotate and scale the tabletop | Hold both grips; rotate the hands or change the distance between them |
| Rotate or tilt with a stick | Left stick |
| Open the left wheel | Left grip + left trigger |
| Open the right wheel | Right grip + right trigger |
| Switch actions and participants | Click the left stick while the left wheel is open |
| Read cards and information | Point at a left-wheel item and hold the right grip; use the right stick to scroll |
| End the turn | Hold the left stick, **A**, or right trigger for 1 second over the end-turn skull |
| Tactical information | Hold **Y** during the player's Ground-combat turn |
| Interact and highlight in exploration | **A** interacts; hold **Y** to highlight available objects |
| Cancel, go back, or close | **B**; the left trigger retains the native secondary click |
| Pause or resume | **X** |
| Toggle contextual reminders | Short right-stick click |
| Open or close VR settings | Hold both triggers and both grips for 1 second, or press **F1** |

On the Star and Galactic maps, use the right stick to pan and the left stick up/down to zoom. Native windows and dialogue use the right-hand pointer with the right trigger or **A**; the right stick scrolls and **B** goes back when allowed by the game.

The mod includes contextual help and an illustrated in-game control guide. A separate English/Spanish quick-reference image will be added to this page.

## Beta status

This is the first public beta of W40KRT_VR. Performance can vary during combat and complex scenes, and untested hardware or game situations may expose additional issues.

This is a personal project without formal technical support. Experiences and questions may be discussed in community forums, without a commitment by the author to respond, investigate, or provide fixes.

Release notes will focus on player-visible features, improvements, and relevant known limitations.

## Possible future improvements

These items are being considered and are not promised for a particular release:

- Hand Tracking as an alternative or complement to Touch controllers.
- Interaction with hyperlinks that open additional information in game windows.
- Further OFXR development and tuning.
- Support for Pimax, Quest 2, and Pico 4 headsets.
- A game launcher that selects VDXR or Meta Quest Link and enables or disables OFXR before launch.
- Additional combat performance and stability work.
- Additional camera, interface, and control options.

## Credits

This project incorporates code adapted from **Rogue Trader VR (RTVR), by SolemnScribe**, released under the MIT License. Its work integrating *Rogue Trader* with the cameras, renderer, and interface provided an important foundation for this adaptation.

Without that initial work, substantially more time would have been required to investigate camera integration, move the menus into three-dimensional space, preserve interaction, and solve compatibility and scene-transition problems. Many thanks to SolemnScribe for sharing this work and source code with the community.

Building on that foundation, W40KRT_VR develops its own tabletop approach inspired by **Demeo**: a direct OpenXR bridge for VDXR and Meta Quest Link, full headset tracking, Touch controls and gestures, multiple camera perspectives, and extensive interface, comfort, and rendering work.

The experimental frame-generation mode is based on **[OFXR Bridge](https://github.com/tig3rmast3r/OFXR-Bridge), created by tig3rmast3r** and released under LGPL-3.0-or-later. Many thanks to tig3rmast3r for creating OFXR Bridge and sharing its source with the VR community. W40KRT_VR adapts and integrates that technology as an optional, game-specific component that is disabled by default.

- [RTVR on Nexus Mods](https://www.nexusmods.com/warhammer40kroguetrader/mods/518)
- [SolemnScribe's source code](https://github.com/SolemnScribe/rogue-trader-and-pathfinder-vr)
- [OFXR Bridge by tig3rmast3r](https://github.com/tig3rmast3r/OFXR-Bridge)

## License and intellectual property

Original W40KRT_VR code is released under the [MIT License](LICENSE). Adapted RTVR code retains SolemnScribe's copyright and MIT terms in [LICENSE-RTVR](LICENSE-RTVR). The optional OFXR provider is distributed under LGPL-3.0-or-later and retains its own license and corresponding source.

Third-party components and assets retain their own terms. The relevant notices are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and in the license files supplied with the release package.

The W40KRT_VR license does not cover *Warhammer 40,000: Rogue Trader*, its assets, models, textures, music, trademarks, or other game content. Runtime visuals such as the servo-skull hands are loaded from the user's legitimate game installation and are not redistributed by this project.

**W40KRT_VR is an unofficial fan project.** It is not affiliated with, sponsored by, or endorsed by Owlcat Games, Games Workshop, NVIDIA, Meta, Virtual Desktop, or the authors of its dependencies. All trademarks belong to their respective owners.
