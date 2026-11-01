using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopSwitcher
{
    internal static class Tests
    {
        private static int checks;
        private static void Check(bool value,string label) { if(!value) throw new Exception(label); checks++; Console.WriteLine("PASS "+label); }
        private static void Reject(byte[] ids,byte[] current,string label)
        { bool rejected=false; try { DesktopSnapshot.Parse(ids,current); } catch(InvalidOperationException) { rejected=true; } Check(rejected,label); }
        private static void RejectRegistry(object before,object current,object after,string label)
        { ExpectInvalid(()=>DesktopSnapshot.FromRegistryValues(before,current,after),label); }
        private static void ExpectInvalid(Action action,string label)
        { bool rejected=false; try { action(); } catch(InvalidOperationException) { rejected=true; } Check(rejected,label); }
        private delegate bool WindowCallback(IntPtr hwnd,IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback,IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
        [STAThread] private static int Main(string[] args)
        {
            try {
                Native.InitializeDpi();
                if(args.Length>1&&args[0]=="--exit-child") return LifetimeTests.Child(args[1],args.Length>2&&args[2]=="fault");
                if(args.Length>0&&args[0]=="--lifecycle-probe") { LifetimeTests.Probe(Check); Console.WriteLine("Passed "+checks+" lifecycle process checks."); return 0; }
                if(args.Length>0&&args[0]=="--probe") { Probe(); return 0; }
                if(args.Length>0&&args[0]=="--show-probe") { ShowProbe(); return 0; }
                if(args.Length>0&&(args[0]=="--widget-probe"||args[0]=="--reminder-probe")) { WidgetFeatureTests.Live(Check); Console.WriteLine("Passed "+checks+" live widget checks. No keyboard input sent."); return 0; }
                Guid a=Guid.NewGuid(),b=Guid.NewGuid(),c=Guid.NewGuid();
                byte[] ids=a.ToByteArray().Concat(b.ToByteArray()).Concat(c.ToByteArray()).ToArray();
                var first=DesktopSnapshot.Parse(ids,a.ToByteArray());
                var middle=DesktopSnapshot.Parse(ids,b.ToByteArray());
                var last=DesktopSnapshot.Parse(ids,c.ToByteArray());
                Check(first.Action(false)==DesktopAction.None,"left boundary does not create or wrap");
                Check(first.Action(true)==DesktopAction.Right,"right neighbor is selected");
                Check(middle.Action(false)==DesktopAction.Left&&middle.Action(true)==DesktopAction.Right,"middle desktop moves in both directions");
                Check(last.Action(true)==DesktopAction.Create,"only the last desktop creates");
                Check(DesktopSnapshot.Parse(a.ToByteArray(),a.ToByteArray()).Action(true)==DesktopAction.Create,"single desktop creates on right");
                Reject(null,a.ToByteArray(),"missing desktop list rejected");
                Reject(new byte[17],a.ToByteArray(),"partial GUID list rejected");
                Reject(ids,new byte[15],"partial current GUID rejected");
                Reject(ids,Guid.NewGuid().ToByteArray(),"unknown current desktop rejected");
                Reject(a.ToByteArray().Concat(a.ToByteArray()).ToArray(),a.ToByteArray(),"duplicate desktop IDs rejected");
                Reject(new byte[16],new byte[16],"empty GUID rejected");
                Check(first.SameAs(DesktopSnapshot.Parse(ids,a.ToByteArray()))&&!first.SameAs(middle),"snapshot stability compares current desktop");
                Check(!first.SameAs(DesktopSnapshot.Parse(c.ToByteArray().Concat(a.ToByteArray()).ToArray(),a.ToByteArray())),"desktop reordering detected");
                var pristine=DesktopSnapshot.FromRegistryValues(null,null,null);
                Check(pristine.IsUninitialized&&pristine.Count==1&&pristine.Index==0,"missing registry represents the implicit default desktop");
                Check(pristine.Current==Guid.Empty&&pristine.Ids.Length==0,"first-run state does not invent a desktop GUID");
                Check(pristine.Action(false)==DesktopAction.None&&pristine.Action(true)==DesktopAction.Create,"first run: left does nothing and right creates");
                Check(DesktopSnapshot.FromRegistryValues(new byte[0],new byte[0],new byte[0]).SameAs(pristine),"empty initial registry values supported");
                Check(DesktopSnapshot.FromRegistryValues(null,new byte[16],null).SameAs(pristine),"unassigned initial current desktop supported");
                var sole=DesktopSnapshot.FromRegistryValues(a.ToByteArray(),null,a.ToByteArray());
                Check(!sole.IsUninitialized&&sole.Current==a&&sole.Count==1,"sole desktop ID can precede the current desktop value");
                RejectRegistry(null,a.ToByteArray(),null,"partial registry with only a current GUID is not guessed");
                RejectRegistry(ids,null,ids,"multiple desktops with unknown current desktop are rejected");
                RejectRegistry(null,null,a.ToByteArray(),"key initialized during read is retried instead of creating");
                RejectRegistry(a.ToByteArray(),null,null,"list disappearing during read is rejected");
                RejectRegistry("invalid",null,"invalid","wrong list registry type rejected");
                RejectRegistry(null,"invalid",null,"wrong current registry type rejected");
                RejectRegistry(new byte[17],null,new byte[17],"malformed existing list never becomes first run");
                RejectRegistry(null,new byte[15],null,"malformed current value never becomes first run");
                var twoAtFirst=DesktopSnapshot.Parse(a.ToByteArray().Concat(b.ToByteArray()).ToArray(),a.ToByteArray());
                var twoAtLast=DesktopSnapshot.Parse(a.ToByteArray().Concat(b.ToByteArray()).ToArray(),b.ToByteArray());
                Check(!pristine.ConfirmsCreation(twoAtFirst)&&pristine.ConfirmsCreation(twoAtLast),"first creation confirms entry into the second desktop");
                Check(!pristine.ConfirmsCreation(pristine)&&!pristine.ConfirmsCreation(last),"first creation rejects unchanged or unexpected counts");
                Check(twoAtLast.ConfirmsCreation(last)&&!twoAtLast.ConfirmsCreation(middle),"normal creation still requires a new active last desktop");
                DesktopSnapshot simulated=pristine;
                var reader=new DesktopStateReader(()=>simulated);
                Check(reader.Read().IsUninitialized,"reader supports a pristine machine");
                reader.MarkInitializationAttempt();
                ExpectInvalid(()=>reader.Read(),"unconfirmed first creation cannot be retried repeatedly");
                simulated=twoAtLast;
                Check(reader.Read().Count==2,"reader recovers when first creation initializes registry");
                simulated=pristine;
                ExpectInvalid(()=>reader.Read(),"registry loss after initialization never creates a new desktop");
                bool denied=true;
                var deniedReader=new DesktopStateReader(()=>{ if(denied) throw new UnauthorizedAccessException("test"); return pristine; });
                bool accessDenied=false;
                try { deniedReader.Read(); } catch(UnauthorizedAccessException) { accessDenied=true; }
                Check(accessDenied,"registry access failure propagates instead of assuming first run");
                denied=false;
                ExpectInvalid(()=>deniedReader.Read(),"missing data after a read error does not bootstrap");
                Check(Marshal.SizeOf(typeof(Native.INPUT))==(IntPtr.Size==8?40:28),"SendInput ABI matches process architecture");
                Native.INPUT[] inputs=Native.Chord(0x27);
                Check(inputs.Length==6&&inputs.Take(3).All(i=>(i.Data.Keyboard.Flags&2)==0)&&inputs.Skip(3).All(i=>(i.Data.Keyboard.Flags&2)!=0),"shortcut releases all injected keys");
                Check((inputs[2].Data.Keyboard.Flags&1)!=0&&(Native.Chord(0x44)[2].Data.Keyboard.Flags&1)==0,"extended arrow flag does not affect D");
                Check(WidgetWindow.Clamp(-2100,-1920,-112)==-1920&&WidgetWindow.Clamp(50,-1920,-112)==-112,"positions clamp on monitors with negative coordinates");
                CoursewareChecks();
                CoursewareOptionChecks();
                WidgetFeatureTests.Run(Check);
                LifetimeTests.Run(Check);
                string testPath=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Tests).Assembly.Location),"settings-test.xml");
                try {
                    var settings=new Settings { DefaultPosition=false,Monitor="test-monitor",OffsetX=-40.25,OffsetY=71.5 };
                    settings.Save(testPath); Settings loaded=Settings.Load(testPath);
                    Check(!loaded.DefaultPosition&&loaded.Monitor=="test-monitor"&&loaded.OffsetX==-40.25&&loaded.OffsetY==71.5,"drag position survives settings roundtrip");
                    settings.OffsetX=123; settings.Save(testPath);
                    Check(Settings.Load(testPath).OffsetX==123,"settings overwrite is atomic and readable");
                    settings.DisableCoursewareMode=true; settings.Save(testPath);
                    Check(Settings.Load(testPath).DisableCoursewareMode,"courseware opt-out survives restart with saved position");
                    settings.DisableCoursewareMode=false; settings.Save(testPath);
                    Check(!Settings.Load(testPath).DisableCoursewareMode,"automatic courseware entry can be saved again");
                    settings.SuppressCoursewareTips=true; settings.Save(testPath);
                    loaded=Settings.Load(testPath);
                    Check(loaded.SuppressCoursewareTips&&!loaded.DisableCoursewareMode&&loaded.OffsetX==123,
                        "do-not-remind persists independently of courseware mode and position");
                    var restarted=new CoursewareMode(); restarted.SetTipsSuppressed(loaded.SuppressCoursewareTips);
                    restarted.Observe(Show()); restarted.Observe(Show());
                    Check(restarted.Active&&!restarted.BannerVisible,"restart retains paging while suppressing future reminders");
                    System.IO.File.WriteAllText(testPath,"<Settings><DefaultPosition>false</DefaultPosition><Monitor>legacy</Monitor><OffsetX>12</OffsetX><OffsetY>34</OffsetY></Settings>");
                    loaded=Settings.Load(testPath);
                    Check(!loaded.SuppressCoursewareTips&&!loaded.DisableCoursewareMode&&!loaded.DefaultPosition&&loaded.OffsetX==12&&loaded.OffsetY==34,
                        "old settings retain position and enable automatic courseware entry by default");
                    System.IO.File.WriteAllText(testPath,"<invalid>");
                    Check(Settings.Load(testPath).DefaultPosition,"corrupt settings fall back to bottom-right");
                    settings.OffsetX=double.NaN; settings.Save(testPath);
                    Check(Settings.Load(testPath).DefaultPosition,"nonfinite positions are rejected");
                } finally { if(System.IO.File.Exists(testPath)) System.IO.File.Delete(testPath); }
                Console.WriteLine("Passed "+checks+" checks."); return 0;
            } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        private static ShowWindow Show(string process="POWERPNT",string className="screenClass",string caption="",int handle=100)
        {
            return new ShowWindow { Handle=new IntPtr(handle),ProcessId=42,ProcessStarted=123456,
                ProcessName=process,ClassName=className,Caption=caption,Exists=true,Reliable=true,Visible=true,
                Bounds=new Native.RECT { Left=-1920,Top=0,Right=0,Bottom=1080 },
                MonitorBounds=new Native.RECT { Left=-1920,Top=0,Right=0,Bottom=1080 } };
        }
        private static void CoursewareOptionChecks()
        {
            var mode=new CoursewareMode(); var ppt=Show(); var browser=Show("chrome","Chrome_WidgetWin_1");
            mode.Observe(ppt); Check(!mode.BannerVisible,"guide waits for second show observation");
            mode.Observe(ppt); int token=mode.Generation;
            Check(mode.Active&&mode.BannerVisible,"confirmed show exposes a persistent guide");
            for(int i=0;i<1000;i++) mode.Observe(ppt);
            Check(mode.BannerVisible&&mode.CanSend(ppt,ppt,token),"polling never expires guide or changes page route");
            mode.DismissBanner();
            Check(!mode.BannerVisible&&mode.Active&&mode.CanSend(ppt,ppt,token),"outside click dismisses only this guide");
            mode.Observe(ppt); mode.Observe(ppt);
            Check(!mode.BannerVisible,"dismissed guide does not reopen during same show entry");
            mode.Observe(browser); mode.Observe(ppt); mode.Observe(ppt);
            Check(mode.BannerVisible,"returning to show can display a new guide");
            token=mode.Generation; mode.SetTipsSuppressed(true);
            Check(!mode.BannerVisible&&mode.Active&&mode.CanSend(ppt,ppt,token),"do-not-remind preserves paging");
            mode.Observe(browser); mode.Observe(ppt); mode.Observe(ppt);
            Check(mode.Active&&!mode.BannerVisible,"permanent reminder selection survives show changes");
            mode.SetDisabled(true);
            Check(!mode.Active&&!mode.CanSend(ppt,ppt,token)&&mode.DesktopAllowed(ppt,mode.Generation),"desktop mode invalidates pending page operation");
            mode.SetDisabled(false); token=mode.Generation; mode.Observe(ppt);
            Check(!mode.Active&&!mode.DesktopAllowed(ppt,token),"courseware mode still waits for two observations");
            mode.Observe(ppt); Check(mode.Active&&!mode.BannerVisible,"changing modes does not erase do-not-remind");
        }
        private static void CoursewareChecks()
        {
            var ppt=Show(); var browser=Show("chrome","screenClass");
            Check(ppt.Eligible,"PowerPoint show on a secondary negative-coordinate screen is eligible");
            Check(!browser.Eligible,"browser full screen is excluded even with a matching class");
            Check(!Show("vlc","QWidget","幻灯片放映").Eligible,"video full screen is excluded");
            Check(!Show("POWERPNT","PPTFrameClass").Eligible,"PowerPoint editor is excluded");
            Check(!Show("POWERPNT","PodiumParent").Eligible,"PowerPoint presenter view is excluded");
            var workArea=Show(); workArea.Bounds.Bottom=1040;
            Check(!workArea.Eligible,"ordinary maximized working-area window is excluded");
            var tolerance=Show(); tolerance.Bounds.Left-=2; tolerance.Bounds.Right+=2;
            Check(tolerance.Eligible,"two physical pixels of full-screen boundary tolerance accepted");
            tolerance.Bounds.Right++;
            Check(!tolerance.Eligible,"larger geometry mismatch is rejected");
            var hidden=Show(); hidden.Visible=false;
            Check(!hidden.Eligible,"hidden slide show excluded");
            hidden.Visible=true; hidden.Minimized=true;
            Check(!hidden.Eligible,"minimized slide show excluded");
            hidden.Minimized=false; hidden.Cloaked=true;
            Check(!hidden.Eligible,"cloaked slide show excluded");
            hidden.Cloaked=false; hidden.Reliable=false;
            Check(!hidden.Eligible,"incomplete identity read fails closed");
            var wps=Show("wpp","QWidget","幻灯片放映 - 测试");
            Check(wps.Eligible,"WPS dedicated show caption plus Qt class and borderless geometry accepted");
            Check(Show("wpp","QWidget","幻灯片").Eligible,"legacy WPS exact show caption accepted");
            Check(Show("wpp","Qt5QWindowIcon","WPS Presentation Slide Show - test").Eligible,"English WPS show signature accepted");
            Check(!Show("wpp","QWidget","演讲者视图 - 测试").Eligible,"WPS presenter view excluded");
            Check(!Show("wpp","QWidget","测试 - WPS 演示").Eligible,"WPS editor excluded");
            Check(!Show("wpp","QWidget","文档中的幻灯片放映 - WPS").Eligible,"document caption containing show words is excluded");
            Check(!Show("wpp","QWidget","幻灯片放映 - WPS 演示").Eligible,"editor named like a show is excluded by its editor suffix");
            Check(!Show("wpp","Qt5QWindowIcon","Slide Show - Presenter View").Eligible,"presenter view with a show prefix is excluded");
            wps.Style=0x00C00000;
            Check(!wps.Eligible,"WPS framed window excluded even with show caption");
            wps.Style=0x01000000;
            Check(!wps.Eligible,"WPS ordinary maximized state excluded");
            var mode=new CoursewareMode();
            mode.Observe(ppt);
            Check(!mode.Active&&!mode.DesktopAllowed(ppt,mode.Generation),"first show observation waits and blocks desktop input");
            mode.Observe(ppt); int token=mode.Generation;
            Check(mode.Active&&mode.CanSend(ppt,ppt,token),"second consecutive observation enters and permits page input");
            Check(!mode.CanSend(browser,ppt,token),"foreground change cancels page input before polling");
            mode.Observe(browser);
            Check(!mode.Active&&!mode.CanSend(ppt,ppt,token),"loss of foreground exits and invalidates captured operation");
            mode.Observe(ppt); mode.Observe(ppt);
            Check(mode.Active,"returning to show automatically reenters");
            token=mode.Generation;
            var current=Show(); current.ProcessStarted++;
            Check(!mode.CanSend(current,ppt,token),"process identity change cancels captured page input");
            mode.Observe(current);
            Check(!mode.Active,"changed process identity requires a fresh two-sample confirmation");
            mode.Observe(current);
            Check(mode.Active,"new process identity can enter automatically");
            mode.Observe(new ShowWindow());
            Check(!mode.Active&&!mode.BannerVisible,"closed foreground show immediately exits and hides reminder");
            var debounce=new CoursewareMode();
            debounce.Observe(ppt); debounce.Observe(browser); debounce.Observe(ppt);
            Check(!debounce.Active,"nonconsecutive observations cannot enter");
            token=debounce.Generation; debounce.Observe(ppt);
            Check(!debounce.DesktopAllowed(browser,token),"desktop click captured before entry cannot change its route");
            Native.INPUT[] page=Native.SingleKey(0x27);
            Check(page.Length==2&&page.All(i=>i.Type==1&&i.Data.Keyboard.Vk==0x27)&&
                (page[0].Data.Keyboard.Flags&2)==0&&(page[1].Data.Keyboard.Flags&2)!=0,"page input is one plain arrow down/up without any modifiers");
            var cleanup=Native.SingleKeyCleanup(0x27,1);
            Check(cleanup.Length==1&&cleanup[0].Data.Keyboard.Vk==0x27&&(cleanup[0].Data.Keyboard.Flags&2)!=0,
                "partial arrow failure releases only accepted arrow and never retries down");
            Check(Native.SingleKeyCleanup(0x25,0).Length==0,"blocked input never releases unaccepted keys");
            Check(Native.SingleKeyCleanup(0x27,2).Length==0,"complete arrow pair needs no extra injection");
        }
        private static void ShowProbe()
        {
            var reader=new ShowWindowReader();
            Console.WriteLine("Foreground="+Native.GetForegroundWindow()+" Modifiers="+Native.ModifiersDown());
            EnumWindows((h,p)=>{
                var w=reader.Read(h);
                if(w.ProcessName.Equals("POWERPNT",StringComparison.OrdinalIgnoreCase)||w.ProcessName.Equals("wpp",StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine("HWND="+h+" Process="+w.ProcessName+" PID="+w.ProcessId+" Class="+w.ClassName+" Visible="+w.Visible+" Minimized="+w.Minimized+" Cloaked="+w.Cloaked+" FullScreen="+w.FullScreen+" Signature="+w.HasShowSignature+" Eligible="+w.Eligible+" Rect="+w.Bounds.Left+","+w.Bounds.Top+","+w.Bounds.Right+","+w.Bounds.Bottom);
                return true;
            },IntPtr.Zero);
        }
        private static void Probe()
        {
            var snapshot=DesktopSnapshot.Read();
            Console.WriteLine("Build="+Environment.OSVersion.Version+" Count="+snapshot.Count+" Index="+snapshot.Index+" Current="+snapshot.Current+" FirstRun="+snapshot.IsUninitialized);
            Console.WriteLine("IDs="+string.Join(",",snapshot.Ids.Select(i=>i.ToString())));
            Console.WriteLine("ModifiersDown="+Native.ModifiersDown());
            IntPtr foreground=Native.GetForegroundWindow();
            Console.WriteLine("Foreground="+foreground);
            using(var manager=new DesktopManager()) {
                EnumWindows((hwnd,p)=>{
                    uint pid; GetWindowThreadProcessId(hwnd,out pid);
                    var title=new StringBuilder(512); GetWindowText(hwnd,title,title.Capacity);
                    if(title.ToString().Contains("桌面切屏/课件翻页助手")) {
                        Native.RECT rect; Native.GetWindowRect(hwnd,out rect); int cloaked; DwmGetWindowAttribute(hwnd,14,out cloaked,4);
                        Console.WriteLine("Window="+hwnd+" PID="+pid+" Title="+title+" Visible="+IsWindowVisible(hwnd)+" Cloaked="+cloaked+" Rect="+rect.Left+","+rect.Top+","+rect.Right+","+rect.Bottom+" Style="+Native.GetWindowLong(hwnd,-20).ToString("X")+" Dpi="+Native.DpiScale(hwnd));
                        try { Console.WriteLine("WindowDesktop="+manager.WindowDesktop(hwnd)+" IsCurrent="+manager.IsCurrent(hwnd)); } catch(Exception e) { Console.WriteLine("COM="+e.Message); }
                    }
                    return true;
                },IntPtr.Zero);
            }
        }
    }
}
