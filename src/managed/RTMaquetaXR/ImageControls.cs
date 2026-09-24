using System;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly string[] ImageModes = { "TAA", "SMAA", "No antialiasing", "DLAA", "DLSS", "NVIDIA diagnostic (no AA image)" };
        static int RequestedImageMode => _cfg.neuralMode == 3 ? 4 : _cfg.neuralMode == 2 ? 3 : _cfg.neuralMode == 1 ? 5 : (int)RequestedEyeAa;
        static int CycleImageValue(int current, int direction, int count) => (current + direction + count) % count;

        static void ChangeImageMode(int direction)
        {
            int mode = CycleImageValue(RequestedImageMode, direction, ImageModes.Length);
            _cfg.neuralMode = mode == 3 ? 2 : mode == 4 ? 3 : mode == 5 ? 1 : 0;
            SetEyeAa(mode <= 2 ? (EyeAaMode)mode : EyeAaMode.Taa);
            MarkSettingsDirty();
        }

        static OverlayOption ImageValue(string label, string description, Func<string> value, Action<int> change,
            Func<float> bar = null, Func<bool> enabled = null) => new OverlayOption {
            Label = label, Description = description, Value = value, Change = change, Slider01 = bar, Enabled = enabled
        };

        static void SetImageSharpness(float value)
        {
            if (_cfg.neuralMode >= 2) _cfg.neuralSharpness = Mathf.Clamp01(value);
            else { _cfg.taaSharpness = Mathf.Clamp01(value); _renderTargets.Clear(); }
            MarkSettingsDirty(); ResetPerformanceWindow();
        }

        static OverlayOption ImageToggle(string label, string description, Func<bool> value, Action<bool> assign) =>
            ImageValue(label, description, () => ModLocalization.Text(value() ? "On" : "Off"), direction => {
                assign(!value()); MarkSettingsDirty(); ResetPerformanceWindow();
            });

        static OverlayOption ImageGroup(string label, string description, params OverlayOption[] options) => new OverlayOption {
            Label = label, Description = description, Menu = new OverlayMenu(label, options)
        };

        static OverlayMenu BuildImageMenu()
        {
            var advanced = BuildAdvancedImageMenu();
            var image = advanced.Options[0].Menu;
            var combatEffects = CombatMinimumEffectsOption();
            var allDiagnostics = AllDiagnosticsOption77();
            var ofxr = OfxrOption77();
            image.Options.Insert(image.Options.Count - 2, combatEffects);
            var performance = advanced.Options.Find(option => option.LabelKey == "Performance").Menu;
            performance.Options.Insert(0, allDiagnostics);
            performance.Options.Insert(1, ofxr);
            var runtime = OpenXrRuntimeOption();
            advanced.Options[advanced.Options.Count - 3].Menu.Options.Insert(0, runtime);
            var language = ImageValue("Mod language", "Initially follows the game: Spanish if the game uses Spanish, otherwise English. Your choice applies immediately to all mod menus, help and messages, is saved and takes priority on later launches. Original game text keeps its own language.",
                () => ModLocalization.Language == 1 ? "Castellano" : "English",
                direction => SetModLanguage(CycleImageValue(ModLocalization.Language, direction, 2)));
            // One option instance per setting, shared by both paths. This also
            // shares the availability gate: DLSS scale is read-only in every
            // menu when a different render mode is selected.
            advanced.Options[advanced.Options.Count - 3].Menu.Options.Insert(0, language);
            var placement = InterfacePlacementOptions80(advanced);
            var recenter = FollowRecenterOption80();
            var monitor = ImageToggle("Monitor image", "Show VR on the monitor. Off keeps the window black and skips its eye copy and redundant world view. Headset menus remain available; Unity may still present the window.",
                () => _cfg.monitorImage, value => { _cfg.monitorImage = value; _desktopRecovery.Reset(); _desktopFallback = false; });
            performance.Options.Insert(0, monitor);
            FindLayoutOption80(advanced,"Tabletop").Menu.Options.Insert(0,recenter);
            return _qualityRoot = new OverlayMenu("Main menu", new[] {
                QualityPresetOption(), image.Options[0], image.Options[1], image.Options[2], image.Options[6], placement, ofxr, combatEffects, runtime, language,
                ExplorationZoomLimitOption(), recenter, monitor, allDiagnostics, SaveCustomOption(), ResetSettingsOption(),
                advanced.Options.Find(option => option.LabelKey == "Help · Touch controls"), AdvancedWarningOption(advanced)
            }, root: true);
        }

        static OverlayMenu BuildAdvancedImageMenu() => new OverlayMenu("Advanced settings", new[] {
          ImageGroup("Image", "Adjust antialiasing, DLSS/DLAA, resolution and sharpness while playing. The status below reports the mode actually in use.",
            ImageValue("Render mode", "TAA smooths edges using previous frames; SMAA is non-temporal. DLAA works at full resolution; DLSS reconstructs from fewer pixels. Diagnostic tests NVIDIA while displaying the current image without AA. NVIDIA never computes TAA as a fallback.", () => ModLocalization.Text(ImageModes[RequestedImageMode]), ChangeImageMode),
            ImageValue("Output resolution", "Final resolution sent to each eye. 100% uses the recommendation of the selected OpenXR runtime. Higher values can reveal more detail and cost more GPU time. Changing resolution may briefly pause the image.", () =>
                (RequestedOutputScale * 100).ToString("0", ModLocalization.Culture) + "%   ·   " + OpenXR.Width + " × " + OpenXR.Height +
                (_outputPending ? "   " + ModLocalization.Text("(change pending)") : ""),
                direction => RequestOutputScale(Mathf.Round((RequestedOutputScale + direction * .05f) * 100) / 100),
                () => (RequestedOutputScale - .25f) / 1.25f),
            ImageValue("DLSS scale", "DLSS only. Lower values reduce GPU work and fine detail. Range: 50–100%, in 5% steps. DLAA always uses the full output resolution.", () => (_cfg.neuralScale * 100).ToString("0", ModLocalization.Culture) +
                "%   ·   " + (_cfg.neuralMode == 3 ? NeuralTemporalHook.InternalSizeText() : ModLocalization.Text("select DLSS to apply")),
                direction => { _cfg.neuralScale = Mathf.Clamp(Mathf.Round((_cfg.neuralScale + direction * .05f) * 100) / 100, .5f, 1f); MarkSettingsDirty(); },
                () => (_cfg.neuralScale - .5f) / .5f, () => _cfg.neuralMode == 3),
            ImageValue("Sharpness · TAA / NVIDIA", "Sharpen edges without increasing resolution. High values may create halos or grain. TAA and NVIDIA have separate values. TAA sharpening does not affect SMAA or No antialiasing.", () =>
                (_cfg.neuralMode >= 2 ? _cfg.neuralSharpness : _cfg.taaSharpness).ToString("0.00", ModLocalization.Culture),
                direction => SetImageSharpness((_cfg.neuralMode >= 2 ? _cfg.neuralSharpness : _cfg.taaSharpness) + direction * .05f),
                () => _cfg.neuralMode >= 2 ? _cfg.neuralSharpness : _cfg.taaSharpness),
            ImageValue("NVIDIA model preset", "Choose Auto, J, K, L or M for both DLSS and DLAA. The NVIDIA runtime may substitute the request. Resolution is unchanged; the status separates requested and identified models.", () => NeuralPresets81.Label(_cfg.neuralPreset),
                direction => { _cfg.neuralPreset = NeuralPresets81.Cycle(_cfg.neuralPreset, direction); MarkSettingsDirty(); }),
            new OverlayOption { Label = "NVIDIA runtime", Description = "Recommended DLSS: 310.9.1.0. Other versions and NVIDIA overrides are allowed. Version and source describe the runtime detected by NGX; unknown identity does not block it. A real backend failure displays the current image without antialiasing. Restart the game after replacing a DLL.",
                Value = NeuralTemporalHook.RuntimeStatusText },
            ImageValue("Engine effects", "Original keeps the game effects. Reduced removes volumetric lighting/fog and screen-space reflections in VR. Minimal also removes ambient occlusion, bloom and camera blur. Keeps particles, tactical targeting and interface. Compare in the same scene; CPU stalls may remain.",
                EngineEffectProfileText,
                direction => SetEngineEffectProfile(CycleImageValue(EngineEffectProfile, direction, 3)), enabled: () => EngineEffectProfileSupported)),
          ImageGroup("Draw distance", "Limit how far the scene is drawn in VR. This can reduce slowdowns when looking across large areas. A short distance may hide the far background.",
            ImageValue("Distance profile", "Choose the game default, 100, 60 or Custom. A shorter distance may improve frame rate and remove distant scenery. Locked during an automatic comparison.", () => DrawDistanceDisplayName(RequestedDrawDistance, _cfg.drawDistanceCustom),
                direction => SetDrawDistanceMode(CycleImageValue(_cfg.drawDistanceMode, direction, 4)),
                enabled: () => !_drawDistanceTrial.Running && !_drawDistanceStartGate.Armed),
            ImageValue("Custom distance", "Select Custom and set the range from 20 to 400 scene units, in steps of 2. Reducing it may improve frame rate; check that scenery you need remains visible.", () => ModLocalization.Format("{0} units", _cfg.drawDistanceCustom.ToString("0.#", ModLocalization.Culture)) +
                (_cfg.drawDistanceMode == 3 ? "" : "   " + ModLocalization.Text("(adjusting selects Custom)")),
                direction => SetCustomDrawDistance(_cfg.drawDistanceCustom + direction * 2),
                () => (_cfg.drawDistanceCustom - 20) / 380,
                () => !_drawDistanceTrial.Running && !_drawDistanceStartGate.Armed),
            ImageToggle("Adaptive distance · experimental", "Adjust range using height, viewing direction and both eyes. Shortens it only when a reliable ground plane is found. Your manual ceiling returns at the horizon. Multi-level scenes may lose background; use Manual if this occurs.",
                () => _cfg.adaptiveDistance, value => _cfg.adaptiveDistance = value),
            new OverlayOption { Label = "Applied distance", Description = "Current range and manual ceiling in scene units. Adaptive range shortens gradually and expands immediately. Suspended during cinematics and automatic comparisons.", Value = () => AdaptiveDistanceStatus }),
          ImageGroup("Tabletop", "Adjust movement, rotation and zoom response. The tabletop keeps its comfort limits. Recenter uses your current seated pose as the reference.",
            ImageToggle("Contextual control hints", "Show a discreet reminder of the controls available in the current context. Original game information remains visible.",
                () => TouchContextHintsEnabled, SetTouchContextHintsEnabled),
            ContextHintPlacementOptions(),
            ImageToggle("Deep-trigger first-person movement", "Hold the right trigger fully for two seconds on a valid destination to move in first person, in exploration or ground combat. Only an accepted native move enters head view. A short click moves normally.",
                () => TouchCombatGroundDoubleClickHeadEnabled, SetTouchCombatGroundDoubleClickHead),
            ImageToggle("Follow moving characters", "Smoothly follow characters moved with the right stick. Forward, backward and sideways movement follow the right controller aiming direction at departure; turning your wrist steers. Camera follow cannot feed its rotation back into movement. Centre the stick to establish a new aiming reference. Off keeps the table still.",
                () => _cfg.touchThirdPersonFollow, SetTouchThirdPersonFollow),
            ImageToggle("Draw distance shortcut", "Off by default. Enable left thumbrest touch + right stick up/down to change draw distance. When off, resting your thumb cannot change rendering range; the Draw distance menu remains available.",
                () => _cfg.touchDrawDistanceShortcut, SetTouchDrawDistanceShortcut),
            ImageValue("Movement speed · Touch", "Adjust how far the tabletop moves when you move one or both hands while holding the grips. Physical hand and head tracking remain unchanged. Tracking jumps still have a speed limit.",
                () => (TouchMoveSpeed * 100).ToString("0", ModLocalization.Culture) + "%",
                direction => SetTouchMoveSpeed(Mathf.Round((TouchMoveSpeed + direction * .05f) * 100) / 100),
                () => (TouchMoveSpeed - .25f) / 1.75f),
            ImageValue("Rotation speed · Touch", "Adjust tabletop rotation and tilt speed. 100% is the initial response; lower values move the view more slowly. Physical head tracking remains unchanged.",
                () => (TouchTurnSpeed * 100).ToString("0", ModLocalization.Culture) + "%",
                direction => SetTouchTurnSpeed(Mathf.Round((TouchTurnSpeed + direction * .05f) * 100) / 100),
                () => (TouchTurnSpeed - .25f) / 1.75f),
            ImageValue("Gesture rotation sensitivity", "Adjust rotation with both Touch grips only. The initial response is 15% slower than before. Does not change stick turning, zoom, group movement or physical head tracking.",
                () => (TouchGestureTurnSpeed * 100).ToString("0", ModLocalization.Culture) + "%",
                direction => SetTouchGestureTurnSpeed(Mathf.Round((TouchGestureTurnSpeed + direction * .05f) * 100) / 100),
                () => (TouchGestureTurnSpeed - .25f) / 1.75f),
            ImageValue("Zoom speed · Touch", "Adjust how much the tabletop scale changes as you move both hands together or apart while holding the grips. 100% is the initial response. Scale limits remain active.",
                () => (TouchZoomSpeed * 100).ToString("0", ModLocalization.Culture) + "%",
                direction => SetTouchZoomSpeed(Mathf.Round((TouchZoomSpeed + direction * .05f) * 100) / 100),
                () => (TouchZoomSpeed - .25f) / 1.75f),
            new OverlayOption { Label = "Recenter view", Value = () => ModLocalization.Text("Use current head position"),
                Description = "Use your current headset position and orientation as the reference. Sit in your playing position, then press A or the right trigger.",
                Action = () => { _recenterPending = true; }, ActionLabel = "recenter" }),
          ImageGroup("Interface", "Configure the game panel, indicators and flat menu screen. These settings affect the game interface. This configuration panel is positioned separately.",
          ImageGroup("Hands and indicators", "Control the visibility of hands, gesture help and scene indicators.",
            ImageToggle("Gesture help", "Show servo-skull gesture hints near the upper-right corner: movement, rotation, tilt, zoom and selection. Hiding the hints keeps the pointer and all controls.", () => _cfg.touchGestureHelp, value => _cfg.touchGestureHelp = value),
            ImageToggle("3D servo-skulls", "Show a fully 3D servo-skull following each hand. Rotate the Touch controllers to inspect it. Hiding the models keeps aiming available and lets you compare their rendering cost.", () => _cfg.touchHandsVisible, value => _cfg.touchHandsVisible = value),
            ImageToggle("Unit indicators", "Place unit indicators above their characters in the 3D scene. Off returns these indicators to the flat game panel.", () => _cfg.worldOvertips, value => ToggleWorldOvertips()),
            ImageValue("Marker size", "Limit the extra marker enlargement used to retain legibility as tabletop scale changes. 1.0 removes this extra enlargement; higher values allow larger markers.", () => _cfg.markerBoostCap.ToString("0.0", ModLocalization.Culture) + " ×",
                direction => { _cfg.markerBoostCap = Mathf.Clamp(_cfg.markerBoostCap + direction * .25f, MarkerBoostMin, MarkerBoostMax); MarkSettingsDirty(); },
                () => Mathf.InverseLerp(MarkerBoostMin, MarkerBoostMax, _cfg.markerBoostCap))),
          ImageGroup("Game panel", "The keyboard-and-mouse interface adapts its windows to the headset aspect ratio. Adjust size, aspect and position without restarting.",
            ImageValue("Management window size", "Fit inventory, equipment and management windows inside your view. This size applies to complete windows, including native flat menus, independently of dialogue HUD settings and wheels.",
                () => (MenuWindowWidth * 100).ToString("0", ModLocalization.Culture) + "%",
                direction => SetMenuWindowWidth(MenuWindowWidth + direction * .025f), () => (MenuWindowWidth - .45f) / .55f),
            ImageValue("Management window distance", "Move management windows nearer or farther without changing dialogue HUD placement or wheels.",
                () => _cfg.uiMenuDistance.ToString("0.00", ModLocalization.Culture) + " m", d => SetMenuWindowDistance(_cfg.uiMenuDistance + d * .05f), () => (_cfg.uiMenuDistance - .5f) / 2.5f),
            ImageValue("Management window proportions", "Original keeps the game aspect ratio. Manual proportions reflow tabletop management windows; native flat screens change their displayed proportions. Aiming follows the displayed window.",
                () => MenuWindowAspect <= 0 ? ModLocalization.Text("Original") : MenuWindowAspect.ToString("0.0", ModLocalization.Culture) + " : 1", CycleMenuWindowAspect),
            ImageValue("Management horizontal position", "Move management windows left or right independently of dialogue HUD placement and wheels.",
                () => (_cfg.uiMenuOffsetX * 100).ToString("0.#", ModLocalization.Culture) + "%", d => SetMenuWindowOffsetX(_cfg.uiMenuOffsetX + d * .025f), () => (_cfg.uiMenuOffsetX + .65f) / 1.3f),
            ImageValue("Management vertical position", "Move management windows up or down independently of dialogue HUD placement and wheels.",
                () => (_cfg.uiMenuOffsetY * 100).ToString("0.#", ModLocalization.Culture) + "%", d => SetMenuWindowOffsetY(_cfg.uiMenuOffsetY + d * .025f), () => (_cfg.uiMenuOffsetY + .65f) / 1.3f),
            ImageToggle("Hide persistent HUD panels", "Use the wheels in exploration and combat. Hides the usual party, action and menu bars while retaining dialogues, tutorials and opened windows. Turn off to restore the original panels or compare their cost.", () => PcHudMinimal, SetPcHudMinimal),
            ImageValue("Service window text size", "Adjust text and buttons in tabletop management windows. Native flat screens use Management window size and proportions instead. Dialogue HUD settings stay unchanged.",
                () => (MenuWindowScale * 100).ToString("0", ModLocalization.Culture) + "%",
                direction => SetMenuWindowScale(Mathf.Round((MenuWindowScale + direction * .05f) * 100) / 100),
                () => (MenuWindowScale - .65f) / .85f),
            ImageValue("Text and button size", "Enlarge the HUD content from 100% to 180% inside the same panel area. Reflows the keyboard-and-mouse layout instead of pushing its edges farther apart. Applies immediately; very large values can crowd fixed-size game windows. Does not lower interface resolution or move world/map markers.",
                () => (_cfg.uiElementScale * 100).ToString("0", ModLocalization.Culture) + "%",
                direction => SetHudElementScale(Mathf.Round((_cfg.uiElementScale + direction * .05f) * 100) / 100),
                () => (_cfg.uiElementScale - 1) / .8f),
            HudPresetMenu(),
            ImageToggle("Show game panel", "Show or hide the game interface inside the tabletop. This configuration panel remains available so you can turn it back on.", () => _cfg.uiEnabled, SetUiEnabled),
            ImageValue("Panel distance", "Move the game panel nearer to or farther from your head, in virtual metres. Does not reposition this configuration panel.", () => _cfg.uiDistance.ToString("0.00", ModLocalization.Culture) + " m",
                direction => SetUiDistance(_cfg.uiDistance + direction * UiDistStep),
                () => Mathf.InverseLerp(UiDistMin, UiDistMax, _cfg.uiDistance)),
            ImageValue("Whole panel size", "Enlarge the regular game HUD, including its outer edges. 100% uses the binocular area; maximum 180%. Management windows have their own size setting. Text and button size changes content inside these edges.", () => (_cfg.uiWidth * 100).ToString("0", ModLocalization.Culture) + "%",
                direction => SetUiWidth(_cfg.uiWidth + direction * UiWidthStep),
                () => Mathf.InverseLerp(UiWidthMin, UiWidthMax, _cfg.uiWidth)),
            ImageValue("HUD aspect ratio", "Headset adapts the interface to the binocular field of view. Manual values change width-to-height ratio without stretching text. Affects the tabletop HUD; flat menus keep the original window ratio.", () => HudAspectDescription, CycleHudAspect),
            ImageValue("HUD horizontal position", "Move the regular HUD left or right by up to 65% of its width. Management windows use their own horizontal position. Extreme values may place buttons outside your view. Aiming follows the displayed panel.", () => (_cfg.uiOffsetX * 100).ToString("0.#", ModLocalization.Culture) + "%",
                direction => SetHudOffsetX(_cfg.uiOffsetX + direction * .025f), () => (_cfg.uiOffsetX + .65f) / 1.3f),
            ImageValue("HUD vertical position", "Move the regular HUD up or down by up to 65% of its height. Negative moves down; positive moves up. Management windows use their own vertical position.", () => (_cfg.uiOffsetY * 100).ToString("0.#", ModLocalization.Culture) + "%",
                direction => SetHudOffsetY(_cfg.uiOffsetY + direction * .025f), () => (_cfg.uiOffsetY + .65f) / 1.3f),
            ImageValue("Interface resolution", "Actual pixels for flat menus and the independent HUD. Does not change text size or eye resolution. Higher values may improve clarity and cost GPU time. Changing the window may briefly pause the image. Actual shows the applied size.", () => HudRasterDescription, CycleHudRaster),
            ImageToggle("Full-resolution HUD", "Render the PC panel and world unit indicators at a resolution independent of DLSS. Preserves masks, drawing order and aiming. Turn off to compare with the earlier path, whose clarity depends on each eye internal resolution.", () => _cfg.uiFullResolution, value => _cfg.uiFullResolution = value),
            ImageToggle("Flat screen in menus", "Show the game on a flat screen when a full-screen menu suspends the tabletop. Touch pointing remains available in those menus.", () => _cfg.desktopMirror, value => _cfg.desktopMirror = value))),
          SpatialInterfaceMenu(),
          ImageGroup("Performance", "Mod options that reduce rendering work and record timings. Their effect depends on the scene and active effects. Compare while holding the same view.",
            ImageValue(OccludedDrawLabel, OccludedDrawHelp, () => OccludedDrawStatus, direction => SetOccludedGeometryDrawSuppression(!_cfg.skipFullyFadedScenery)),
            ImageToggle("Familiar effects only with Y", "In ground combat, hide identified persistent familiar lights and visual effects until you hold Y. Keeps familiar models, rules, attacks and audio. Outside combat their normal appearance returns.", () => _cfg.familiarEffectsOnY76, value => _cfg.familiarEffectsOnY76 = value),
            ImageToggle("Coalesce popup layout", "Avoid repeatedly updating every panel when a popup tries several consecutive positions. Keeps an update per popup. Turn off to compare with the previous path.", () => _cfg.coalesceHudLayout, value => _cfg.coalesceHudLayout = value),
            ImageToggle("Stable HUD geometry", "Keep panel geometry fixed while moving your head or zooming the table. Reduces repeated Canvas work without lowering HUD resolution. Turn off for a live comparison; maps keep their registered projection.", () => _cfg.stableHudCapture, value => _cfg.stableHudCapture = value),
            ImageToggle("Separate unit indicators", "Update each unit indicator separately. May reduce CPU work but increase draw calls and pointing cost. Off by default. Compare in the same combat view.", () => _cfg.isolateOvertipBatches, value => _cfg.isolateOvertipBatches = value),
            ImageToggle("Reduce monitor rendering", "Reuse one eye image for the monitor instead of drawing the world again. Can reduce work in VR. Both eyes continue to render separately.", () => _cfg.skipDesktopWorld, value => _cfg.skipDesktopWorld = value),
            ImageToggle("Monitor copy comparison", "Session-only measurement: omit the eye copy and clear the window while retaining the selected world-render policy. Use with traces enabled; turn off after comparing.", () => _monitorCopyProbe81, value => _monitorCopyProbe81 = value),
            ImageValue("Allow game FSR", "Allow the game FSR setting to affect both eyes. May reduce detail to save GPU time. Suspended while DLSS, DLAA or NVIDIA Diagnostic is active.", () =>
                ModLocalization.Text(!_cfg.allowGameFsr ? "Off" : _cfg.neuralMode != 0 ? "Allowed; suspended with NVIDIA" : "Allowed"),
                direction => { _cfg.allowGameFsr = !_cfg.allowGameFsr; MarkSettingsDirty(); ResetPerformanceWindow(); }),
            ImageToggle("Cull objects outside view", "Reduce drawing outside the visible headset area. A margin is preserved. Culling may be suspended when effects such as reflections need those regions.", () => _cfg.visibleRegionCulling, value => _cfg.visibleRegionCulling = value),
            ImageToggle("Cull instances outside view", "Cull repeated objects drawn by the GPU outside the view. May reduce rendering work. Suspended when effects or viewing direction require the full image.", () => _cfg.indirectVisibleRegionCulling, value => _cfg.indirectVisibleRegionCulling = value),
            ImageToggle("Synchronize visible effects", "Update which effects the VR view needs when monitor rendering is reduced. Considers both eyes rather than relying on a camera that is no longer drawn.", () => _cfg.syncForcedVisibility, value => _cfg.syncForcedVisibility = value),
            new OverlayOption { Label = "New measurement", Value = () => ModLocalization.Text("Reset current samples"),
                Description = "Clear recent frame-rate and timing samples to begin a fresh comparison. Quality settings and previously saved diagnostic files are preserved.",
                Action = ResetLiveMeasurements, ActionLabel = "reset samples", Enabled = () => DiagnosticsRecording },
            EngineCadenceMenu(), EngineOptimizationMenu58(), CombatPresentationMenu74()),
          ImageGroup("Visual effects", "Adjust grids, unit markers and character highlights throughout the VR scene, in exploration and combat. Preserves the game's information and rules.",
            ImageToggle("Deployment aura", "Show the enveloping preparation glow while placing units before combat. Off removes this visual effect while preserving preparation rules, placement markers and other buffs. Can be changed during deployment.",
                () => PreparationAuraEnabled, SetPreparationAuraEnabled),
            ImageValue("Grids and areas", "Dim scene grids wherever the game displays them, preserving their boundaries, range and colours. Changes visual intensity without altering gameplay calculations.", () => (_cfg.combatSurfaceIntensity*100).ToString("0", ModLocalization.Culture)+" %",
                d=>{_cfg.combatSurfaceIntensity=Mathf.Clamp(_cfg.combatSurfaceIntensity+d*.05f,.25f,1);MarkSettingsDirty();},()=> (_cfg.combatSurfaceIntensity-.25f)/.75f),
            ImageValue("Markers and reticles", "Dim native unit markers in exploration and combat. Keeps shapes and colours to distinguish targets, selected units and speakers. Does not remove attack effects.", ()=>(_cfg.combatUnitFxIntensity*100).ToString("0", ModLocalization.Culture)+" %",
                d=>{_cfg.combatUnitFxIntensity=Mathf.Clamp(_cfg.combatUnitFxIntensity+d*.05f,.25f,1);MarkSettingsDirty();},()=> (_cfg.combatUnitFxIntensity-.25f)/.75f),
            ImageValue("Character highlight", "Original keeps the outline. Dimmed reduces its intensity. Base circle replaces it; No outline hides it. The last two modes help compare outline cost. Other tactical markers remain.",
                ()=>ModLocalization.Text(new[]{"Original","Dimmed","Base circle","No outline"}[_cfg.combatOutlineMode]),
                d=>{_cfg.combatOutlineMode=CycleImageValue(_cfg.combatOutlineMode,d,4);MarkSettingsDirty();ResetPerformanceWindow();}),
            ImageValue("Outline intensity", "Adjust outline intensity in Dimmed mode. Does not change the base circle or other highlight modes.", ()=> (_cfg.combatOutlineIntensity*100).ToString("0", ModLocalization.Culture)+" %",
                d=>{_cfg.combatOutlineIntensity=Mathf.Clamp(_cfg.combatOutlineIntensity+d*.05f,.1f,1);MarkSettingsDirty();},()=> (_cfg.combatOutlineIntensity-.1f)/.9f)),
          TouchGuideMenuOption(),
          ImageGroup("Advanced", "Startup and compatibility settings. Vertical image flips correct an upside-down image and last for this session only.",
            ImageToggle("Start VR automatically", "Start the selected OpenXR runtime with the game when the headset is available. Saved for future launches. Changing it does not stop the current VR session.", () => _cfg.autoStartVr, value => { _cfg.autoStartVr = value; _autoStartArmed = value; }),
            ImageToggle("Flip eye images · this session", "Flip both eye images vertically if they appear upside down. Does not swap left and right. Resets on the next launch.", () => OpenXR.FlipEyes, value => OpenXR.FlipEyes = value),
            ImageToggle("Flip flat screen · this session", "Flip the headset flat screen vertically if it appears upside down. Does not change the stereo tabletop. Resets on the next launch.", () => OpenXR.FlipFlat, value => OpenXR.FlipFlat = value))
        });

        static string LiveImageStatus()
        {
            string mode = _cfg.neuralMode != 0 || NeuralTemporalHook.IsFaulted ? NeuralTemporalHook.StatusLine() :
                ModLocalization.Format("Active image: {0}", _aaHasApplied ? ModLocalization.Text(EyeAaOptions.Name(EffectiveEyeAa)) : ModLocalization.Text("preparing"));
            string output = _outputPending ? OutputStatusText : ModLocalization.Format("Output: {0} × {1} per eye", OpenXR.Width, OpenXR.Height);
            string notice = NeuralTemporalHook.RuntimeNoticeText();
            return NeuralRuntimeInfo.ImageStatus(mode, output, LiveMeasurementLine(), notice);
        }
    }
}
