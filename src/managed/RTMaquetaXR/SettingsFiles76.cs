using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Text;

namespace RTMaquetaXR
{
    // Also compiled by the installer: migration and preservation have a single
    // implementation and never touch UnityModManager's shared Params.xml.
    public static class SettingsFiles76
    {
        public static string PathFor(string directory,string suffix)
        {
            if(suffix!="settings"&&suffix!="custom"&&
                !(suffix.StartsWith("view",StringComparison.Ordinal)&&int.TryParse(suffix.Substring(4),out int n)&&n>0&&n<=20))
                throw new ArgumentException("Unknown mod settings slot");
            return Path.Combine(directory,"RTMAQUETAXR_"+suffix+".cfg");
        }
        public static bool Valid(string text)
        {
            bool found=false;
            foreach(var line in text.Split('\n'))
            {
                string s=line.Trim();if(s.Length==0||s.StartsWith("#")||s.StartsWith(";"))continue;
                int eq=s.IndexOf('=');if(eq<=0)return false;
                string key=s.Substring(0,eq).Trim(),value=s.Substring(eq+1).Trim();
                if(key=="worldScale"||key=="uiWidth"||key=="uiDistance"||key.StartsWith("touchGesture",StringComparison.Ordinal)&&key.EndsWith("Speed",StringComparison.Ordinal))
                    if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out float f)||float.IsNaN(f)||float.IsInfinity(f)||f<=0)return false;
                found=true;
            }
            return found;
        }
        public static void Migrate(string directory,int slots)
        {
            var names=new List<string>{"settings","custom"};
            for(int i=1;i<=slots;i++)names.Add("view"+i);
            foreach(string suffix in names)
            {
                string path=PathFor(directory,suffix),legacy=Path.Combine(directory,"rtvr_"+suffix+".cfg");
                string text=null;
                if(File.Exists(path))
                {
                    text=File.ReadAllText(path);
                    if(!Valid(text))
                    {
                        // Keep malformed new files for diagnosis; only a valid
                        // legacy snapshot can replace them, never defaults.
                        if(!File.Exists(legacy)||!Valid(File.ReadAllText(legacy)))throw new InvalidDataException(path);
                        Backup(path,".invalid76.bak");text=null;
                    }
                }
                if(text==null&&File.Exists(legacy))
                {
                    text=File.ReadAllText(legacy);if(!Valid(text))throw new InvalidDataException(legacy);
                    Backup(legacy,".before76.bak");
                }
                if(text==null)continue;
                string migrated=GestureDefaults(text);
                if(!File.Exists(path)||File.ReadAllText(path)!=migrated)
                {
                    if(File.Exists(path))Backup(path,".before76.bak");
                    AtomicWrite(path,migrated);
                }
            }
        }
        public static string GestureDefaults(string text)
        {
            var values=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var line in text.Split('\n')){int at=line.IndexOf('=');if(at>0)values[line.Substring(0,at).Trim()]=line.Substring(at+1).Trim();}
            if(values.TryGetValue("gestureRevision76",out string revision)&&int.TryParse(revision,out int r)&&r>=76)return text;
            var change=new StringBuilder("gestureRevision76=76\n");
            foreach(string key in new[]{"touchGestureTurnSpeed","touchMoveSpeed","touchZoomSpeed"})
            {
                float speed=1;
                if(values.TryGetValue(key,out string raw)&&(!float.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out speed)||float.IsNaN(speed)||float.IsInfinity(speed)))
                    throw new InvalidDataException("Invalid gesture speed: "+key);
                change.Append(key).Append('=').Append(Math.Max(.25f,Math.Min(2f,speed*1.1f)).ToString("R",CultureInfo.InvariantCulture)).Append('\n');
            }
            return Merge(text,change.ToString());
        }
        public static string Merge(string previous,string current)
        {
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var line in current.Split('\n')){int eq=line.IndexOf('=');if(eq>0)keys.Add(line.Substring(0,eq).Trim());}
            var result=new StringBuilder(current.TrimEnd('\r','\n')).Append('\n');
            foreach(var line in previous.Split('\n'))
            {
                int eq=line.IndexOf('=');string key=eq>0?line.Substring(0,eq).Trim():null;
                if(key!=null&&!keys.Contains(key))result.Append(line.TrimEnd('\r')).Append('\n');
                else if(key==null&&!string.IsNullOrWhiteSpace(line))result.Append(line.TrimEnd('\r')).Append('\n');
            }
            return result.ToString();
        }
        public static void Save(string path,string current)
        {
            AtomicWrite(path,Merge(File.Exists(path)?File.ReadAllText(path):"",current));
        }
        public static void RestoreExact(string path,string previous) { AtomicWrite(path,previous); }
        static void Backup(string path,string suffix)
        {
            if(!File.Exists(path+suffix))File.Copy(path,path+suffix,false);
        }
        static void AtomicWrite(string path,string text)
        {
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temp,text,new UTF8Encoding(false));
                if(File.ReadAllText(temp)!=text)throw new IOException("Settings verification failed");
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
