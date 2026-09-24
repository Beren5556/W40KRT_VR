using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using RTMaquetaXR;
[assembly:System.Reflection.AssemblyVersion("0.9.81.0")]
[assembly:System.Reflection.AssemblyFileVersion("0.9.81.0")]
[assembly:System.Reflection.AssemblyProduct("W40KRT VR Launcher")]
namespace RogueTraderLauncher
{
    internal sealed class Launcher80 : Form
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd,IntPtr dc,uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
        readonly ComboBox headset=new ComboBox(),connection=new ComboBox(),render=new ComboBox(),quality=new ComboBox();
        readonly Label qualityLabel=new Label();
        readonly System.Collections.Generic.List<double> qualityScales=new System.Collections.Generic.List<double>();
        readonly TextBox game=new TextBox(),manifest=new TextBox(),status=new TextBox();
        readonly CheckBox ofxr=new CheckBox();
        readonly Button launch=new Button();
        readonly string mod=LauncherPolicy80.ModDirectory;
        bool loading=true;
        Icon gameIcon;
        internal Launcher80()
        {
            Text="W40KRT VR · Launcher 0.9.81";StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(900,840);MinimumSize=new Size(820,760);Font=new Font("Segoe UI",14);BackColor=Color.FromArgb(245,247,250);
            var table=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=3,RowCount=13,AutoScroll=true};
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,108));
            Controls.Add(table);
            var title=new Label{Text="Rogue Trader in VR",Font=new Font(Font.FontFamily,26,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,0,0,16)};
            var heading=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,12)};
            title.Margin=new Padding(0,0,0,8);heading.Controls.Add(title);
            heading.Controls.Add(new Label{Text=LauncherPolicy80.SavedSettings(mod)==null?"Default preferences":"Loaded from your saved preferences",AutoSize=true,ForeColor=Color.DimGray,Margin=new Padding(0)});
            table.Controls.Add(heading,0,0);table.SetColumnSpan(heading,3);
            headset.DropDownStyle=connection.DropDownStyle=ComboBoxStyle.DropDownList;headset.Items.AddRange(LauncherPolicy80.Headsets);
            foreach(var combo in new[]{headset,connection,render,quality}) {
                combo.DrawMode=DrawMode.OwnerDrawFixed;combo.ItemHeight=30;combo.DropDownStyle=ComboBoxStyle.DropDownList;
                combo.DrawItem+=(s,e)=>{e.DrawBackground();var box=(ComboBox)s;int index=e.Index>=0?e.Index:box.SelectedIndex;
                    if(index>=0)TextRenderer.DrawText(e.Graphics,box.Items[index].ToString(),box.Font,e.Bounds,e.ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);e.DrawFocusRectangle();};
            }
            AddRow(table,1,"Headset",headset);AddRow(table,2,"Connection",connection);
            AddRow(table,3,"Game folder",game);AddRow(table,4,"Runtime file",manifest);
            var browseGame=new Button{Text="Browse…",Dock=DockStyle.Fill,AutoSize=true};var browseRuntime=new Button{Text="Browse…",Dock=DockStyle.Fill,AutoSize=true};
            table.Controls.Add(browseGame,2,3);table.Controls.Add(browseRuntime,2,4);
            browseGame.Click+=(s,e)=>{using(var picker=new FolderBrowserDialog{Description="Select the Rogue Trader game folder",SelectedPath=game.Text})if(picker.ShowDialog(this)==DialogResult.OK)game.Text=picker.SelectedPath;};
            browseRuntime.Click+=(s,e)=>{using(var picker=new OpenFileDialog{Title="Select the chosen provider's x64 OpenXR manifest",Filter="OpenXR runtime (*.json)|*.json",CheckFileExists=true})if(picker.ShowDialog(this)==DialogResult.OK){manifest.Text=picker.FileName;RefreshStatus();}};
            var optional=new Label{Text="Optional — only if the runtime is not found automatically.",ForeColor=Color.Red,AutoSize=true,MaximumSize=new Size(640,0),Margin=new Padding(0,0,0,10)};
            table.Controls.Add(optional,1,5);table.SetColumnSpan(optional,2);
            render.Items.AddRange(LauncherPolicy80.RenderModes);AddRow(table,6,"Render mode",render);table.SetColumnSpan(render,2);
            qualityLabel.Text="DLSS quality";qualityLabel.AutoSize=true;qualityLabel.Anchor=AnchorStyles.Left;
            table.Controls.Add(qualityLabel,0,7);quality.Dock=DockStyle.Fill;quality.Margin=new Padding(0,5,8,5);table.Controls.Add(quality,1,7);table.SetColumnSpan(quality,2);
            foreach(var scale in new[]{.67,.58,.5,1.0})qualityScales.Add(scale);
            quality.Items.AddRange(new[]{"Quality (67%)","Balanced (58%)","Performance (50%)","Native (100%)"});
            render.SelectedIndexChanged+=(s,e)=>{quality.Visible=qualityLabel.Visible=render.SelectedIndex==4;if(!loading)RefreshStatus();};
            quality.SelectedIndexChanged+=(s,e)=>{if(!loading)RefreshStatus();};
            game.TextChanged+=(s,e)=>UpdateGameIcon();
            ofxr.Text="Request experimental OFXR frame generation";ofxr.AutoSize=true;ofxr.Margin=new Padding(0,12,0,6);table.Controls.Add(ofxr,0,8);table.SetColumnSpan(ofxr,3);
            var note=new Label{Text="Connect your headset and controllers in the selected PC app before launch.\nOFXR is a request; the in-game menu reports whether synthesis is active.\nResolution comes from the runtime. Your layout choices are preserved.",AutoSize=true,MaximumSize=new Size(820,0),Margin=new Padding(0,4,0,14)};
            table.Controls.Add(note,0,9);table.SetColumnSpan(note,3);
            status.Multiline=true;status.ReadOnly=true;status.ScrollBars=ScrollBars.Vertical;status.BackColor=Color.White;status.Dock=DockStyle.Fill;status.MinimumSize=new Size(0,140);table.Controls.Add(status,0,10);table.SetColumnSpan(status,3);table.RowStyles.Add(new RowStyle());
            var footer=new FlowLayoutPanel{FlowDirection=FlowDirection.RightToLeft,Dock=DockStyle.Fill,AutoSize=true,Margin=new Padding(0,12,0,0)};
            launch.BackColor=Color.FromArgb(0,120,215);launch.ForeColor=Color.White;launch.FlatStyle=FlatStyle.Flat;launch.FlatAppearance.BorderSize=0;launch.Text="Launch game";launch.AutoSize=true;launch.Padding=new Padding(16,6,16,6);launch.Click+=(s,e)=>Launch();footer.Controls.Add(launch);
            var check=new Button{Text="Check setup",AutoSize=true,Padding=new Padding(8,6,8,6)};check.Click+=(s,e)=>RefreshStatus();footer.Controls.Add(check);
            table.Controls.Add(footer,0,11);table.SetColumnSpan(footer,3);
            headset.SelectedIndexChanged+=(s,e)=>{connection.Items.Clear();connection.Items.AddRange(LauncherPolicy80.Connections(headset.SelectedIndex));connection.SelectedIndex=0;if(!loading){manifest.Text="";RefreshStatus();}};
            connection.SelectedIndexChanged+=(s,e)=>{if(!loading){manifest.Text="";RefreshStatus();}};
            ofxr.CheckedChanged+=(s,e)=>{if(!loading)RefreshStatus();};
            var cfg=LauncherPolicy80.Parse(LauncherPolicy80.SavedSettings(mod)??"");
            int.TryParse(LauncherPolicy80.Value(cfg,"openXrRuntime"),out int savedRuntime);
            if(!int.TryParse(LauncherPolicy80.Value(cfg,"launcherHeadset80"),out int savedHeadset))savedHeadset=savedRuntime==2?3:0;
            headset.SelectedIndex=Math.Max(0,Math.Min(LauncherPolicy80.Headsets.Length-1,savedHeadset));
            connection.SelectedIndex=headset.SelectedIndex==0&&savedRuntime==1?1:0;
            game.Text=LauncherPolicy80.FindGame(LauncherPolicy80.Value(cfg,"launcherGamePath80"));
            manifest.Text=LauncherPolicy80.Value(cfg,"openXrRuntimeManifest80");
            ofxr.Checked=LauncherPolicy80.Value(cfg,"ofxrEnabled77").Equals("True",StringComparison.OrdinalIgnoreCase);
            render.SelectedIndex=LauncherPolicy80.SavedRender(cfg);
            double savedScale=LauncherPolicy80.SavedScale(cfg);int qualityIndex=qualityScales.IndexOf(savedScale);
            if(qualityIndex<0){qualityScales.Add(savedScale);quality.Items.Add("Custom ("+(savedScale*100).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"%)");qualityIndex=qualityScales.Count-1;}
            quality.SelectedIndex=qualityIndex;
            loading=false;RefreshStatus();
        }
        void UpdateGameIcon()
        {
            try {string exe=Path.Combine(game.Text,"WH40KRT.exe");if(File.Exists(exe)){var next=Icon.ExtractAssociatedIcon(exe);if(next!=null){var previous=gameIcon;gameIcon=next;Icon=next;if(previous!=null)previous.Dispose();}}}
            catch(ArgumentException){}catch(IOException){}catch(System.ComponentModel.Win32Exception){}
        }
        static void AddRow(TableLayoutPanel table,int row,string label,Control control)
        {
            table.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,10,8,10)},0,row);
            control.Dock=DockStyle.Fill;control.Margin=new Padding(0,5,8,5);table.Controls.Add(control,1,row);
            if(row<3)table.SetColumnSpan(control,2);
        }
        void RefreshStatus()
        {
            try {
                int runtime=LauncherPolicy80.Runtime(headset.SelectedIndex,connection.SelectedIndex);
                string found=RuntimeDiscovery80.Resolve(runtime,manifest.Text);
                string text=OpenXrRuntimePolicy.Name(runtime)+(found==null?": not found or invalid x64 library.":": ready.\r\n"+found);
                if(runtime==2)text+="\r\nPimax Play: native OpenXR; Touch or Index-compatible controller bindings. Hardware validation is pending.";
                if(headset.SelectedIndex==2)text+="\r\nPICO 4 Ultra: Virtual Desktop only. Hardware validation is pending.";
                if(!LauncherPolicy80.GameFolder(game.Text))text+="\r\nSelect the game folder.";
                if(!File.Exists(Path.Combine(mod,"RTMaquetaXR.dll")))text+="\r\nThe VR mod is not installed. Run INSTALL first.";
                if(ofxr.Checked)text+="\r\n"+(OfxrCheck80.Problem(mod)??"OFXR module found; actual activation is reported in game.");
                if(LauncherPolicy80.GameRunning())text+="\r\nRogue Trader is running. Close it normally before launching again.";
                if(LauncherPolicy80.GameFolder(game.Text)) {
                    string clientProblem=LauncherPolicy80.SteamAccessProblem(LauncherPolicy80.SteamInstallation(game.Text));
                    if(clientProblem!=null)text+="\r\n"+clientProblem;
                }
                if(render.SelectedIndex>=0)text+="\r\nRender mode: "+render.Text+(render.SelectedIndex==4?" · "+quality.Text:"");
                status.Text=text;
            }catch(Exception e){status.Text=e.Message;}
        }
        void Launch()
        {
            launch.Enabled=false;
            try {
                string accessProblem=LauncherPolicy80.LaunchAccessProblem();if(accessProblem!=null)throw new InvalidOperationException(accessProblem);
                bool disableCadence=false;string settings=LauncherPolicy80.SavedSettings(mod);
                if(ofxr.Checked&&settings!=null&&LauncherPolicy80.Value(LauncherPolicy80.Parse(settings),"engineCadenceEnabled").Equals("True",StringComparison.OrdinalIgnoreCase))
                {
                    if(MessageBox.Show(this,"OFXR requires experimental engine cadence to be off. Disable cadence and launch with OFXR?","OFXR and engine cadence",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                    disableCadence=true;
                }
                int pid=LauncherPolicy80.Launch(mod,game.Text,headset.SelectedIndex,connection.SelectedIndex,manifest.Text,ofxr.Checked,disableCadence,
                    LauncherPolicy80.GameRunning,RuntimeDiscovery80.Resolve,OfxrCheck80.Problem,info=>{using(var process=Process.Start(info)){if(process==null)throw new IOException("Windows did not start the game.");return process.Id;}},render.SelectedIndex,qualityScales[quality.SelectedIndex]);
                status.Text="Game started (process "+pid+").";
                Close(); // Release the launcher executable so it cannot block maintenance.
            }catch(Exception e){status.Text=e.Message;MessageBox.Show(this,e.Message,"Unable to launch",MessageBoxButtons.OK,MessageBoxIcon.Information);}
            finally{launch.Enabled=true;}
        }
        [STAThread] static int Main(string[] args)
        {
            try {
                if(args.Length>=1&&args[0]=="--write-defaults"&&args.Length==2){File.WriteAllText(args[1],LauncherPolicy80.Defaults(),new System.Text.UTF8Encoding(false));return 0;}
                if(args.Length>=2&&args[0]=="--check-runtime") {
                    int value=args[1]=="VDXR"?0:args[1]=="MetaLink"?1:args[1]=="Pimax"?2:-1;
                    if(value<0)return 2;
                    string found=RuntimeDiscovery80.Resolve(value,args.Length>2?args[2]:null);Console.WriteLine(found??"Selected x64 runtime is missing or invalid.");return found==null?3:0;
                }
                if(args.Length>0&&args[0]!="--smoke-ui")return 2;
                using(var mutex=new Mutex(true,@"Local\W40KRT_VR_Launcher80"+(args.Length>0?"_Smoke_"+Process.GetCurrentProcess().Id:""),out bool first)) {
                    if(!first){MessageBox.Show("The VR launcher is already open.","W40KRT VR");return 1;}
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    using(var form=new Launcher80()) {
                        if(args.Length>=1){var timer=new System.Windows.Forms.Timer{Interval=1500};timer.Tick+=(s,e)=>{
                            timer.Stop();
                            if(args.Length==2)using(var bitmap=new Bitmap(form.Width,form.Height)){
                                using(var graphics=Graphics.FromImage(bitmap)){var dc=graphics.GetHdc();try{if(!PrintWindow(form.Handle,dc,2))throw new IOException("Window capture failed.");}finally{graphics.ReleaseHdc(dc);}}
                                bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);
                                File.WriteAllText(args[1]+".txt","Headset="+form.headset.Text+"\nConnection="+form.connection.Text+"\nGameRunning="+LauncherPolicy80.GameRunning()+"\nRender="+form.render.Text+"\nQuality="+form.quality.Text+"\nOFXR="+form.ofxr.Checked+"\nQualityVisible="+form.quality.Visible+"\nFontSize="+form.Font.Size+"\nGameIcon="+(form.Icon!=null)+"\nLauncherIntegrity="+LauncherPolicy80.ProcessIntegrity(Process.GetCurrentProcess().Id));}
                            int previousMode=form.render.SelectedIndex;form.render.SelectedIndex=3;if(form.quality.Visible||form.qualityLabel.Visible)throw new InvalidOperationException("DLAA must hide DLSS quality");form.render.SelectedIndex=4;if(!form.quality.Visible)throw new InvalidOperationException("DLSS quality must be visible");form.render.SelectedIndex=previousMode;form.Close();timer.Dispose();};form.Shown+=(s,e)=>{ShowWindow(form.Handle,4);timer.Start();};}
                        Application.Run(form);
                    }
                }
                return 0;
            }catch(Exception e){if(args.Length==0)MessageBox.Show(e.Message,"W40KRT VR launcher");else Console.Error.WriteLine(e);return 1;}
        }
    }
}
