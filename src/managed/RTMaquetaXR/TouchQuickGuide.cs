using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static GameObject _touchQuickGuideRoot;
        static bool _touchQuickGuideShown;
        static Text _touchQuickHeading, _touchQuickQuestion, _touchQuickHelp, _touchQuickYes, _touchQuickNo;
        static Image _touchQuickLogo, _touchQuickYesBacking, _touchQuickNoBacking;
        static Sprite _touchWelcomeLogo;
        static int _touchQuickLanguageRevision = -1, _touchQuickSelection = -1, _touchWelcomeAssetAttempts;
        static float _touchWelcomeNextAssets;
        static readonly List<GameObject> _touchQuickHidden = new List<GameObject>();
        static void UpdateTouchQuickGuide()
        {
            if (!TouchQuickGuideVisible) { HideTouchQuickGuide(); return; }
            if (_liveRoot == null) return;
            if (_touchQuickGuideRoot == null || _touchQuickGuideRoot.transform.parent != _liveRoot.transform) CreateTouchQuickGuide();
            if (_touchQuickLanguageRevision != ModLocalization.Revision) RefreshTouchQuickGuideLanguage();
            if (!_touchQuickGuideShown)
            {
                _touchQuickGuideShown = true; _touchQuickGuideRoot.SetActive(true); _touchQuickHidden.Clear();
                for (int i = 0; i < _liveRoot.transform.childCount; ++i)
                {
                    var child = _liveRoot.transform.GetChild(i).gameObject;
                    if (child == _touchQuickGuideRoot || (_overlayPointerDot != null && child == _overlayPointerDot.gameObject) || !child.activeSelf) continue;
                    _touchQuickHidden.Add(child); child.SetActive(false);
                }
            }
            if (_overlayPointerDot != null && _overlayPointerDot.transform.GetSiblingIndex() != _liveRoot.transform.childCount - 1) _overlayPointerDot.transform.SetAsLastSibling();
            if (_touchQuickSelection != _livePage)
            {
                _touchQuickSelection = _livePage;
                _touchQuickYesBacking.color = _livePage == 0 ? LiveSelection : LiveBackground;
                _touchQuickNoBacking.color = _livePage == 1 ? LiveSelection : LiveBackground;
                _touchQuickYes.color = _livePage == 0 ? LiveInk : LiveMuted; _touchQuickNo.color = _livePage == 1 ? LiveInk : LiveMuted;
            }
            RefreshTouchWelcomeLogo();
        }
        static void RefreshTouchWelcomeLogo()
        {
            if (_touchWelcomeLogo == null && _loadingLogo != null) _touchWelcomeLogo = _loadingLogo;
            if (_touchWelcomeLogo == null && _touchWelcomeAssetAttempts < 3 && Time.unscaledTime >= _touchWelcomeNextAssets)
            {
                ++_touchWelcomeAssetAttempts; _touchWelcomeNextAssets = Time.unscaledTime + 2;
                foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (sprite != null && sprite.name == "logo-start_TEMP_UI") { _touchWelcomeLogo = sprite; break; }
            }
            if (_touchQuickLogo != null && _touchQuickLogo.sprite != _touchWelcomeLogo)
            { _touchQuickLogo.sprite = _touchWelcomeLogo; _touchQuickLogo.enabled = _touchWelcomeLogo != null; }
        }
        static void HideTouchQuickGuide()
        {
            if (!_touchQuickGuideShown) return;
            _touchQuickGuideShown = false;
            if (_touchQuickGuideRoot != null) _touchQuickGuideRoot.SetActive(false);
            foreach (var child in _touchQuickHidden) if (child != null) child.SetActive(true);
            _touchQuickHidden.Clear(); _liveNextTextUpdate = 0;
        }
        static void CreateTouchQuickGuide()
        {
            _touchQuickGuideRoot = new GameObject("Welcome aboard · optional controls tutorial", typeof(RectTransform));
            _touchQuickGuideRoot.layer = 5;
            var root = (RectTransform)_touchQuickGuideRoot.transform;
            root.SetParent(_liveRoot.transform, false); root.sizeDelta = new Vector2(TouchQuickGuideLayout.Width, TouchQuickGuideLayout.Height);
            LiveTopImage("Welcome background", root, LiveBackground, 0, 0, TouchQuickGuideLayout.Width, TouchQuickGuideLayout.Height);
            LiveTopImage("Welcome brass rule", root, LiveFrame, 28, 23, TouchQuickGuideLayout.Width-56, 2);
            _touchQuickLogo = LiveTopImage("Original game logo", root, Color.white, 180, 45, 560, 155);
            _touchQuickLogo.sprite = _touchWelcomeLogo; _touchQuickLogo.preserveAspect = true; _touchQuickLogo.enabled = _touchWelcomeLogo != null;
            _touchQuickHeading = LiveText("Welcome heading", root, 23, FontStyle.Normal, LiveAccent, 40, -208, 840, 34);
            _touchQuickHeading.alignment = TextAnchor.MiddleCenter; _touchQuickHeading.font = _liveHeaderFont;
            _touchQuickQuestion = LiveText("Tutorial invitation", root, 31, FontStyle.Normal, LiveInk, 48, -262, 824, 78);
            _touchQuickQuestion.alignment = TextAnchor.MiddleCenter; FitLiveText(_touchQuickQuestion, 27, 31);
            _touchQuickYesBacking = LiveTopImage("Tutorial yes", root, LiveBackground, TouchQuickGuideLayout.YesX, TouchQuickGuideLayout.ButtonY, TouchQuickGuideLayout.ButtonWidth, TouchQuickGuideLayout.ButtonHeight);
            _touchQuickNoBacking = LiveTopImage("Tutorial no", root, LiveBackground, TouchQuickGuideLayout.NoX, TouchQuickGuideLayout.ButtonY, TouchQuickGuideLayout.ButtonWidth, TouchQuickGuideLayout.ButtonHeight);
            for (int i=0;i<2;++i) LiveTopImage(i==0?"Yes rule":"No rule", root, LiveFrame, i==0?TouchQuickGuideLayout.YesX:TouchQuickGuideLayout.NoX, TouchQuickGuideLayout.ButtonY+TouchQuickGuideLayout.ButtonHeight-2, TouchQuickGuideLayout.ButtonWidth, 2);
            _touchQuickYes = LiveText("Yes", root, 28, FontStyle.Bold, LiveInk, TouchQuickGuideLayout.YesX, -TouchQuickGuideLayout.ButtonY-8, TouchQuickGuideLayout.ButtonWidth, 40);
            _touchQuickNo = LiveText("No", root, 28, FontStyle.Bold, LiveInk, TouchQuickGuideLayout.NoX, -TouchQuickGuideLayout.ButtonY-8, TouchQuickGuideLayout.ButtonWidth, 40);
            _touchQuickYes.alignment = _touchQuickNo.alignment = TextAnchor.MiddleCenter;
            _touchQuickHelp = LiveText("Tutorial available later", root, 20, FontStyle.Normal, LiveMuted, 48, -438, 824, 66);
            _touchQuickHelp.alignment = TextAnchor.MiddleCenter; FitLiveText(_touchQuickHelp, 18, 20);
            RefreshTouchQuickGuideLanguage(); _touchQuickSelection = -1; _touchQuickGuideShown = false;
        }
        static void RefreshTouchQuickGuideLanguage()
        {
            _touchQuickHeading.text = "MOD VR By Beren5556";
            _touchQuickQuestion.text = ModLocalization.Text("Would you like to see the controls tutorial?");
            _touchQuickYes.text = ModLocalization.Text("YES"); _touchQuickNo.text = ModLocalization.Text("NO");
            _touchQuickHelp.text = ModLocalization.Text("Tutorial: F1 > Help · Touch controls.");
            _touchQuickLanguageRevision = ModLocalization.Revision;
        }
        static void DrawFlatTouchQuickGuide()
        {
            RefreshTouchWelcomeLogo();
            DrawFlatLiveRect(0,0,TouchQuickGuideLayout.Width,TouchQuickGuideLayout.Height,LiveBackground);
            DrawFlatLiveRect(28,23,TouchQuickGuideLayout.Width-56,2,LiveFrame);
            if (_touchWelcomeLogo != null)
            {
                var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(_touchWelcomeLogo); var size = _touchWelcomeLogo.rect.size;
                float ratio = size.x / Mathf.Max(1,size.y), width = Mathf.Min(560,155*ratio), height = width / ratio;
                GUI.color = Color.white;
                GUI.DrawTextureWithTexCoords(new Rect((TouchQuickGuideLayout.Width-width)*.5f,45+(155-height)*.5f,width,height),_touchWelcomeLogo.texture,new Rect(uv.x,uv.y,uv.z-uv.x,uv.w-uv.y));
            }
            else DrawFlatLiveText("WARHAMMER 40,000  ·  ROGUE TRADER",160,96,600,58,28,true,LiveInk);
            DrawFlatLiveText("MOD VR By Beren5556",280,208,420,34,23,false,LiveAccent);
            DrawFlatLiveText(ModLocalization.Text("Would you like to see the controls tutorial?"),48,262,824,78,31,false,LiveInk);
            for (int i=0;i<2;++i)
            {
                float x=i==0?TouchQuickGuideLayout.YesX:TouchQuickGuideLayout.NoX;
                DrawFlatLiveRect(x,TouchQuickGuideLayout.ButtonY,TouchQuickGuideLayout.ButtonWidth,TouchQuickGuideLayout.ButtonHeight,_livePage==i?LiveSelection:LiveBackground);
                DrawFlatLiveRect(x,TouchQuickGuideLayout.ButtonY+TouchQuickGuideLayout.ButtonHeight-2,TouchQuickGuideLayout.ButtonWidth,2,LiveFrame);
                DrawFlatLiveText(ModLocalization.Text(i==0?"YES":"NO"),x+88,TouchQuickGuideLayout.ButtonY+8,160,40,28,true,_livePage==i?LiveInk:LiveMuted);
            }
            DrawFlatLiveText(ModLocalization.Text("Tutorial: F1 > Help · Touch controls."),48,438,824,66,20,false,LiveMuted);
            DrawFlatOverlayTouchCursor();
        }
    }
}

