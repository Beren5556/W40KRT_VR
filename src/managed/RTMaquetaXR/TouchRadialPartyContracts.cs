using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RTMaquetaXR
{
    internal sealed class TouchRadialPartyContracts
    {
        internal Type PanelView, CharacterView, UnitModel;
        internal Func<object, object> PanelModel, Models, Views, ViewModel, Unit, PortraitModel, Health;
        internal Func<object, Sprite> Portrait;
        internal Func<object, string> Name, HpText;
        internal Func<object, bool> Enabled, Selected, Dead, Crippled, LevelUp, InCombat, ServiceWindowAvailable;
        internal FieldInfo PortraitView, LifeImage, DeadImage, LevelUpButton;
        internal TouchRadialCharacterContracts Navigation;
        internal static TouchRadialPartyContracts Create(Func<string, Type> resolve)
        {
            Func<string, Type> type = name => resolve(name) ?? throw new TypeLoadException(name);
            var c = new TouchRadialPartyContracts();
            c.PanelView = type("Kingmaker.Code.UI.MVVM.View.Party.PC.PartyPCView");
            c.CharacterView = type("Kingmaker.Code.UI.MVVM.View.Party.PC.PartyCharacterPCView");
            c.UnitModel = type("Kingmaker.Code.UI.MVVM.VM.Party.PartyCharacterVM");
            var panel = type("Kingmaker.Code.UI.MVVM.VM.Party.PartyVM");
            c.PanelModel = Getter(TouchRadialContracts.Property(c.PanelView, "ViewModel"));
            c.Models = Field(panel, "CharactersVM"); c.Views = Field(c.PanelView, "m_Characters");
            c.ViewModel = Getter(TouchRadialContracts.Property(c.CharacterView, "ViewModel"));
            c.Unit = Getter(TouchRadialContracts.Property(c.UnitModel, "UnitEntityData"));
            c.Enabled = Flag(c.UnitModel, "IsEnable"); c.Selected = Flag(c.UnitModel, "IsSelected");
            c.LevelUp = Flag(c.UnitModel, "IsLevelUp"); c.InCombat = Flag(c.UnitModel, "IsInCombat");
            c.ServiceWindowAvailable = Flag(c.UnitModel, "IsServiceWindowAvailable");
            c.LevelUpButton = TouchRadialContracts.Field(c.CharacterView, "m_LevelUpButton");
            c.PortraitModel = Field(c.UnitModel, "PortraitPartVM");
            var portraitModel = type("Kingmaker.Code.UI.MVVM.VM.Party.UnitPortraitPartVM");
            c.Dead = Flag(portraitModel, "IsDead"); c.Crippled = Flag(portraitModel, "IsCrippled");
            c.Portrait = Reactive<Sprite>(portraitModel, "Portrait");
            c.Name = Reactive<string>(c.UnitModel, "CharacterName");
            c.Health = Field(c.UnitModel, "HealthPartVM");
            c.HpText = Reactive<string>(type("Kingmaker.Code.UI.MVVM.VM.Party.UnitHealthPartVM"), "HpText");
            c.PortraitView = TouchRadialContracts.Field(c.CharacterView, "m_PortraitView");
            c.LifeImage = TouchRadialContracts.Field(c.PortraitView.FieldType, "m_LifePortrait");
            c.DeadImage = TouchRadialContracts.Field(c.PortraitView.FieldType, "m_DeadPortrait");
            c.Navigation = TouchRadialCharacterContracts.CreateNavigation(resolve);
            return c;
        }
        static Func<object, object> Getter(PropertyInfo p) => (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), p.GetGetMethod(true));
        static Func<object, object> Field(Type type, string name) => TouchSelectionCallFactory.FieldGetter(TouchRadialContracts.Field(type, name));
        internal static Func<object, T> Reactive<T>(Type type, string name)
        {
            var field = TouchRadialContracts.Field(type, name); var receiver = TouchSelectionCallFactory.FieldGetter(field);
            var get = (Func<object, T>)TouchSelectionCallFactory.Build(typeof(Func<object, T>), TouchRadialContracts.Property(field.FieldType, "Value").GetGetMethod(true));
            return model => { var value = model == null ? null : receiver(model); return value == null ? default(T) : get(value); };
        }
        static Func<object, bool> Flag(Type type, string name) => Reactive<bool>(type, name);
        // Identical to PartyCharacterPCView.UpdateLevelUp visibility gate.
        internal bool LevelUpVisible(object model) => model != null && LevelUp(model) && !InCombat(model) && ServiceWindowAvailable(model);
        internal void Snapshot(IList models, List<object> savedModels, List<object> savedUnits, List<bool> savedEnabled)
        {
            savedModels.Clear(); savedUnits.Clear(); savedEnabled.Clear();
            if (models == null) return;
            foreach (object model in models)
            {
                savedModels.Add(model); bool valid = model != null && UnitModel.IsInstanceOfType(model);
                savedUnits.Add(valid ? Unit(model) : null); savedEnabled.Add(valid && Enabled(model));
            }
        }
        internal bool SameSlots(IList models, List<object> savedModels, List<object> savedUnits, List<bool> savedEnabled)
        {
            if (models == null || models.Count != savedModels.Count || savedUnits.Count != savedModels.Count || savedEnabled.Count != savedModels.Count) return false;
            for (int i = 0; i < models.Count; ++i)
            {
                object model = models[i]; if (!ReferenceEquals(model, savedModels[i])) return false;
                bool valid = model != null && UnitModel.IsInstanceOfType(model);
                if (!ReferenceEquals(valid ? Unit(model) : null, savedUnits[i]) || (valid && Enabled(model)) != savedEnabled[i]) return false;
            }
            return true;
        }
        internal static bool SameViews(IList views, List<object> snapshot)
        {
            if (views == null || views.Count != snapshot.Count) return false;
            for (int i = 0; i < views.Count; ++i) if (!ReferenceEquals(views[i], snapshot[i])) return false;
            return true;
        }
        internal void Commit(object unit) => Navigation.Commit(unit);
    }
}
