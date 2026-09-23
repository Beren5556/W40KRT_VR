using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RTMaquetaXR;

namespace RogueTraderLauncher
{
    internal static class LauncherPolicy80
    {
        internal static readonly string[] Headsets={"Meta Quest", "PICO 4", "PICO 4 Ultra", "Pimax Dream Air SE (SLAM)", "Pimax Dream Air (SLAM)"};
        internal static string[] Connections(int headset) => headset>=3 ? new[]{"Pimax Play"} : headset>=1 ? new[]{"Virtual Desktop"} : new[]{"Virtual Desktop","Meta Quest Link / Air Link"};
        internal static int Runtime(int headset,int connection)
        {
            if(headset<0||headset>=Headsets.Length||connection<0||connection>=Connections(headset).Length)throw new ArgumentException("Select a headset and its connection.");
            return headset>=3?2:headset==0&&connection==1?1:0;
        }
        internal static readonly string[] RenderModes={"TAA","SMAA","No antialiasing","DLAA","DLSS","NVIDIA diagnostic (no AA image)"};
        internal static int SavedRender(IDictionary<string,string> cfg)
        {
            string neural=Value(cfg,"neuralMode");
            if(neural=="3")return 4;if(neural=="2")return 3;if(neural=="1")return 5;
            if(neural!="0"&&!cfg.ContainsKey("temporalAA")&&!cfg.ContainsKey("disableAA"))return 4;
            return Value(cfg,"disableAA").Equals("True",StringComparison.OrdinalIgnoreCase)?2:
                Value(cfg,"temporalAA","True").Equals("False",StringComparison.OrdinalIgnoreCase)?1:0;
        }
        internal static double SavedScale(IDictionary<string,string> cfg)
        {
            return double.TryParse(Value(cfg,"neuralScale"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double scale)&&!double.IsNaN(scale)&&scale>=.5&&scale<=1?scale:.67;
        }
        internal static string RenderChanges(int mode,double scale)
        {
            if(mode<0||mode>=RenderModes.Length||double.IsNaN(scale)||scale<.5||scale>1)throw new ArgumentException("Select a valid render mode and DLSS quality.");
            return "neuralMode="+(mode==4?3:mode==3?2:mode==5?1:0)+"\ntemporalAA="+(mode!=1&&mode!=2)+"\ndisableAA="+(mode==2)+"\n"+
                (mode==4?"neuralScale="+scale.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"\n":"");
        }
        internal static string ModDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),@"AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\RTMaquetaXR");
        internal static string Defaults()
        {
            using(var stream=typeof(LauncherPolicy80).Assembly.GetManifestResourceStream("InitialDefaults80.cfg"))
            using(var reader=new StreamReader(stream,Encoding.UTF8))
                return SettingsFiles76.Merge(reader.ReadToEnd(),ApprovedLayout80.Serialize()+"touchFollowRecenter=False\nofxrEnabled77=False\n");
        }
        internal static Dictionary<string,string> Parse(string text)
        {
            var values=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var line in text.Split('\n')) {int at=line.IndexOf('=');if(at>0)values[line.Substring(0,at).Trim()]=line.Substring(at+1).Trim();}
            return values;
        }
        internal static string Value(IDictionary<string,string> cfg,string key,string fallback="") => cfg.TryGetValue(key,out string value)?value:fallback;
        internal static string SavedSettings(string mod)
        {
            string path=SettingsFiles76.PathFor(mod,"settings");
            if(!File.Exists(path))path=Path.Combine(mod,"rtvr_settings.cfg");
            return File.Exists(path)?File.ReadAllText(path):null;
        }
        internal static bool GameRunning() {var processes=Process.GetProcessesByName("WH40KRT");bool found=processes.Length>0;foreach(var process in processes)process.Dispose();return found;}
        internal static bool GameFolder(string path) {try{return File.Exists(Path.Combine(path,"WH40KRT.exe"))&&Directory.Exists(Path.Combine(path,"WH40KRT_Data"));}catch(ArgumentException){return false;}}
        internal static string FindGame(string saved)
        {
            if(GameFolder(saved))return saved;
            var roots=new List<string>();
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam",false)) {string steam=key?.GetValue("SteamPath") as string;if(!string.IsNullOrEmpty(steam))roots.Add(steam);}
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
            foreach(string root in roots.ToArray())
            {
                try {
                    string library=Path.Combine(root,@"steamapps\libraryfolders.vdf");
                    if(File.Exists(library))foreach(Match m in Regex.Matches(File.ReadAllText(library),"\"path\"\\s*\"([^\"]+)\""))roots.Add(m.Groups[1].Value.Replace(@"\\",@"\"));
                } catch(IOException){} catch(UnauthorizedAccessException){}
            }
            foreach(string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string candidate=Path.Combine(root,@"steamapps\common\Warhammer 40,000 Rogue Trader");
                if(GameFolder(candidate))return candidate;
            }
            return "";
        }
        internal static string Changes(int headset,int connection,string manifest,bool ofxr,bool disableCadence,string game)
        {
            if(string.IsNullOrWhiteSpace(manifest)||manifest.IndexOfAny(new[]{'\r','\n','\0'})>=0||game.IndexOfAny(new[]{'\r','\n','\0'})>=0)throw new ArgumentException("Invalid launch path.");
            return "openXrRuntime="+Runtime(headset,connection)+"\nopenXrRuntimeManifest80="+manifest+"\nofxrEnabled77="+ofxr+
                "\nlauncherHeadset80="+headset+"\nlauncherGamePath80="+game+"\n"+(disableCadence?"engineCadenceEnabled=False\n":"");
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
        [System.Runtime.InteropServices.DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
        [System.Runtime.InteropServices.DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr buffer,int length,out int required);
        [System.Runtime.InteropServices.DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [System.Runtime.InteropServices.DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid,uint index);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        internal static string SteamIntegrityProblem(int? integrity) => integrity.HasValue&&integrity.Value<0x2000 ?
            "Steam is running with low Windows integrity and cannot access its normal settings. Exit Steam completely, then open Steam from the Windows Start menu and retry. Do not run the VR launcher as administrator." : null;
        internal static string LauncherIntegrityProblem(int? integrity) => !integrity.HasValue ?
            "Windows could not verify the launcher's permissions. No game was started." : integrity.Value<0x2000 ?
            "The launcher has restricted Windows permissions. Close it and open LAUNCH-VR.cmd from the extracted package, or reinstall the corrected launcher. No game was started." : null;
        internal static string LaunchAccessProblem()
        {
            using(var current=Process.GetCurrentProcess()) {
                string problem=LauncherIntegrityProblem(ProcessIntegrity(current.Id));
                if(problem!=null)return problem;
            }
            return null;
        }
        internal static int? ProcessIntegrity(int pid)
        {
            IntPtr process=OpenProcess(0x1000,false,pid),token=IntPtr.Zero;
            try {
                if(process==IntPtr.Zero||!OpenProcessToken(process,8,out token))return null;
                int size;GetTokenInformation(token,25,IntPtr.Zero,0,out size);if(size<=0||size>8192)return null;
                IntPtr buffer=System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
                try {
                    if(!GetTokenInformation(token,25,buffer,size,out size))return null;
                    IntPtr sid=System.Runtime.InteropServices.Marshal.ReadIntPtr(buffer);
                    byte count=System.Runtime.InteropServices.Marshal.ReadByte(GetSidSubAuthorityCount(sid));if(count==0)return null;
                    return System.Runtime.InteropServices.Marshal.ReadInt32(GetSidSubAuthority(sid,(uint)(count-1)));
                } finally {System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);}
            } finally {if(token!=IntPtr.Zero)CloseHandle(token);if(process!=IntPtr.Zero)CloseHandle(process);}
        }
        internal static string SteamAccessProblem(bool required=false)
        {
            var processes=Process.GetProcessesByName("steam");
            try {
                if(required&&processes.Length==0)return "Open Steam normally and sign in, then retry. This game's Steam edition does not load mods while Steam is closed. The launcher starts WH40KRT.exe directly.";
                foreach(var process in processes){string problem=SteamIntegrityProblem(ProcessIntegrity(process.Id));if(problem!=null)return problem;}return null;
            }
            finally {foreach(var process in processes)process.Dispose();}
        }
        internal static bool SteamInstallation(string game) {
            var parent=Directory.GetParent(game);
            string appManifest=parent?.Parent==null?"":Path.Combine(parent.Parent.FullName,"appmanifest_2186680.acf");
            return File.Exists(appManifest)&&Regex.IsMatch(File.ReadAllText(appManifest),"\"appid\"\\s+\"2186680\"");
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Ansi,ExactSpelling=true)] static extern IntPtr GetProcAddress(IntPtr module,string name);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)] delegate bool SteamInit();
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate void SteamShutdown();
        internal static string SteamConnectionProblem(string game)
        {
            if(!SteamInstallation(game))return null;
            string problem=SteamAccessProblem(true);if(problem!=null)return problem;
            string app=Environment.GetEnvironmentVariable("SteamAppId"),id=Environment.GetEnvironmentVariable("SteamGameId");
            IntPtr library=IntPtr.Zero;SteamShutdown shutdown=null;bool initialized=false;
            try {
                // Use the same installed API as the game, without RestartAppIfNecessary
                // or any client/game launch. This catches the native mod-loader gate.
                string path=Path.Combine(game,@"WH40KRT_Data\Plugins\x86_64\steam_api64.dll");
                library=LoadLibraryEx(path,IntPtr.Zero,8);
                if(library==IntPtr.Zero)return "The game's Steam API could not be checked. No game was started.";
                IntPtr initAddress=GetProcAddress(library,"SteamAPI_Init"),stopAddress=GetProcAddress(library,"SteamAPI_Shutdown");
                if(initAddress==IntPtr.Zero||stopAddress==IntPtr.Zero)return "The game's Steam API is incomplete. No game was started.";
                var init=(SteamInit)System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(initAddress,typeof(SteamInit));
                shutdown=(SteamShutdown)System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(stopAddress,typeof(SteamShutdown));
                Environment.SetEnvironmentVariable("SteamAppId","2186680");Environment.SetEnvironmentVariable("SteamGameId","2186680");
                initialized=init();
                return initialized?null:"The game cannot connect to Steam, so its mod loader would stay disabled. Open Steam normally, sign in to the account owning Rogue Trader, and retry. No game was started.";
            } finally {
                if(initialized)shutdown();
                Environment.SetEnvironmentVariable("SteamAppId",app);Environment.SetEnvironmentVariable("SteamGameId",id);
                if(library!=IntPtr.Zero)FreeLibrary(library);
            }
        }
        internal static ProcessStartInfo StartInfo(string game,string manifest) {
            var info=new ProcessStartInfo(Path.Combine(game,"WH40KRT.exe"),"-force-d3d11 --rtmaquetaxr-vr"){WorkingDirectory=game,UseShellExecute=false};
            info.EnvironmentVariables["XR_RUNTIME_JSON"]=manifest;
            if(SteamInstallation(game)) {
                // Process-local identity preserves direct startup and its VR arguments.
                // No client launch, registry writes or steam_appid.txt in the game folder.
                info.EnvironmentVariables["SteamAppId"]="2186680";
                info.EnvironmentVariables["SteamGameId"]="2186680";
            }
            return info;
        }
        // All validation precedes writes. Launch is injectable for tests; the UI
        // supplies Process.Start. A failed launch restores the exact old config.
        internal static int Launch(string mod,string game,int headset,int connection,string explicitManifest,bool ofxr,bool disableCadence,
            Func<bool> running,Func<int,string,string> resolve,Func<string,string> ofxrProblem,Func<ProcessStartInfo,int> start,int renderMode=-1,double dlssScale=.67,Func<string> accessProblem=null)
        {
            string access=(accessProblem??LaunchAccessProblem)();
            if(access!=null)throw new InvalidOperationException(access);
            if(running())throw new InvalidOperationException("Rogue Trader is already running. Close it normally before changing launch settings.");
            if(!GameFolder(game))throw new FileNotFoundException("Select the folder containing WH40KRT.exe and WH40KRT_Data.");
            if(!File.Exists(Path.Combine(mod,"RTMaquetaXR.dll")))throw new FileNotFoundException("Install W40KRT VR before using the launcher.");
            if(accessProblem==null){string clientProblem=SteamConnectionProblem(game);if(clientProblem!=null)throw new InvalidOperationException(clientProblem);}
            int runtime=Runtime(headset,connection);string manifest=resolve(runtime,explicitManifest);
            if(manifest==null)throw new FileNotFoundException(OpenXrRuntimePolicy.Name(runtime)+" was not found or its x64 library is invalid. Install its PC application, or browse to its runtime manifest.");
            string path=SettingsFiles76.PathFor(mod,"settings"),before=File.Exists(path)?File.ReadAllText(path):null;
            string saved=before??SavedSettings(mod);
            if(saved!=null&&!SettingsFiles76.Valid(saved))throw new InvalidDataException("The settings file is malformed. Restore a valid backup before launching.");
            string source=saved??Defaults();var cfg=Parse(source);
            if(ofxr) {
                string problem=ofxrProblem(mod);if(problem!=null)throw new InvalidOperationException(problem);
                if(Value(cfg,"engineCadenceEnabled").Equals("True",StringComparison.OrdinalIgnoreCase)&&!disableCadence)throw new InvalidOperationException("Disable experimental engine cadence before enabling OFXR.");
            }
            if(running())throw new InvalidOperationException("Rogue Trader started while checking settings. Close it normally and try again.");
            string changes=Changes(headset,connection,manifest,ofxr,disableCadence,game)+(renderMode<0?"":RenderChanges(renderMode,dlssScale));
            SettingsFiles76.Save(path,SettingsFiles76.Merge(source,changes));
            try{return start(StartInfo(game,manifest));}
            catch{if(before==null)File.Delete(path);else SettingsFiles76.RestoreExact(path,before);throw;}
        }
    }
}
