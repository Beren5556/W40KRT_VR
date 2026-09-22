using System;
using UnityEngine;

namespace RTMaquetaXR
{
    // Resolve hidden CLR accessors by their complete signature. In particular,
    // BaseUnitEntity.View cannot be obtained with a name-only GetProperty.
    internal sealed class PresentationContracts
    {
        internal Func<object> Game;
        internal Func<object, object> RootUi, Common, Tutorial, Dialog, Speaker, FirstSpeaker, UnitView;
        internal Func<object, object> Initiator, ActingUnit, InvolvedUnits, DialogIdentity;
        internal Func<object, object> Turn, CurrentUnit, MechanicView;
        internal Func<object, object> BigTutorialModel, SmallTutorialModel;
        internal Func<object, bool> BigTutorial, SmallTutorial, IngameMenu, InCombat;
        internal Func<object, Vector3> RigTarget;
        internal static PresentationContracts Create(Func<string, Type> find)
        {
            var game = find("Kingmaker.Game");
            var root = find("Kingmaker.Code.UI.MVVM.RootUIContext");
            var common = find("Kingmaker.Code.UI.MVVM.VM.Common.CommonVM");
            var tutorial = find("Kingmaker.UI.MVVM.VM.Tutorial.TutorialVM");
            var dialog = find("Kingmaker.Controllers.Dialog.DialogController");
            var unit = find("Kingmaker.EntitySystem.Entities.BaseUnitEntity");
            var view = find("Kingmaker.View.UnitEntityView");
            var turn = find("Kingmaker.Controllers.TurnBased.TurnController");
            var mechanic = find("Kingmaker.EntitySystem.Entities.MechanicEntity");
            return new PresentationContracts {
                Game = (Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>), TouchSelectionCallFactory.ExactMethod(game, "get_Instance", game, true)),
                RootUi = Getter(game, "get_RootUiContext", root), Common = Getter(root, "get_CommonVM", common),
                Tutorial = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(common, "TutorialVM", tutorial)),
                BigTutorial = Flag(tutorial, "get_IsShowingBigWindow"), SmallTutorial = Flag(tutorial, "get_IsShowingSmallWindow"),
                BigTutorialModel = ReactiveModel(tutorial, "BigWindowVM"), SmallTutorialModel = ReactiveModel(tutorial, "SmallWindowVM"),
                IngameMenu = SafeIngameMenu(find, root),
                Dialog = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(game, "DialogController", dialog)),
                Speaker = Getter(dialog, "get_CurrentSpeaker", unit), FirstSpeaker = Getter(dialog, "get_FirstSpeaker", unit),
                Initiator = Getter(dialog, "get_Initiator", unit), ActingUnit = Getter(dialog, "get_ActingUnit", unit),
                InvolvedUnits = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(dialog, "InvolvedUnits", typeof(System.Collections.Generic.HashSet<>).MakeGenericType(unit))),
                DialogIdentity = Getter(dialog, "get_Dialog", find("Kingmaker.DialogSystem.Blueprints.BlueprintDialog")),
                UnitView = Getter(unit, "get_View", view),
                Turn = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(game, "TurnController", turn)),
                InCombat = Flag(turn, "get_InCombat"), CurrentUnit = Getter(turn, "get_CurrentUnit", mechanic),
                MechanicView = Getter(mechanic, "get_View", find("Kingmaker.View.Mechanics.MechanicEntityView")),
                RigTarget = (Func<object, Vector3>)TouchSelectionCallFactory.Build(typeof(Func<object, Vector3>),
                    TouchSelectionCallFactory.ExactMethod(find("Kingmaker.View.CameraRig"), "get_TargetPosition", typeof(Vector3), false))
            };
        }
        static Func<object, object> Getter(Type type, string name, Type result) =>
            (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), TouchSelectionCallFactory.ExactMethod(type, name, result, false));
        static Func<object, bool> Flag(Type type, string name) =>
            (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>), TouchSelectionCallFactory.ExactMethod(type, name, typeof(bool), false));
        static Func<object, object> ReactiveModel(Type type, string name)
        {
            var field = PcUiPath.Field(type, name);
            var read = TouchSelectionCallFactory.FieldGetter(field);
            var value = PcUiPath.Getter(PcUiPath.Property(field.FieldType, "Value"));
            return instance => { var property = instance == null ? null : read(instance); return property == null ? null : value(property); };
        }
        static Func<object,bool> SafeIngameMenu(Func<string,Type> find,Type root)
        {
            // The native convenience getter dereferences this full chain with
            // no guards. Loading/menu teardown legitimately omits SurfaceVM.
            var surface=find("Kingmaker.Code.UI.MVVM.VM.Surface.SurfaceVM");
            var part=find("Kingmaker.Code.UI.MVVM.VM.Surface.SurfaceStaticPartVM");
            var hud=find("Kingmaker.Code.UI.MVVM.VM.SurfaceCombat.SurfaceHUDVM");
            var menu=find("Kingmaker.Code.UI.MVVM.VM.IngameMenu.IngameMenuVM");
            var getSurface=Getter(root,"get_SurfaceVM",surface);
            var getPart=TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(surface,"StaticPartVM",part));
            var getHud=TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(part,"SurfaceHUDVM",hud));
            var getMenu=TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(hud,"IngameMenuVM",menu));
            var shownField=TouchSelectionCallFactory.ExactField(menu,"IsShown",typeof(bool));
            var readShown=new System.Reflection.Emit.DynamicMethod("RTMaquetaXR_IngameMenuShown",typeof(bool),new[]{typeof(object)},typeof(PresentationContracts).Module,true);
            var il=readShown.GetILGenerator();il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass,menu);il.Emit(System.Reflection.Emit.OpCodes.Ldfld,shownField);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            var isShown=(Func<object,bool>)readShown.CreateDelegate(typeof(Func<object,bool>));
            return instance=> {
                var s=instance==null?null:getSurface(instance);
                var p=s==null?null:getPart(s);
                var h=p==null?null:getHud(p);
                var m=h==null?null:getMenu(h);
                return m!=null && isShown(m);
            };
        }
    }
}
