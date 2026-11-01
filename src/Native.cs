using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopSwitcher
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint Type; public INPUTUNION Data; }
        // MOUSEINPUT must be present: INPUT is 40 bytes on x64, 28 on x86.
        [StructLayout(LayoutKind.Explicit)] internal struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT Keyboard;
            [FieldOffset(0)] public MOUSEINPUT Mouse;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT
        { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr ExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT
        { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }

        [DllImport("user32.dll", SetLastError=true)] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd,StringBuilder text,int count);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor,ref MONITORINFO info);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
        [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint="GetWindowLongW")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint="SetWindowLongW")] internal static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(POINT point, uint flags);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll",EntryPoint="RegisterWindowMessageW",CharSet=CharSet.Unicode)] internal static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll",EntryPoint="PostMessageW")] internal static extern bool PostMessage(IntPtr hwnd,int message,IntPtr w,IntPtr l);
        [DllImport("kernel32.dll")] internal static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool TerminateProcess(IntPtr process,uint exitCode);

        internal static void InitializeDpi()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
        }
        internal static double DpiScale(IntPtr hwnd)
        {
            try { uint dpi = GetDpiForWindow(hwnd); return dpi > 0 ? dpi / 96.0 : 1.0; }
            catch (EntryPointNotFoundException) { return 1.0; }
        }
        internal static double MonitorScale(System.Windows.Forms.Screen screen)
        {
            uint x, y;
            var point = new POINT { X=screen.Bounds.Left+1, Y=screen.Bounds.Top+1 };
            try { if (GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out x, out y)==0) return x/96.0; }
            catch (DllNotFoundException) { }
            return 1.0;
        }
        internal static bool ModifiersDown()
        {
            foreach (int key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
                if ((GetAsyncKeyState(key) & 0x8000)!=0) return true;
            return false;
        }
        internal static INPUT Key(ushort key, bool up)
        {
            bool extended = key==0x25 || key==0x27 || key==0x5B;
            return new INPUT { Type=1, Data=new INPUTUNION {
                Keyboard=new KEYBDINPUT { Vk=key, Flags=(up ? 2u : 0u) | (extended ? 1u : 0u) }
            }};
        }
        internal static INPUT[] Chord(ushort key)
        {
            return new[] { Key(0xA2,false), Key(0x5B,false), Key(key,false),
                Key(key,true), Key(0x5B,true), Key(0xA2,true) };
        }
        internal static INPUT[] SingleKey(ushort key) { return new[] { Key(key,false),Key(key,true) }; }
        internal static bool SendSingleKey(ushort key,Func<bool> stillValid)
        {
            if(ModifiersDown()) throw new InvalidOperationException("请先松开 Ctrl、Win、Alt 和 Shift 键，再点击箭头。");
            INPUT[] inputs=SingleKey(key);
            if(!stillValid()) return false;
            if(ModifiersDown()) throw new InvalidOperationException("请先松开 Ctrl、Win、Alt 和 Shift 键，再点击箭头。");
            uint sent=SendInput(2,inputs,Marshal.SizeOf(typeof(INPUT)));
            if(sent==2) return true;
            INPUT[] cleanup=SingleKeyCleanup(key,sent);
            if(cleanup.Length!=0) SendInput((uint)cleanup.Length,cleanup,Marshal.SizeOf(typeof(INPUT)));
            throw new InvalidOperationException("Windows 未接受完整翻页按键，本次不会自动重试。课件以管理员身份运行时，工具可能需要同等权限。");
        }
        internal static INPUT[] SingleKeyCleanup(ushort key,uint sent)
        { return sent==1 ? new[] { Key(key,true) } : new INPUT[0]; }
        internal static void SendChord(ushort key)
        { SendChord(key,()=>true); }
        internal static bool SendChord(ushort key,Func<bool> stillValid)
        {
            if (ModifiersDown()) throw new InvalidOperationException("请先松开 Ctrl、Win、Alt 和 Shift 键，再点击箭头。");
            INPUT[] inputs = Chord(key);
            if(!stillValid()) return false;
            if(ModifiersDown()) throw new InvalidOperationException("请先松开 Ctrl、Win、Alt 和 Shift 键，再点击箭头。");
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent != inputs.Length)
            {
                // Release only keys whose down event was actually accepted.
                var release = new System.Collections.Generic.List<INPUT>();
                if (sent>=3) release.Add(Key(key,true));
                if (sent>=2) release.Add(Key(0x5B,true));
                if (sent>=1) release.Add(Key(0xA2,true));
                if (release.Count>0) SendInput((uint)release.Count, release.ToArray(), Marshal.SizeOf(typeof(INPUT)));
                throw new InvalidOperationException("Windows 未接受完整快捷键。请退出远程桌面或系统弹窗后重试；当前应用以管理员身份运行时，可能需要同等权限。");
            }
            return true;
        }
    }

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid desktop);
    }
    internal sealed class DesktopManager : IDisposable
    {
        private IVirtualDesktopManager manager;
        private IVirtualDesktopManager Manager
        {
            get
            {
                if (manager==null) manager=(IVirtualDesktopManager)Activator.CreateInstance(
                    Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")));
                return manager;
            }
        }
        internal Guid WindowDesktop(IntPtr hwnd)
        {
            Guid id; int result=Manager.GetWindowDesktopId(hwnd,out id);
            // Shell-excluded overlay windows have no desktop assignment on newer builds.
            if(result==unchecked((int)0x8002802B)) return Guid.Empty;
            Marshal.ThrowExceptionForHR(result); return id;
        }
        internal bool IsCurrent(IntPtr hwnd)
        { bool current; Marshal.ThrowExceptionForHR(Manager.IsWindowOnCurrentVirtualDesktop(hwnd,out current)); return current; }
        internal void Move(IntPtr hwnd, Guid id)
        { Marshal.ThrowExceptionForHR(Manager.MoveWindowToDesktop(hwnd, ref id)); }
        public void Dispose()
        { var previous=manager; manager=null; if(previous!=null) Marshal.ReleaseComObject(previous); }
    }

}
