namespace RTMaquetaXR
{
    public static partial class Main
    {
        static OverlayOption OfxrOption77() => ImageValue("Experimental OFXR",
            "Optional frame generation for stereo VR, compatible with DLSS and DLAA. Off by default. Requires restarting the game. Uses real images during menus, transitions and failures. Engine cadence must be disabled before enabling OFXR. Status describes processing; visual quality and headset smoothness require a live comparison.",
            OfxrStatus77,direction=>{_cfg.ofxrEnabled77=!_cfg.ofxrEnabled77;MarkSettingsDirty();});
    }
}
