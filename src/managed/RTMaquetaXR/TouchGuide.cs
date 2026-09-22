using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchGuideLoadPolicy _touchGuideLoad = new TouchGuideLoadPolicy();
        static readonly Dictionary<OverlayOption, TouchControlAssignment> _touchGuideTopics = new Dictionary<OverlayOption, TouchControlAssignment>();
        static OverlayMenu _touchGuideMenu;
        static readonly HashSet<OverlayMenu> _touchGuideSections = new HashSet<OverlayMenu>();
        static OverlayMenu _touchQuickGuideMenu;
        static Harmony _touchGuideHarmony;
        static bool _touchGuideInstalled;
        static string _touchGuideStatus = "Not installed";
        static bool TouchGuideVisible => _liveNavigation.Visible && _touchGuideSections.Contains(_liveNavigation.Menu);
        static bool TouchQuickGuideVisible => _touchQuickGuideMenu != null && _liveNavigation.Visible && ReferenceEquals(_liveNavigation.Menu, _touchQuickGuideMenu);
        static TouchControlAssignment TouchGuideTopic => CurrentLiveOption() != null && _touchGuideTopics.TryGetValue(CurrentLiveOption(), out var topic) ? topic : null;

        static OverlayOption TouchGuideMenuOption()
        {
            _touchGuideTopics.Clear(); _touchGuideSections.Clear();
            var groups = new List<OverlayOption>();
            for (int i = 0; i < TouchControlAssignments.TutorialContexts.Length; ++i)
            {
                var options = new List<OverlayOption>();
                foreach (string id in TouchControlAssignments.TutorialAssignments[i])
                {
                    var assignment = TouchControlAssignments.Find(id);
                    var option = new OverlayOption {
                        LabelProvider = () => assignment.Title, DescriptionProvider = () => assignment.Description,
                        // Selecting a chapter displays its canonical illustration;
                        // only the menu's explicit Back/Close entries navigate out.
                        Action = () => { }, ActionLabel = "View"
                    };
                    _touchGuideTopics.Add(option, assignment); options.Add(option);
                }
                var section = new OverlayMenu(TouchControlAssignments.TutorialContexts[i], options);
                _touchGuideSections.Add(section);
                groups.Add(new OverlayOption { Label = TouchControlAssignments.TutorialContexts[i], Menu = section,
                    Description = "Choose a control to see its animation and explanation. Scroll the list with the right stick, or use the previous and next buttons. B returns to the sections." });
            }
            _touchGuideMenu = new OverlayMenu("Touch controls", groups); _touchGuideSections.Add(_touchGuideMenu);
            _touchQuickGuideMenu = new OverlayMenu("Welcome aboard", new[] {
                new OverlayOption { Label = "YES", Menu = groups[0].Menu,
                    Description = "Open the controls tutorial, organised by game context." }
            }, true);
            // OverlayMenu adds its standard close command. Present that exact
            // command as NO: no new input path, game action or pause is injected.
            _touchQuickGuideMenu.Options[1].Label = "NO";
            _touchQuickGuideMenu.Options[1].Description = "Close this invitation and continue playing. The tutorial remains available in the mod settings.";
            return new OverlayOption { Label = "Help · Touch controls", Description = "Animated guide to the current Touch controls and the F1 shortcut. You can return here at any time.", Menu = _touchGuideMenu };
        }

        internal static void InstallTouchGuide()
        {
            if (_touchGuideInstalled) return;
            try
            {
                var contracts = TouchGuideContracts.Create(AccessTools.TypeByName);
                _touchGuideHarmony = new Harmony("RTMaquetaXR.TouchGuide");
                foreach (var method in new[] { contracts.LoadGame, contracts.NewGame })
                    _touchGuideHarmony.Patch(method, prefix: new HarmonyMethod(typeof(Main), nameof(TouchGuideLoadRequested)),
                        finalizer: new HarmonyMethod(typeof(Main), nameof(TouchGuideLoadFinished)));
                _touchGuideHarmony.Patch(contracts.AreaReady, postfix: new HarmonyMethod(typeof(Main), nameof(TouchGuideAreaReady)));
                _touchGuideInstalled = true; _touchGuideStatus = "Ready";
            }
            catch (Exception error)
            {
                _touchGuideHarmony?.UnpatchAll(_touchGuideHarmony.Id); _touchGuideHarmony = null;
                _touchGuideStatus = "Manual help only: " + error.Message;
                _log.Error("[touch/guide] " + _touchGuideStatus);
            }
        }
        static void TouchGuideLoadRequested()
        {
            if (!_active) return;
            TouchCameraLoadRequested();
            if (!_liveStarted) return;
            _touchGuideLoad.Request(); HideLiveOverlay();
        }
        static Exception TouchGuideLoadFinished(Exception __exception)
        {
            TouchCameraLoadFinished(__exception);
            if (__exception != null) _touchGuideLoad.Cancel();
            return __exception;
        }
        static void TouchGuideAreaReady()
        {
            if (!_active) return;
            TouchCameraLoadAreaReady();
            if (_liveStarted) _touchGuideLoad.Loaded();
        }

        internal static void UpdateTouchGuide(bool sceneFrameReady = true)
        {
            // The closed, already-seen guide costs only these flags. No game
            // queries, canvas walk, animation component Update or allocation.
            if (!_touchGuideLoad.Pending && !TouchGuideVisible && !TouchQuickGuideVisible && !_touchGuideVisualShown && !_touchQuickGuideShown) return;
            // Manually visiting Help already fulfils the pending introduction;
            // closing it must not immediately open a second copy.
            if (_touchGuideLoad.Pending && (TouchGuideVisible || TouchQuickGuideVisible)) _touchGuideLoad.Acknowledge();
            if (_touchGuideLoad.Pending && _liveStarted && _touchGuideMenu != null)
            {
                var sample = CurrentTouchFrame;
                bool neutral = sample.ready != 0 && sample.focused != 0 && TouchGuideLoadPolicy.InputNeutral(
                    sample.left.buttons, sample.right.buttons, sample.left.trigger, sample.right.trigger,
                    sample.left.squeeze, sample.right.squeeze, sample.left.stickX, sample.left.stickY, sample.right.stickX, sample.right.stickY);
                bool mapReady = InNavigationMap && !PresentationTransition && _modeFlat &&
                    (_modeName == "GlobalMap" || _modeName == "StarSystem") && OpenXR.GetFlatPanel(out var panel) && panel.valid != 0;
                bool sceneReady = sceneFrameReady && _attached && !_modeFlat;
                bool normalPlay = _modeName == "Default" || (InSpaceCombat && _modeName == "SpaceCombat") || mapReady;
                if (_touchGuideLoad.Step(normalPlay && !_tutorialShowing && !TouchMenuWindowVisible && !CinematicWanted,
                    (sceneReady || mapReady) && LiveOverlayVr, neutral,
                    TouchOverlayOpen || TouchOverlayChordCaptured, Time.unscaledTime))
                {
                    CancelTouchGameGestures(); ResetOverlayTouchPointer();
                    string initialContext = InStarSystemMap ? "Galactic map · system planets" : InGalacticMap ? "Star map · warp routes" :
                        InSpaceCombat ? "Space combat" : TouchRadialCombatNow(out _) ? "Ground combat" : "On-foot exploration";
                    for (int i=0;i<TouchControlAssignments.TutorialContexts.Length;i++)
                        if(TouchControlAssignments.TutorialContexts[i]==initialContext) _touchQuickGuideMenu.Options[0].Menu=_touchGuideMenu.Options[i].Menu;
                    _liveNavigation.OpenMenu(_touchQuickGuideMenu); _liveNavigation.Select(1);
                    _liveMessage = null; _liveNextTextUpdate = 0;
                    _log.Log("[touch/guide] Opened after completed game load " + _touchGuideLoad.Requests);
                }
            }
            UpdateTouchGuideVisual();
            UpdateTouchQuickGuide();
        }
        internal static void StopTouchGuide()
        {
            CancelTouchCameraLoadFocus();
            _touchGuideLoad.Cancel(); HideTouchGuideVisual(); HideTouchQuickGuide();
        }
        internal static object TouchGuideSnapshot() => new {
            Installed = _touchGuideInstalled, Status = _touchGuideStatus, Pending = _touchGuideLoad.Pending,
            AreaReady = _touchGuideLoad.AreaReady, LoadRequests = _touchGuideLoad.Requests, AutomaticShows = _touchGuideLoad.Shown,
            Visible = TouchGuideVisible || TouchQuickGuideVisible, QuickPage = TouchQuickGuideVisible,
            Topic = TouchGuideTopic?.Id, ControlSource = "TouchControlAssignments: shared binding, summary, gesture and details"
        };
    }
}
