using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        internal const string MainMenuCredit = "MOD VR By Beren5556";
        static Harmony _mainMenuBrandingHooks;
        static GameObject _mainMenuBranding;
        static object _mainMenuBrandingOwner;
        static Graphic _mainMenuBrandingText;
        static int _mainMenuBrandingLanguage = -1;
        internal static void InstallMainMenuBranding()
        {
            if (_mainMenuBrandingHooks != null) return;
            try
            {
                var type = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.MainMenu.PC.MainMenuPCView");
                var bind = AccessTools.DeclaredMethod(type,"BindViewImplementation",Type.EmptyTypes);
                var destroy = AccessTools.DeclaredMethod(type,"DestroyViewImplementation",Type.EmptyTypes);
                if (bind == null || destroy == null || bind.ReturnType != typeof(void) || destroy.ReturnType != typeof(void))
                    throw new MissingMethodException("Official main-menu view lifecycle");
                _mainMenuBrandingHooks = new Harmony("RTMaquetaXR.MainMenuBranding");
                _mainMenuBrandingHooks.Patch(bind,postfix:new HarmonyMethod(typeof(Main),nameof(MainMenuBrandingBound)));
                _mainMenuBrandingHooks.Patch(destroy,prefix:new HarmonyMethod(typeof(Main),nameof(MainMenuBrandingDestroyed)));
            }
            catch (Exception error)
            {
                _mainMenuBrandingHooks?.UnpatchAll(_mainMenuBrandingHooks.Id); _mainMenuBrandingHooks = null;
                _log.Error("[presentation/menu] Credit unavailable: " + error.Message);
            }
        }
        static void MainMenuBrandingBound(object __instance)
        {
            try
            {
                var view = __instance as Component;
                if (view == null || (ReferenceEquals(__instance,_mainMenuBrandingOwner) && _mainMenuBranding != null)) return;
                ReleaseMainMenuBranding();
                // Verified native prefab path. Inherit its scaler, clipping and
                // capture treatment; do not create a competing screen canvas.
                var parent = view.transform.Find("UICanvas") as RectTransform;
                if (parent == null) return;
                var obj = new GameObject("RTMaquetaXR main menu credit",typeof(RectTransform),typeof(CanvasRenderer));
                _mainMenuBranding = obj; _mainMenuBrandingOwner = __instance;
                obj.layer = parent.gameObject.layer; var rect = (RectTransform)obj.transform; rect.SetParent(parent,false);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f,0); rect.pivot = new Vector2(.5f,0);
                rect.anchoredPosition = new Vector2(0,36); rect.sizeDelta = new Vector2(600,38);
                // Early sibling draws beneath native modal windows. Bottom
                // center leaves the left command list/right news panel clear.
                rect.SetAsFirstSibling();
                var sideBar = AccessTools.Field(__instance.GetType(),"m_MainMenuSideBarPCView")?.GetValue(__instance) as Component;
                var label = sideBar != null ? sideBar.transform.Find("ButtonsPanel/Continue/Label01") : null;
                var tmpType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
                var original = label != null && tmpType != null ? label.GetComponent(tmpType) : null;
                if (original != null)
                {
                    var text = obj.AddComponent(tmpType) as Graphic;
                    if (text == null) throw new InvalidOperationException("Native menu typography is not a Graphic");
                    CopyMainMenuTextProperty(original,text,"font"); CopyMainMenuTextProperty(original,text,"fontSharedMaterial");
                    SetMainMenuTextProperty(text,"text",ModLocalization.Text(MainMenuCredit)); SetMainMenuTextProperty(text,"fontSize",26f);
                    SetMainMenuTextProperty(text,"enableAutoSizing",false); SetMainMenuTextProperty(text,"enableWordWrapping",false);
                    SetMainMenuTextProperty(text,"richText",false);
                    var alignment = AccessTools.Property(tmpType,"alignment");
                    alignment?.SetValue(text,Enum.Parse(alignment.PropertyType,"Center"),null);
                    text.raycastTarget = false; text.color = new Color(.85f,.83f,.70f,1);
                    _mainMenuBrandingText = text;
                }
                else
                {
                    var text = obj.AddComponent<Text>(); text.font = _liveFont != null ? _liveFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    text.text = ModLocalization.Text(MainMenuCredit); text.fontSize = 26; text.alignment = TextAnchor.MiddleCenter;
                    text.color = new Color(.85f,.83f,.70f,1); text.raycastTarget = false;
                    text.horizontalOverflow = HorizontalWrapMode.Overflow; text.supportRichText = false;
                    _mainMenuBrandingText = text;
                }
                _mainMenuBrandingLanguage = ModLocalization.Revision;
                _log.Log("[presentation/menu] Mod credit attached to native main-menu canvas; borrowed font=" + (original != null));
            }
            catch (Exception error) { ReleaseMainMenuBranding(); _log.Error("[presentation/menu] " + error.Message); }
        }
        static void CopyMainMenuTextProperty(object from,object to,string name)
        { var property = AccessTools.Property(from.GetType(),name); property?.SetValue(to,property.GetValue(from,null),null); }
        static void SetMainMenuTextProperty(object target,string name,object value)
        { AccessTools.Property(target.GetType(),name)?.SetValue(target,value,null); }
        static void MainMenuBrandingDestroyed(object __instance)
        { if (ReferenceEquals(__instance,_mainMenuBrandingOwner)) ReleaseMainMenuBranding(); }
        internal static void RefreshMainMenuBrandingLanguage()
        {
            if (_mainMenuBrandingText == null || _mainMenuBrandingLanguage == ModLocalization.Revision) return;
            if (_mainMenuBrandingText is Text text) text.text = ModLocalization.Text(MainMenuCredit);
            else SetMainMenuTextProperty(_mainMenuBrandingText,"text",ModLocalization.Text(MainMenuCredit));
            _mainMenuBrandingLanguage = ModLocalization.Revision;
        }
        static void ReleaseMainMenuBranding()
        {
            if (_mainMenuBranding != null) UnityEngine.Object.Destroy(_mainMenuBranding);
            _mainMenuBranding = null; _mainMenuBrandingOwner = null;
            _mainMenuBrandingText = null; _mainMenuBrandingLanguage = -1;
        }
        // There is no separate canvas: excluding the native menu would hide
        // the entire menu. The small label must follow ordinary game UI capture.
        internal static bool IsMainMenuBrandingCanvas(Canvas canvas) => false;
        internal static void StopMainMenuBranding()
        {
            ReleaseMainMenuBranding();
            if (!_appQuitting) _mainMenuBrandingHooks?.UnpatchAll(_mainMenuBrandingHooks.Id);
            _mainMenuBrandingHooks = null;
        }
    }
}
