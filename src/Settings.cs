using System;
using System.IO;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace DesktopSwitcher
{
    public enum RunMode { Desktop, Courseware, Automatic }
    public enum DockSide { None, Left, Right }
    public sealed class Settings
    {
        public bool DefaultPosition=true;
        public bool DisableCoursewareMode=false;
        public bool SuppressCoursewareTips=false;
        public RunMode Mode=RunMode.Desktop;
        public bool TransparentMode=false;
        public bool SuperTopmost=false;
        public bool EdgeAutoHide=true;
        public DockSide Dock=DockSide.None;
        public bool CoursewareEdgeAutoHide=false;
        public DockSide CoursewareDock=DockSide.None;
        public bool AutomaticEdgeAutoHide=false;
        public DockSide AutomaticDock=DockSide.None;
        public bool SetupCompleted=false;
        public bool CoursewareStartupAsked=false;
        internal bool AutoHideEnabled
        { get { return Mode==RunMode.Automatic ? AutomaticEdgeAutoHide : Mode==RunMode.Courseware ? CoursewareEdgeAutoHide : EdgeAutoHide; }
          set { if(Mode==RunMode.Automatic) AutomaticEdgeAutoHide=value; else if(Mode==RunMode.Courseware) CoursewareEdgeAutoHide=value; else EdgeAutoHide=value; } }
        internal DockSide CurrentDock
        { get { return Mode==RunMode.Automatic ? AutomaticDock : Mode==RunMode.Courseware ? CoursewareDock : Dock; }
          set { if(Mode==RunMode.Automatic) AutomaticDock=value; else if(Mode==RunMode.Courseware) CoursewareDock=value; else Dock=value; } }
        public string Monitor="";
        public double OffsetX, OffsetY;
        private static string directoryOverride;
        internal static string DirectoryPath { get { return directoryOverride??Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DesktopSwitcher"); } }
        internal static void UseDirectory(string directory)
        {
            if(!Path.IsPathRooted(directory)) throw new ArgumentException("诊断数据目录必须使用绝对路径。");
            directoryOverride=Path.GetFullPath(directory);
        }
        internal static string FilePath { get { return Path.Combine(DirectoryPath,"settings.xml"); } }
        internal static Settings Load()
        { return Load(FilePath); }
        internal static Settings Load(string path)
        {
            try { using(var reader=File.OpenRead(path)) {
                Settings value=(Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader);
                if (double.IsNaN(value.OffsetX)||double.IsInfinity(value.OffsetX)||double.IsNaN(value.OffsetY)||double.IsInfinity(value.OffsetY)) return new Settings();
                return value;
            }} catch { return new Settings(); }
        }
        internal void Save()
        { Save(FilePath); }
        internal void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+".tmp";
            using(var writer=File.Create(temporary)) new XmlSerializer(typeof(Settings)).Serialize(writer,this);
            if (File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path);
        }
        internal static string ExecutablePath { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }
        internal static bool AutoStart
        {
            get
            { using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                return key!=null && string.Equals(key.GetValue("DesktopSwitcher") as string,"\""+ExecutablePath+"\"",StringComparison.OrdinalIgnoreCase); }
            set
            { using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                if (value) key.SetValue("DesktopSwitcher","\""+ExecutablePath+"\""); else key.DeleteValue("DesktopSwitcher",false);
            }}
        }
        internal static void Log(string message)
        {
            try { Directory.CreateDirectory(DirectoryPath); string path=Path.Combine(DirectoryPath,"diagnostics.log");
                if(File.Exists(path)&&new FileInfo(path).Length>262144) File.WriteAllText(path,"");
                File.AppendAllText(path,DateTime.Now.ToString("s")+" "+message+Environment.NewLine);
            } catch { }
        }
    }
}
