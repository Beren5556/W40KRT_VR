using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    // Preparation has its own StartBattle button; EndTurn is not a substitute.
    // The right wheel exposes that live native control, including its current
    // deployment/co-op permission. Nothing starts automatically after a movie.
    public static partial class Main
    {
        static Type _touchStartBattleView;
        static PropertyInfo _touchStartBattleModel;
        static FieldInfo[] _touchStartBattleButtons;
        static Func<object, bool> _touchCanStartBattle;
        static bool _touchStartBattleAttempted;

        static void AddTouchNativeStartBattle()
        {
            if (_touchRadialContracts == null || _touchRadial.Side != 1 || InSpaceCombat || !TouchGroundPreparationNow()) return;
            if (!_touchStartBattleAttempted)
            {
                _touchStartBattleAttempted = true;
                try
                {
                    _touchStartBattleView = AccessTools.TypeByName("Kingmaker.UI.MVVM.View.SurfaceCombat.PC.CombatStartWindowPCView");
                    _touchStartBattleModel = TouchRadialContracts.Property(_touchStartBattleView, "ViewModel");
                    _touchStartBattleButtons = new[] { TouchRadialContracts.Field(_touchStartBattleView, "m_StartBattleButton") };
                    _touchCanStartBattle = TouchRadialPartyContracts.Reactive<bool>(_touchStartBattleModel.PropertyType, "CanStartCombat");
                }
                catch (Exception error) { _touchStartBattleView = null; _log.Error("[tutorial/preparation] Native Start battle wheel entry unavailable: " + error.Message); }
            }
            if (_touchStartBattleView == null) return;
            int before = _touchRadialEntries.Count;
            AddTouchRadialMenus(_touchStartBattleView, _touchStartBattleModel, _touchStartBattleButtons, new[] { "Start battle" });
            for (int i = before; i < _touchRadialEntries.Count; i++) _touchRadialEntries[i].StartBattle = true;
        }

        static bool TouchNativeStartBattleAvailable(TouchRadialEntry entry)
        {
            if (entry == null || entry.View == null || entry.Button == null || _touchCanStartBattle == null || _touchRadialContracts == null) return false;
            return NativeTutorialInputPolicy.StartBattleAvailable(_active, TouchGroundPreparationNow(), NativeTutorialInputBlocked,
                entry.Model != null && ReferenceEquals(entry.ModelProperty.GetValue(entry.View, null), entry.Model),
                entry.Model != null && _touchCanStartBattle(entry.Model),
                entry.Button.gameObject.activeInHierarchy && (!(entry.Button is Behaviour behaviour) || behaviour.isActiveAndEnabled),
                (bool)_touchRadialContracts.Interactable.GetValue(entry.Button, null));
        }
    }
}
