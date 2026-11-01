using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace DesktopSwitcher
{
    internal enum DesktopAction { None, Left, Right, Create }
    internal sealed class DesktopSnapshot
    {
        internal readonly Guid[] Ids;
        internal readonly Guid Current;
        internal readonly bool IsUninitialized;
        internal int Count { get { return IsUninitialized ? 1 : Ids.Length; } }
        internal int Index { get { return IsUninitialized ? 0 : Array.IndexOf(Ids, Current); } }
        internal DesktopSnapshot(Guid[] ids, Guid current) { Ids=ids; Current=current; }
        private DesktopSnapshot() { Ids=new Guid[0]; Current=Guid.Empty; IsUninitialized=true; }
        internal static DesktopSnapshot Parse(byte[] ids, byte[] current)
        {
            if (ids==null || ids.Length==0 || ids.Length%16!=0 || current==null || current.Length!=16)
                throw new InvalidOperationException("无法识别 Windows 的虚拟桌面信息，已停止自动新建。");
            var guids = new Guid[ids.Length/16];
            for (int i=0; i<guids.Length; i++)
            {
                byte[] bytes=new byte[16]; Array.Copy(ids,i*16,bytes,0,16); guids[i]=new Guid(bytes);
                if (guids[i]==Guid.Empty || Array.IndexOf(guids,guids[i],0,i)>=0)
                    throw new InvalidOperationException("虚拟桌面列表无效，请稍后重试。");
            }
            var snapshot=new DesktopSnapshot(guids,new Guid(current));
            if (snapshot.Index<0) throw new InvalidOperationException("虚拟桌面正在更新，请稍后重试。");
            return snapshot;
        }
        internal bool SameAs(DesktopSnapshot other)
        {
            if (other==null || IsUninitialized!=other.IsUninitialized || Current!=other.Current || Ids.Length!=other.Ids.Length) return false;
            for (int i=0;i<Ids.Length;i++) if (Ids[i]!=other.Ids[i]) return false;
            return true;
        }
        internal DesktopAction Action(bool right)
        { return right ? (Index==Count-1 ? DesktopAction.Create : DesktopAction.Right) : (Index==0 ? DesktopAction.None : DesktopAction.Left); }
        internal bool ConfirmsCreation(DesktopSnapshot after)
        {
            if(after==null || after.IsUninitialized || after.Count!=Count+1 || after.Index!=after.Count-1 || Array.IndexOf(Ids,after.Current)>=0) return false;
            foreach(Guid id in Ids) if(Array.IndexOf(after.Ids,id)<0) return false;
            return true;
        }
        internal static DesktopSnapshot FromRegistryValues(object beforeValue,object currentValue,object afterValue)
        {
            // A missing value is different from access denied, wrong types or corrupt data.
            byte[] before=BinaryValue(beforeValue), current=BinaryValue(currentValue), after=BinaryValue(afterValue);
            if(!BytesEqual(before,after)) throw new InvalidOperationException("虚拟桌面列表正在更新，请稍后重试。");
            bool noList=after==null || after.Length==0;
            bool noCurrent=current==null || current.Length==0 || (current.Length==16 && new Guid(current)==Guid.Empty);
            // Windows may not persist desktop IDs until the first extra desktop is created.
            // Represent the implicit default desktop without inventing a real GUID.
            if(noList && noCurrent) return new DesktopSnapshot();
            // Some Windows builds write the sole desktop's ID before writing the current ID.
            if(after!=null && after.Length==16 && noCurrent) current=after;
            return Parse(after,current);
        }
        private static byte[] BinaryValue(object value)
        {
            if(value==null) return null;
            byte[] bytes=value as byte[];
            if(bytes==null) throw new InvalidOperationException("虚拟桌面信息格式无效，已停止自动新建。");
            return bytes;
        }
        internal static DesktopSnapshot Read()
        {
            const string path=@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
            object before,current,after;
            using (RegistryKey key=Registry.CurrentUser.OpenSubKey(path))
            {
                before=key==null ? null : key.GetValue("VirtualDesktopIDs");
                current=key==null ? null : key.GetValue("CurrentVirtualDesktop");
            }
            // Also read the session key if the root key itself has never been created.
            byte[] rootCurrent=current as byte[];
            if (Environment.OSVersion.Version.Build<22000 || current==null ||
                (rootCurrent!=null && (rootCurrent.Length==0 || (rootCurrent.Length==16 && new Guid(rootCurrent)==Guid.Empty))))
                using (RegistryKey session=Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\"+
                    Process.GetCurrentProcess().SessionId+@"\VirtualDesktops"))
                { if (session!=null) current=session.GetValue("CurrentVirtualDesktop") ?? current; }
            // Reopen to detect a registry key created while the first snapshot was read.
            using (RegistryKey key=Registry.CurrentUser.OpenSubKey(path))
                after=key==null ? null : key.GetValue("VirtualDesktopIDs");
            return FromRegistryValues(before,current,after);
        }
        private static bool BytesEqual(byte[] a,byte[] b)
        { if (a==null || b==null) return a==b; if(a.Length!=b.Length) return false; for(int i=0;i<a.Length;i++) if(a[i]!=b[i]) return false; return true; }
    }
    internal sealed class DesktopStateReader
    {
        private readonly Func<DesktopSnapshot> read;
        private bool initialized, initializationAttempted;
        internal DesktopStateReader() : this(DesktopSnapshot.Read) { }
        internal DesktopStateReader(Func<DesktopSnapshot> read) { this.read=read; }
        internal DesktopSnapshot Read()
        {
            DesktopSnapshot snapshot;
            try { snapshot=read(); }
            catch { initialized=true; throw; } // A failed read must never become a pristine first-run fallback.
            if(!snapshot.IsUninitialized) initialized=true;
            else if(initialized) throw new InvalidOperationException("Windows 的桌面信息暂时不可用，请稍后重试；不会重复新建。");
            else if(initializationAttempted) throw new InvalidOperationException("已发送首次创建快捷键，但 Windows 尚未提供结果。请在任务视图核对；本次不会重复新建。");
            return snapshot;
        }
        internal void MarkInitializationAttempt() { initializationAttempted=true; }
    }
}
