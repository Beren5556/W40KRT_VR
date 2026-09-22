using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _ofxrStartup77,_ofxrCaptured77,_ofxrCadenceStartup77;
        static int _ofxrState77;
        static Action<bool> _ofxrDiagnostics77;
        static Func<int> _ofxrStatus77;
        // OFF returns before filesystem access, reflection, native loading or hooks.
        static void CaptureOfxrStartup77(string folder)
        {
            if(_ofxrCaptured77)return;
            _ofxrCaptured77=true;_ofxrStartup77=_cfg.ofxrEnabled77;_ofxrCadenceStartup77=_cfg.engineCadenceEnabled;
            if(!_ofxrStartup77)return;
            if(_cfg.engineCadenceEnabled){_ofxrState77=8;return;}
            LoadOptionalOfxr77(folder);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void LoadOptionalOfxr77(string folder)
        {
            try {
                string module=Path.Combine(folder,"Optional","OFXR");
                string adapter=Path.Combine(module,"RTMaquetaXR.Ofxr77.dll");
                if(!File.Exists(adapter)){_ofxrState77=6;return;}
                var type=Assembly.LoadFrom(adapter).GetType("RTMaquetaXR.Ofxr77.Adapter77",true);
                var start=(Func<string,bool,int>)Delegate.CreateDelegate(typeof(Func<string,bool,int>),type.GetMethod("Start"));
                var trace=(Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>),type.GetMethod("SetDiagnostics"));
                var status=(Func<int>)Delegate.CreateDelegate(typeof(Func<int>),type.GetMethod("GetState"));
                _ofxrState77=start(module,AllDiagnosticsEnabled);
                if(_ofxrState77==1){_ofxrDiagnostics77=trace;_ofxrStatus77=status;}
            }catch{_ofxrState77=5;}
        }
        static void ApplyOptionalDiagnostics77(bool enabled)
        {
            try{_ofxrDiagnostics77?.Invoke(enabled);}catch{_ofxrState77=5;}
        }
        static string OfxrStatus77()
        {
            int state=_ofxrState77;
            try{if(_ofxrStatus77!=null)state=_ofxrStatus77();}catch{state=5;}
            string[] names={"Off at startup","Waiting for stereo images","Preparing new image history","Synthesis submitted","Showing real images","Unavailable; showing real images","Optional module missing","Another OFXR layer is active","Conflict with engine cadence"};
            if(state<0||state>=names.Length)state=5;
            string current=ModLocalization.Text(names[state]);
            if(_cfg.ofxrEnabled77!=_ofxrStartup77||_ofxrStartup77&&_cfg.engineCadenceEnabled!=_ofxrCadenceStartup77)current+=" · "+ModLocalization.Text("Restart required");
            return ModLocalization.Text(_cfg.ofxrEnabled77?"On":"Off")+" · "+current;
        }
    }
}
