using System;
using System.Diagnostics;
using System.Windows;

namespace DesktopSwitcher
{
    internal static class Program
    {
        [STAThread] private static int Main(string[] args)
        {
            ProcessExitGuard.Enable();
            int exitCode=0; SingleInstance instance=null;
            Application app=null; WidgetWindow window=null;
            try {
                bool quit=false,separateProfile=false;
                for(int i=0;i<args.Length;i++) {
                    if(args[i]=="--exit") quit=true;
                    else if(args[i]=="--data-dir"&&i+1<args.Length) { Settings.UseDirectory(args[++i]); separateProfile=true; }
                    else throw new ArgumentException("不支持的启动参数。");
                }
                string instanceName=SingleInstance.InstanceName(separateProfile);
                if(quit) { exitCode=SingleInstance.RequestExit(instanceName) ? 0 : 2; return exitCode; }
                instance=new SingleInstance(instanceName);
                if(!instance.Acquired) { SingleInstance.WakeExisting(); return 0; }
                Native.InitializeDpi();
                app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
                ApplicationLifetime.AttachFailureHandler(app,()=>window);
                instance.ListenForExit(instanceName,()=>{
                    ProcessExitGuard.Arm();
                    try {
                        app.Dispatcher.BeginInvoke(new Action(()=>{
                            if(window!=null) window.RequestExit(); else app.Shutdown();
                        }));
                    } catch { ProcessExitGuard.Finish(0); }
                });
                Settings.Log("Start PID="+Process.GetCurrentProcess().Id+" TID="+Native.GetCurrentThreadId());
                var settings=Settings.Load();
                if(!settings.SetupCompleted) {
                    var firstUse=new FirstUseDialog();
                    if(firstUse.ShowDialog()!=true) { app.Shutdown(); return 0; }
                    settings.Mode=firstUse.SelectedMode;
                    StartupConsent.Request(settings,null);
                    settings.SetupCompleted=true;
                    try { settings.Save(); }
                    catch(Exception error) { MessageBox.Show("本次可以继续使用，但首次选择未能保存："+error.Message,"桌面切屏/课件翻页助手",MessageBoxButton.OK,MessageBoxImage.Information); }
                }
                window=new WidgetWindow(settings,null,false);
                app.MainWindow=window; app.ShutdownMode=ShutdownMode.OnMainWindowClose;
                window.Start();
                Settings.Log("Ready PID="+Process.GetCurrentProcess().Id+" Mode="+settings.Mode);
                app.Run();
                Settings.Log("Application loop returned PID="+Process.GetCurrentProcess().Id);
            }
            catch(Exception error) {
                exitCode=1; Settings.Log(error.ToString());
                if(window==null||!window.IsExiting) MessageBox.Show("小工具未能启动："+error.Message,"桌面切屏/课件翻页助手",MessageBoxButton.OK,MessageBoxImage.Warning);
            }
            finally {
                ProcessExitGuard.Arm();
                CleanupSteps.Run(new Action[] {
                    ()=>{ if(window!=null) window.CleanupResources(); },
                    ()=>{ if(app!=null&&!app.Dispatcher.HasShutdownStarted) app.Shutdown(); },
                    ()=>{ if(instance!=null) instance.Dispose(); }
                },error=>Settings.Log("Final cleanup: "+error));
                ProcessExitGuard.Finish(exitCode);
            }
            return exitCode;
        }
    }
}
