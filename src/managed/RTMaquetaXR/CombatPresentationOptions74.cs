using System.Collections.Generic;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static OverlayOption CombatPresentationMenu74()
        {
            string[] names={"Hidden combat presentation","Attack presentation reuse","Event-driven world indicators","Retain radial instruments"};
            var options=new List<OverlayOption>();
            for(int i=0;i<names.Length;i++)
            {
                int bit=1<<i;
                options.Add(ImageToggle(names[i],"Independent interface optimization. Restart the game to apply changes. Game rules, prediction and AI remain unchanged.",
                    ()=> (_cfg.combatPresentationMask74&bit)!=0,
                    value=>{if(value)_cfg.combatPresentationMask74|=bit;else _cfg.combatPresentationMask74&=~bit;MarkSettingsDirty();SaveSettings();}));
            }
            options.Add(new OverlayOption{Label="Applied interface blocks",Description="Independent interface optimization. Restart the game to apply changes. Game rules, prediction and AI remain unchanged.",
                Value=()=> (_presentationMask74&_presentationReady74)+(_presentationMask74!=_cfg.combatPresentationMask74?" · "+ModLocalization.Text("Restart required"):"")});
            return ImageGroup("Combat interface performance","Independent interface optimization. Restart the game to apply changes. Game rules, prediction and AI remain unchanged.",options.ToArray());
        }
    }
    internal static partial class ModLocalization
    {
        static partial void AddRelease74(Dictionary<string,string> s)
        {
            s["Hidden combat presentation"]="Información oculta en combate";
            s["Attack presentation reuse"]="Reutilizar información de ataques";
            s["Event-driven world indicators"]="Actualizar indicadores por cambios";
            s["Retain radial instruments"]="Conservar instrumentos de ruedas";
            s["Applied interface blocks"]="Bloques de interfaz aplicados";
            s["Combat interface performance"]="Rendimiento de interfaz en combate";
            s["Independent interface optimization. Restart the game to apply changes. Game rules, prediction and AI remain unchanged."]="Optimización independiente de interfaz. Reinicia el juego para aplicar los cambios. Conserva las reglas del juego, las predicciones y la IA.";
            s["During your Ground combat turn, hold Y to show native information for the pointed unit or affected attack targets, including allies. The attacker is shown only if also affected. The floating cover badge stays hidden. Release Y to hide the information. The game continues running; this control is inactive during enemy turns."]="Durante tu turno en Combates terrestres, mantén Y para mostrar la información original de la unidad señalada o de los objetivos afectados por el ataque, incluidos los aliados. El atacante sólo aparece si también resulta afectado. La etiqueta flotante de cobertura permanece oculta. Suelta Y para retirar la información. El juego continúa; este control no actúa durante los turnos enemigos.";
        }
    }
}
