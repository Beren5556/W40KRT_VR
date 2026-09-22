using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static TouchRadialPartyContracts _touchRadialPartyContracts;
        static Component _touchRadialPartyPanel;
        static object _touchRadialPartyModel;
        static readonly List<object> _touchRadialPartyModels = new List<object>(12), _touchRadialPartyUnits = new List<object>(12), _touchRadialPartyViews = new List<object>(12);
        static readonly List<bool> _touchRadialPartyEnabled = new List<bool>(12);
        static readonly List<Image> _touchRadialLevelSourceImages = new List<Image>(8);
        static string _touchRadialPartyFault;
        static void InstallTouchRadialParty()
        {
            try { _touchRadialPartyContracts = TouchRadialPartyContracts.Create(AccessTools.TypeByName); _touchRadialPartyFault = null; }
            catch (Exception error) { _touchRadialPartyContracts = null; _touchRadialPartyFault = error.Message; _log.Error("[touch/radial] Party portraits unavailable: " + error.Message); }
        }
        static void ClearTouchRadialParty()
        {
            _touchRadialPartyPanel = null; _touchRadialPartyModel = null;
            _touchRadialPartyModels.Clear(); _touchRadialPartyUnits.Clear(); _touchRadialPartyViews.Clear(); _touchRadialPartyEnabled.Clear();
        }
        static void OpenTouchRadialParty()
        {
            BeginTouchRadialCombatInformation();
            ClearTouchRadialParty(); var c = _touchRadialPartyContracts;
            if (c == null) { _touchRadialMessage = "Party panel unavailable"; return; }
            foreach (var item in FindTouchRadialViews(c.PanelView))
            {
                var view = item as Component; if (view == null || !view.gameObject.activeInHierarchy) continue;
                var model = c.PanelModel(view); if (model == null) continue;
                _touchRadialPartyPanel = view; _touchRadialPartyModel = model; break;
            }
            if (_touchRadialPartyPanel == null) { _touchRadialMessage = "Party panel not visible"; return; }
            var models = c.Models(_touchRadialPartyModel) as IList; var views = c.Views(_touchRadialPartyPanel) as IList;
            if (models == null || views == null) { _touchRadialMessage = "Party portraits are updating"; return; }
            c.Snapshot(models, _touchRadialPartyModels, _touchRadialPartyUnits, _touchRadialPartyEnabled);
            foreach (var view in views) _touchRadialPartyViews.Add(view);
            // The PC panel binds these lists index-for-index. Disabled/empty
            // slots are not characters; a page change reuses VM instances and
            // therefore must also invalidate their native unit identities.
            for (int i = 0; i < models.Count && i < views.Count; ++i)
            {
                var model = models[i]; var unit = _touchRadialPartyUnits[i]; var view = views[i] as Component;
                if (!_touchRadialPartyEnabled[i] || unit == null || view == null || !view.gameObject.activeInHierarchy || !ReferenceEquals(c.ViewModel(view), model)) continue;
                var portrait = c.PortraitModel(model); var portraitView = c.PortraitView.GetValue(view);
                var picture = portraitView == null ? null : (c.Dead(portrait) ? c.DeadImage : c.LifeImage).GetValue(portraitView) as Image;
                string nativeName = c.Name(model);
                var entry = new TouchRadialEntry { Character = true, Party = true, View = view, Model = model, Mechanic = unit,
                    Icon = picture != null && picture.sprite != null ? picture.sprite : c.Portrait(portrait),
                    IconColor = picture == null ? Color.white : picture.color, Label = nativeName ?? "Character", OwnLabel = nativeName == null };
                CaptureTouchRadialPartyLevelUpArt(entry, c.LevelUpButton.GetValue(view) as Component);
                entry.Enabled = TouchRadialPartyAvailable(entry); _touchRadialEntries.Add(entry);
            }
            if (_touchRadialEntries.Count == 0) _touchRadialMessage = "No party portraits available";
        }
        static bool TouchRadialPartyContextValid()
        {
            var c = _touchRadialPartyContracts;
            if (c == null || _touchRadialPartyPanel == null || !_touchRadialPartyPanel.gameObject.activeInHierarchy) return _touchRadialEntries.Count == 0;
            if (!ReferenceEquals(c.PanelModel(_touchRadialPartyPanel), _touchRadialPartyModel)) return false;
            return c.SameSlots(c.Models(_touchRadialPartyModel) as IList, _touchRadialPartyModels, _touchRadialPartyUnits, _touchRadialPartyEnabled) &&
                TouchRadialPartyContracts.SameViews(c.Views(_touchRadialPartyPanel) as IList, _touchRadialPartyViews);
        }
        static bool TouchRadialPartyAvailable(TouchRadialEntry entry)
        {
            var c = _touchRadialPartyContracts;
            if (c == null || entry.Mechanic == null || entry.View == null || !entry.View.gameObject.activeInHierarchy ||
                !ReferenceEquals(c.ViewModel(entry.View), entry.Model) || !ReferenceEquals(c.Unit(entry.Model), entry.Mechanic) || !c.Enabled(entry.Model)) return false;
            if (entry.StateLabel != null && Time.unscaledTime < entry.CharacterRefreshAt) return true;
            entry.CharacterRefreshAt = Time.unscaledTime + .15f;
            entry.Selected = c.Selected(entry.Model); entry.Current = entry.Enemy = entry.Neutral = false;
            bool previousLevelUp=entry.LevelUp;
            entry.LevelUp = c.LevelUpVisible(entry.Model);
            if(entry.LevelUp&&!previousLevelUp) CaptureTouchRadialPartyLevelUpArt(entry,c.LevelUpButton.GetValue(entry.View) as Component);
            var portrait = c.PortraitModel(entry.Model); entry.UnableToAct = c.Dead(portrait);
            string state = entry.UnableToAct ? "DEAD" : c.Crippled(portrait) ? "CRIPPLED" : entry.Selected ? "SELECTED" : "PARTY";
            SetTouchRadialPortraitText(entry, state, c.HpText(c.Health(entry.Model)));
            return true;
        }
        static void ExecuteTouchRadialParty(TouchRadialEntry entry)
        {
            if (!TouchRadialPartyContextValid() || !TouchRadialPartyAvailable(entry)) return;
            _touchRadialPartyContracts.Commit(entry.Mechanic);
            TouchCameraSelectionCommitted();
            // Wheel selection is selection only; explicit double trigger owns framing.
        }
        static void CaptureTouchRadialPartyLevelUpArt(TouchRadialEntry entry, Component button)
        {
            if (button == null) return;
            // Inspect this known button at catalogue opening or when its
            // native level-up flag becomes true; never on steady frames.
            // Read its original active art even if the containing HUD is hidden
            // by our CanvasGroup; never clone scripts or the level-up action.
            Image chosen = null; int best=-1;
            _touchRadialLevelSourceImages.Clear();button.GetComponentsInChildren(true,_touchRadialLevelSourceImages);
            foreach (var image in _touchRadialLevelSourceImages)
            {
                if (image == null || image.sprite == null) continue;
                string name=image.name.ToLowerInvariant();
                // The native button contains a transparent RaycastZone plus
                // Default/Active/Disabled art. A hidden parent does not make
                // that real arrow unavailable to the wheel.
                if(name.Contains("raycast"))continue;
                int score=(name.Contains("levelup")?20:0)+(name.Contains("default")?4:0)+
                    (image.gameObject.activeSelf?8:0)+(image.enabled?2:0);
                if(image.color.a<=.01f&&!name.Contains("levelup"))continue;
                if(score>best){chosen=image;best=score;}
            }
            if (chosen != null) { entry.LevelUpIcon = chosen.sprite;var color=chosen.color;color.a=1;entry.LevelUpColor=color; }
        }
    }
}
