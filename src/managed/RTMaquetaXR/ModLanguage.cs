using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static FieldInfo _gameLanguageInstance;
        static PropertyInfo _gameLanguageLocale;
        static float _nextGameLanguageCheck;

        // Metadata-verified against RogueTrader.GameCore: static Instance field,
        // instance CurrentLocale property returning Localization.Enums.Locale.
        // Poll only in automatic mode, once a second, including the initial menu.
        // CurrentLocale can throw before the game's providers are registered.
        static string ReadGameLanguage()
        {
            try
            {
                if (_gameLanguageInstance == null || _gameLanguageLocale == null)
                {
                    var type = AccessTools.TypeByName("Kingmaker.Localization.LocalizationManager");
                    if (type == null) return null;
                    _gameLanguageInstance = AccessTools.Field(type, "Instance");
                    _gameLanguageLocale = AccessTools.Property(type, "CurrentLocale");
                }
                object instance = _gameLanguageInstance?.GetValue(null);
                return instance == null ? null : _gameLanguageLocale?.GetValue(instance, null)?.ToString();
            }
            catch { return null; }
        }
        static void UpdateModLanguage(bool force = false)
        {
            if (!force && (_cfg.modLanguage != -1 || Time.unscaledTime < _nextGameLanguageCheck)) return;
            _nextGameLanguageCheck = Time.unscaledTime + 1f;
            string locale = _cfg.modLanguage == -1 ? ReadGameLanguage() : null;
            int before = ModLocalization.Revision;
            ModLocalization.SetLanguage(ModLocalization.ResolveLanguage(_cfg.modLanguage, locale));
            Localization.ActiveCode = ModLocalization.Language == 1 ? "es" : "en";
            if (before == ModLocalization.Revision) return;
            _liveNextTextUpdate = 0;
            _liveMessage = null;
            RefreshMainMenuBrandingLanguage();
        }
        static void SetModLanguage(int language)
        {
            _cfg.modLanguage = language == 1 ? 1 : 0;
            UpdateModLanguage(true);
            // Explicit override is durable immediately, even if VR stops or the
            // player exits directly after selecting the language.
            SaveSettings();
        }
    }
}
