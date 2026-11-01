using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DesktopSwitcher
{
    internal static class LifetimeTests
    {
        internal static void Run(Action<bool,string> check)
        {
            bool closed=false,stopped=false; int errors=0;
            ExitSequence.Run(()=>{ closed=true; throw new InvalidOperationException("injected close failure"); },
                ()=>stopped=true,error=>errors++);
            check(closed&&stopped&&errors==1,"close failure cannot skip application shutdown");
            stopped=false;
            ExitSequence.Run(()=>{ throw new Exception("injected"); },()=>stopped=true,error=>{ throw new Exception("reporter failure"); });
            check(stopped,"reporting failure also cannot skip application shutdown");
            int cleanup=0;
            CleanupSteps.Run(new Action[] { ()=>{ cleanup++; throw new Exception("first cleanup failure"); },
                ()=>cleanup++,()=>{ cleanup++; throw new Exception("second cleanup failure"); },()=>cleanup++ },error=>{ throw new Exception("logging failure"); });
            check(cleanup==4,"all cleanup steps run independently even if a step and logging fail");

            string name=@"Local\DesktopSwitcher.QA."+Guid.NewGuid().ToString("N");
            using(var reference=new Mutex(false,name))
            using(var instance=new SingleInstance(name)) {
                check(instance.Acquired,"existing unowned mutex object does not falsely block startup");
                bool contended=true;
                var contender=new Thread(()=>{ using(var second=new SingleInstance(name)) contended=second.Acquired; });
                contender.Start(); check(contender.Join(2000)&&!contended,"another thread cannot acquire a genuinely owned instance mutex");
            }
            using(var reopened=new SingleInstance(name)) check(reopened.Acquired,"disposed instance releases mutex for immediate restart");
            using(var keeper=new Mutex(false,name)) {
                Mutex abandoned=null;
                var owner=new Thread(()=>{ abandoned=new Mutex(false,name); abandoned.WaitOne(); });
                owner.Start(); check(owner.Join(2000),"abandoned-mutex fixture terminates its owner thread");
                try { using(var recovered=new SingleInstance(name)) check(recovered.Acquired,"abandoned instance mutex is acquired instead of silently refusing launch"); }
                finally { if(abandoned!=null) abandoned.Dispose(); }
            }

            foreach(bool immersive in new[] { false,true }) {
                var root=WidgetWindow.CreateContent();
                var surface=(System.Windows.Controls.Border)root.FindName("Capsule");
                WidgetWindow.ApplyAppearance(surface,(System.Windows.Shapes.Path)root.FindName("LeftArrow"),(System.Windows.Shapes.Path)root.FindName("RightArrow"),immersive,false);
                // InputHitTest filters unpresented visuals; attach to our own
                // nonactivating source instead of mistaking an offscreen render for input.
                var host=new Window { Content=root,Width=112,Height=60,WindowStyle=WindowStyle.None,
                    ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=System.Windows.Media.Brushes.Transparent,
                    ShowActivated=false,ShowInTaskbar=true,Left=-10000,Top=-10000 };
                host.SourceInitialized+=(s,e)=>{
                    var handle=new System.Windows.Interop.WindowInteropHelper(host).Handle;
                    Native.SetWindowLong(handle,-20,(Native.GetWindowLong(handle,-20)&~0x00040080)|0x08000000);
                };
                host.Show();
                try {
                var size=WidgetWindow.MeasureContent(root); root.Arrange(new Rect(size)); root.UpdateLayout();
                var rendered=new System.Windows.Media.Imaging.RenderTargetBitmap(112,60,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                rendered.Render(root);
                foreach(string buttonName in new[] { "LeftButton","RightButton" }) {
                    var button=(Button)root.FindName(buttonName);
                    check(button.ActualWidth==43&&button.ActualHeight==42,"full 43 by 42 click area for "+buttonName+" immersive="+immersive);
                    Point origin=button.TranslatePoint(new Point(),root);
                    int hits=0,total=0,nativeHits=0;
                    for(int x=2;x<42;x+=4) for(int y=2;y<41;y+=4) {
                        total++; DependencyObject hit=root.InputHitTest(new Point(origin.X+x,origin.Y+y)) as DependencyObject;
                        while(hit!=null&&!(hit is Button)) hit=System.Windows.Media.VisualTreeHelper.GetParent(hit);
                        if(hit==button) hits++;
                        var pixel=new byte[4];
                        rendered.CopyPixels(new Int32Rect((int)(origin.X+x),(int)(origin.Y+y),1,1),pixel,4,0);
                        if(pixel[3]>0) nativeHits++;
                    }
                    check((immersive ? hits==total : hits>=95)&&hits>=90,
                        "button hit coverage="+hits+"/"+total+" points for "+buttonName+", immersive="+immersive);
                    if(immersive) check(nativeHits==total,"invisible native hit layer also covers all "+nativeHits+" button points including corners, "+buttonName);
                }
                check(size==new Size(112,60),"larger hit areas preserve host footprint, immersive="+immersive);
                } finally { host.Close(); }
            }
        }
        internal static int Child(string mutexName,bool injectFailure)
        {
            using(var instance=new SingleInstance(mutexName)) {
                if(!instance.Acquired) return 2;
                var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
                var widget=new WidgetWindow(new Settings { Mode=RunMode.Courseware,SetupCompleted=true },()=>new ShowWindow(),true);
                ApplicationLifetime.AttachFailureHandler(app,()=>widget);
                app.MainWindow=widget; app.ShutdownMode=ShutdownMode.OnMainWindowClose;
                if(injectFailure) widget.Closed+=(s,e)=>{ throw new InvalidOperationException("injected Closed handler failure"); };
                var exit=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(120) };
                exit.Tick+=(s,e)=>{ exit.Stop(); widget.RequestExit(); };
                widget.Start(); exit.Start(); app.Run();
                if(!widget.IsExiting) return 3;
            }
            return 0;
        }
        internal static void Probe(Action<bool,string> check)
        {
            string executable=typeof(LifetimeTests).Assembly.Location;
            for(int round=0;round<4;round++) {
                string name=@"Local\DesktopSwitcher.QA.Exit."+Guid.NewGuid().ToString("N");
                var start=new ProcessStartInfo(executable,"--exit-child "+name+(round%2==1 ? " fault" : " normal")) {
                    UseShellExecute=false,CreateNoWindow=true };
                using(var child=Process.Start(start)) {
                    bool ended=child.WaitForExit(6000);
                    if(!ended) { child.Kill(); child.WaitForExit(); }
                    Console.WriteLine("Child round="+round+" exited="+ended+" code="+child.ExitCode);
                    check(ended&&child.ExitCode==0,"hidden courseware process exits fully, round="+round+" injected close fault="+(round%2==1));
                }
                using(var reopened=new SingleInstance(name)) check(reopened.Acquired,"process exit releases instance mutex, round="+round);
            }
            using(var file=File.Open(executable,FileMode.Open,FileAccess.Read,FileShare.Read))
                check(file.Length>0,"lifecycle test image remains readable after all children exit");
            Console.WriteLine("No production process or user settings were modified by lifecycle probe.");
        }
    }
}
