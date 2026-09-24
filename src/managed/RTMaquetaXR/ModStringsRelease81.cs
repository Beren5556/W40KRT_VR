using System.Collections.Generic;
namespace RTMaquetaXR
{
    internal static partial class ModLocalization
    {
        static partial void AddRelease81(Dictionary<string,string> s)
        {
            var ui=new Dictionary<string,string>{
                {"Game management screens","Pantallas de gestión del juego"},
                {"Mod menus and tutorial","Menús del mod y tutorial"},
                {"Interactive dialogues","Diálogos interactivos"},
                {"Large game tutorials","Tutoriales grandes del juego"},
                {"Small game tutorials and tips","Tutoriales pequeños y consejos del juego"},
                {"Other panels","Otros paneles"},
                {"Position HUD, maps and control hints.","Coloca el HUD, los mapas y las ayudas de controles."},
                {"Mod menu distance","Distancia del menú del mod"},
                {"Move the mod menu and its tutorial nearer or farther. Both share horizontal position, height and distance.","Acerca o aleja el menú del mod y su tutorial. Ambos comparten posición horizontal, altura y distancia."},
                {"Adjust this group independently. Position and distance move the whole panel; content size and aspect change its internal layout.","Ajusta este grupo de forma independiente. Posición y distancia mueven todo el panel; tamaño del contenido y proporción cambian su distribución interior."},
                {"Horizontal position","Posición horizontal"},{"Vertical position","Posición vertical"},
                {"Move this group left or right.","Mueve este grupo a izquierda o derecha."},
                {"Move this group up or down.","Mueve este grupo arriba o abajo."},
                {"Move the panel nearer or farther without changing its font size.","Acerca o aleja el panel sin cambiar el tamaño de sus fuentes."},
                {"Scale this group's complete panel uniformly.","Escala uniformemente el panel completo de este grupo."},
                {"Content size","Tamaño del contenido"},{"Panel aspect ratio","Proporción del panel"},
                {"Adjust native text size inside this group's panel.","Ajusta el tamaño del texto nativo dentro del panel de este grupo."},
                {"Change available layout proportions. Original retains the native proportions.","Cambia las proporciones del espacio disponible. Original conserva las proporciones nativas."}
            };
            foreach(var entry in ui)if(!s.ContainsKey(entry.Key))Add(s,entry.Key,entry.Value);
            Add(s,"Monitor image","Imagen en el monitor");
            Add(s,"Monitor copy comparison","Comparación de copia al monitor");
            Add(s,"Session-only measurement: omit the eye copy and clear the window while retaining the selected world-render policy. Use with traces enabled; turn off after comparing.","Medición sólo para esta sesión: omite la copia del ojo y deja la ventana negra conservando la política de renderizado del mundo. Activa las trazas para medir y desactiva esta opción al terminar.");
            Add(s,"Show VR on the monitor. Off keeps the window black and skips its eye copy and redundant world view. Headset menus remain available; Unity may still present the window.","Muestra VR en el monitor. Desactivado deja la ventana negra y omite la copia del ojo y la vista redundante del mundo. Conserva los menús del visor; Unity puede seguir presentando la ventana.");
            Add(s,"Legacy request","Solicitud antigua");
            Add(s,"Choose Auto, J, K, L or M for both DLSS and DLAA. The NVIDIA runtime may substitute the request. Resolution is unchanged; the status separates requested and identified models.","Elige Auto, J, K, L o M para DLSS y DLAA. El runtime NVIDIA puede sustituir la solicitud. No cambia la resolución; el estado distingue el modelo solicitado del identificado.");
        }
    }
}
