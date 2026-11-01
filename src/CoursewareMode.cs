using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopSwitcher
{
    internal sealed class ShowWindow
    {
        internal IntPtr Handle;
        internal uint ProcessId;
        internal long ProcessStarted;
        internal string ProcessName="", ClassName="", Caption="";
        internal Native.RECT Bounds, MonitorBounds;
        internal bool Exists, Reliable, Visible, Minimized, Cloaked;
        internal int Style;
        internal string Identity { get { return Handle+":"+ProcessId+":"+ProcessStarted; } }
        internal bool SameIdentity(ShowWindow other)
        { return other!=null&&Identity==other.Identity; }
        internal bool FullScreen
        {
            get {
                return Bounds.Right>Bounds.Left&&Bounds.Bottom>Bounds.Top&&
                    MonitorBounds.Right>MonitorBounds.Left&&MonitorBounds.Bottom>MonitorBounds.Top&&
                    Math.Abs((long)Bounds.Left-MonitorBounds.Left)<=2&&
                    Math.Abs((long)Bounds.Top-MonitorBounds.Top)<=2&&
                    Math.Abs((long)Bounds.Right-MonitorBounds.Right)<=2&&
                    Math.Abs((long)Bounds.Bottom-MonitorBounds.Bottom)<=2;
            }
        }
        // Keep these rules separate. Never accept a generic full-screen application.
        internal bool HasShowSignature
        {
            get {
                if(ProcessName.Equals("POWERPNT",StringComparison.OrdinalIgnoreCase))
                    return ClassName=="screenClass";
                if(!ProcessName.Equals("wpp",StringComparison.OrdinalIgnoreCase)) return false;
                // WPS uses generic Qt classes, so a class alone cannot identify a show.
                // Require its dedicated show caption and a borderless, non-maximized window.
                if(ClassName!="QWidget"&&ClassName!="Qt5QWindowIcon"&&ClassName!="Qt6QWindowIcon") return false;
                if((Style&0x00C00000)!=0||(Style&0x01000000)!=0) return false;
                // An editor document can itself be named "Slide Show". Reject editor suffixes.
                foreach(string suffix in new[] { " - WPS 演示", " - WPS演示", " - WPS Presentation", " - WPS Office" })
                    if(Caption.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)) return false;
                if(Caption.IndexOf("演讲者",StringComparison.OrdinalIgnoreCase)>=0||
                   Caption.IndexOf("演示者",StringComparison.OrdinalIgnoreCase)>=0||
                   Caption.IndexOf("Presenter",StringComparison.OrdinalIgnoreCase)>=0) return false;
                if(Caption=="幻灯片") return true; // Legacy WPS show window; exact match only.
                foreach(string prefix in new[] { "幻灯片放映", "WPS 幻灯片放映", "WPS 演示 幻灯片放映", "WPS Presentation Slide Show", "Slide Show" }) {
                    if(Caption.Equals(prefix,StringComparison.OrdinalIgnoreCase)) return true;
                    if(Caption.StartsWith(prefix+" - ",StringComparison.OrdinalIgnoreCase)||
                       Caption.StartsWith(prefix+"：",StringComparison.OrdinalIgnoreCase)||
                       Caption.StartsWith(prefix+": ",StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
        }
        internal bool ShowSession { get { return Exists&&HasShowSignature&&FullScreen; } }
        internal bool Eligible { get { return Reliable&&ShowSession&&Visible&&!Minimized&&!Cloaked; } }
    }

    internal sealed class ShowWindowReader
    {
        internal ShowWindow Foreground() { return Read(Native.GetForegroundWindow()); }
        internal ShowWindow Read(IntPtr handle)
        {
            var result=new ShowWindow { Handle=handle };
            if(handle==IntPtr.Zero||!Native.IsWindow(handle)) return result;
            result.Exists=true;
            try {
                uint pid; Native.GetWindowThreadProcessId(handle,out pid);
                using(var process=Process.GetProcessById((int)pid)) {
                    result.ProcessId=pid; result.ProcessName=process.ProcessName;
                    result.ProcessStarted=process.StartTime.ToUniversalTime().Ticks;
                }
                var buffer=new StringBuilder(256);
                Native.GetClassName(handle,buffer,buffer.Capacity); result.ClassName=buffer.ToString();
                // Only WPS needs a fixed show caption. Never log document captions.
                if(result.ProcessName.Equals("wpp",StringComparison.OrdinalIgnoreCase)) {
                    buffer.Clear(); Native.GetWindowText(handle,buffer,buffer.Capacity); result.Caption=buffer.ToString();
                }
                result.Style=Native.GetWindowLong(handle,-16);
                result.Visible=Native.IsWindowVisible(handle); result.Minimized=Native.IsIconic(handle);
                int cloaked; result.Cloaked=Native.DwmGetWindowAttribute(handle,14,out cloaked,4)==0&&cloaked!=0;
                Native.RECT bounds;
                if(!Native.GetWindowRect(handle,out bounds)) return result;
                result.Bounds=bounds;
                var info=new Native.MONITORINFO { Size=Marshal.SizeOf(typeof(Native.MONITORINFO)) };
                if(!Native.GetMonitorInfo(Native.MonitorFromWindow(handle,2),ref info)) return result;
                result.MonitorBounds=info.Monitor;
                // Catch windows/processes that changed while their metadata was being read.
                uint finalPid; Native.GetWindowThreadProcessId(handle,out finalPid);
                result.Exists=Native.IsWindow(handle)&&finalPid==pid;
                result.Reliable=result.Exists;
            } catch(Exception) { /* Inaccessible or disappearing windows are not shows. */ }
            return result;
        }
    }

    internal sealed class CoursewareMode
    {
        private bool guideDismissed;
        private ShowWindow candidate;
        internal ShowWindow Target { get; private set; }
        internal int Generation { get; private set; }
        internal bool Active { get { return Target!=null; } }
        internal bool Disabled { get; private set; }
        internal bool TipsSuppressed { get; private set; }
        internal bool BannerVisible { get { return Active&&!TipsSuppressed&&!guideDismissed; } }
        internal void SetTipsSuppressed(bool value) { TipsSuppressed=value; }
        internal void DismissBanner() { guideDismissed=true; }
        internal void SetDisabled(bool value)
        {
            if(Disabled==value) return;
            Disabled=value; candidate=null;
            // Invalidate any click already waiting for modifiers or desktop confirmation.
            SetTarget(null);
        }
        internal void Observe(ShowWindow foreground)
        {
            if(Disabled) { candidate=null; return; }
            bool allowed=foreground!=null&&foreground.Eligible;
            if(Active&&(!allowed||!Target.SameIdentity(foreground))) SetTarget(null);
            if(!allowed) { candidate=null; return; }
            if(Active) return;
            if(candidate!=null&&candidate.SameIdentity(foreground)) { SetTarget(foreground); candidate=null; }
            else candidate=foreground;
        }
        private void SetTarget(ShowWindow value)
        { Target=value; Generation++; guideDismissed=false; }
        internal bool CanSend(ShowWindow foreground,ShowWindow captured,int generation)
        {
            return !Disabled&&Active&&Generation==generation&&Target.SameIdentity(captured)&&
                captured.SameIdentity(foreground)&&foreground.Eligible;
        }
        internal bool DesktopAllowed(ShowWindow foreground,int generation)
        {
            // During the two-sample entry interval cancel, rather than send a desktop chord.
            return !Active&&Generation==generation&&
                (Disabled||foreground==null||!foreground.Eligible);
        }
    }
}
