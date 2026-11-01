using System;
using System.Threading;
using System.Windows;
using System.Security.Cryptography;
using System.Text;

namespace DesktopSwitcher
{
    internal sealed class SingleInstance : IDisposable
    {
        private readonly Mutex mutex;
        private bool acquired,disposed;
        private EventWaitHandle exitSignal;
        private readonly ManualResetEvent stopListener=new ManualResetEvent(false);
        internal static readonly int WakeMessage=Native.RegisterWindowMessage("DesktopSwitcher.ShowRunningStatus.v1");
        internal bool Acquired { get { return acquired; } }
        internal SingleInstance(string name)
        {
            mutex=new Mutex(false,name);
            try { acquired=mutex.WaitOne(0); }
            catch(AbandonedMutexException) { acquired=true; }
        }
        internal static void WakeExisting()
        { if(WakeMessage!=0) Native.PostMessage(new IntPtr(0xffff),WakeMessage,IntPtr.Zero,IntPtr.Zero); }
        internal static string InstanceName(bool separateProfile)
        {
            const string standard=@"Local\DesktopSwitcher.xiaotan.v1";
            if(!separateProfile) return standard;
            using(var hash=SHA256.Create()) return standard+"."+BitConverter.ToString(hash.ComputeHash(
                Encoding.UTF8.GetBytes(Settings.DirectoryPath.ToUpperInvariant()))).Replace("-","").Substring(0,24);
        }
        internal void ListenForExit(string name,Action request)
        {
            if(!acquired) return;
            exitSignal=new EventWaitHandle(false,EventResetMode.AutoReset,name+".Exit"); exitSignal.Reset();
            var worker=new Thread(()=>{
                if(WaitHandle.WaitAny(new WaitHandle[] { stopListener,exitSignal })==1) request();
            }) { IsBackground=true,Name="DesktopSwitcher exit listener" };
            worker.Start();
        }
        internal static bool RequestExit(string name)
        {
            try { using(var signal=EventWaitHandle.OpenExisting(name+".Exit")) return signal.Set(); }
            catch(WaitHandleCannotBeOpenedException) { return false; }
        }
        public void Dispose()
        {
            if(disposed) return;
            disposed=true;
            stopListener.Set();
            try { if(acquired) { acquired=false; mutex.ReleaseMutex(); } }
            finally { mutex.Dispose(); }
        }
    }
    internal static class CleanupSteps
    {
        internal static void Run(Action[] steps,Action<Exception> report)
        {
            foreach(var step in steps) {
                try { step(); }
                catch(Exception error) { try { report(error); } catch { } }
            }
        }
    }
    internal static class ExitSequence
    {
        internal static void Run(Action close,Action shutdown,Action<Exception> report)
        {
            try { close(); }
            catch(Exception error) { try { report(error); } catch { } }
            finally { shutdown(); }
        }
    }
    internal static class ApplicationLifetime
    {
        internal static void AttachFailureHandler(Application application,Func<WidgetWindow> getWindow)
        {
            application.DispatcherUnhandledException+=(s,e)=>{
                ProcessExitGuard.Arm();
                Settings.Log(e.Exception.ToString()); e.Handled=true;
                ExitSequence.Run(()=>{
                    var window=getWindow();
                    if(window!=null) { window.CleanupResources(); window.Close(); }
                },()=>application.Shutdown(1),error=>Settings.Log("Fatal exit: "+error));
            };
        }
    }
    // Enabled only by the real executable entry point. A close request first
    // cleans up normally, then ends this process without depending on lingering
    // CLR/IME/native threads. A separate background deadline covers a blocked UI.
    internal static class ProcessExitGuard
    {
        private static bool enabled;
        private static int armed;
        internal static void Enable() { enabled=true; }
        internal static void Arm()
        {
            if(!enabled||Interlocked.Exchange(ref armed,1)!=0) return;
            var guard=new Thread(()=>{
                Thread.Sleep(2500);
                Native.TerminateProcess(Native.GetCurrentProcess(),0);
            }) { IsBackground=true,Name="DesktopSwitcher exit deadline" };
            guard.Start();
        }
        internal static void Finish(int exitCode)
        {
            if(enabled) Native.TerminateProcess(Native.GetCurrentProcess(),unchecked((uint)exitCode));
        }
    }
}
