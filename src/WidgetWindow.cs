using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace DesktopSwitcher
{
    internal sealed class WidgetWindow : Window
    {
        private readonly Settings settings;
        private readonly Func<ShowWindow> foregroundReader;
        private readonly DesktopManager desktops=new DesktopManager();
        private readonly DesktopStateReader desktopState=new DesktopStateReader();
        private readonly CoursewareMode courseware=new CoursewareMode();
        private readonly ShowWindowReader showReader=new ShowWindowReader();
        private readonly Docking docking=new Docking();
        private readonly DockMotion motion;
        private readonly MenuDismissal menuDismissal=new MenuDismissal();
        private readonly DispatcherTimer menuTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(30) };
        private ContextMenu openMenu;
        private IntPtr menuForeground;
        private readonly DispatcherTimer timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(150) };
        private readonly Button left,right;
        private readonly Border capsule,edgeTab;
        private readonly Path leftArrow,rightArrow;
        private readonly Forms.NotifyIcon tray;
        private readonly ContextMenu trayMenu;
        private readonly System.Drawing.Icon ownedIcon;
        private readonly bool testing;
        private readonly CancellationTokenSource shutdown=new CancellationTokenSource();
        private bool resourcesCleaned,statusOpen;
        private SpotlightGuide guide;
        private IntPtr hwnd;
        private bool busy,closing,dragging,moved,positioning,started,visualCourseware;
        private Native.POINT dragStart;
        private Native.RECT dragRect;
        private Button dragButton;
        private int dragGeneration;
        private RunMode dragMode;
        private DateTime lastFollowError=DateTime.MinValue,lastPositionCheck=DateTime.MinValue,nextPageClick=DateTime.MinValue;
        private string layout="",lastStatus="";
        private int visualGeneration=-1;
        private static long Now { get { return (long)(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency); } }

        private const string WidgetXaml=@"<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' ClipToBounds='True'>
          <Grid.Resources>
            <Style TargetType='Button'>
              <Setter Property='Focusable' Value='False'/><Setter Property='Cursor' Value='Hand'/>
              <Setter Property='Width' Value='43'/><Setter Property='Height' Value='42'/>
              <Setter Property='Background' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/>
              <Setter Property='ToolTipService.InitialShowDelay' Value='450'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
                <Border x:Name='surface' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' CornerRadius='10'>
                  <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#28FFFFFF'/></Trigger>
                  <Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Background' Value='#487BB7F3'/></Trigger>
                  <Trigger Property='IsEnabled' Value='False'><Setter TargetName='surface' Property='Opacity' Value='0.38'/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            <Style x:Key='ImmersiveButton' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
                <Border Background='Transparent'>
                  <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                </Border>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
          </Grid.Resources>
          <Border x:Name='Capsule' Width='96' Height='44' HorizontalAlignment='Right' VerticalAlignment='Bottom' Margin='8' Background='#A82B3038' BorderBrush='#38FFFFFF' BorderThickness='1' CornerRadius='16' Cursor='Arrow'>
            <Border.Effect><DropShadowEffect BlurRadius='10' ShadowDepth='2' Opacity='0.22'/></Border.Effect>
            <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='43'/><ColumnDefinition Width='8'/><ColumnDefinition Width='43'/></Grid.ColumnDefinitions>
              <Button x:Name='LeftButton' Grid.Column='0' AutomationProperties.Name='左侧桌面' ToolTip='切换到左侧桌面'>
                <Path x:Name='LeftArrow' Data='M 11,1 L 3,9 L 11,17' Stroke='#FFF4F6FA' StrokeThickness='1.8' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Width='14' Height='18'/>
              </Button>
              <Rectangle x:Name='Divider' Grid.Column='1' Width='1' Height='16' Fill='#28FFFFFF'/>
              <Button x:Name='RightButton' Grid.Column='2' AutomationProperties.Name='右侧桌面或新建' ToolTip='切换到右侧桌面；到末尾时新建'>
                <Path x:Name='RightArrow' Data='M 3,1 L 11,9 L 3,17' Stroke='#FFF4F6FA' StrokeThickness='1.8' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Width='14' Height='18'/>
              </Button>
            </Grid>
          </Border>
          <Border x:Name='EdgeTab' Width='6' Height='32' Visibility='Hidden' VerticalAlignment='Center' HorizontalAlignment='Left'
             Background='#C06C91B7' BorderBrush='#80D2E7FF' BorderThickness='1' CornerRadius='0,4,4,0' Cursor='Arrow' ToolTip='移入展开，右键打开菜单'/>
        </Grid>";

        internal static Grid CreateContent() { return (Grid)XamlReader.Parse(WidgetXaml); }
        internal static Size MeasureContent(FrameworkElement root)
        {
            root.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            return new Size(Math.Ceiling(root.DesiredSize.Width),Math.Ceiling(root.DesiredSize.Height));
        }
        internal WidgetWindow() : this(Settings.Load(),null,false) { }
        internal WidgetWindow(Settings options,Func<ShowWindow> foreground,bool test)
        {
            settings=options; foregroundReader=foreground; testing=test;
            courseware.SetDisabled(settings.Mode==RunMode.Desktop);
            courseware.SetTipsSuppressed(settings.SuppressCoursewareTips);
            docking.SetSide(settings.AutoHideEnabled ? settings.CurrentDock : DockSide.None,Now);
            Title="桌面切屏/课件翻页助手";
            WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true; Background=Brushes.Transparent;
            ShowInTaskbar=true; ShowActivated=false; Topmost=true;
            var root=CreateContent(); Content=root;
            var size=MeasureContent(root); Width=size.Width; Height=size.Height;
            left=(Button)root.FindName("LeftButton"); right=(Button)root.FindName("RightButton");
            leftArrow=(Path)root.FindName("LeftArrow"); rightArrow=(Path)root.FindName("RightArrow");
            capsule=(Border)root.FindName("Capsule"); edgeTab=(Border)root.FindName("EdgeTab");
            motion=new DockMotion(capsule,edgeTab);
            left.Click+=async (s,e)=>await Navigate(false); right.Click+=async (s,e)=>await Navigate(true);
            capsule.PreviewMouseLeftButtonDown+=BeginDrag; capsule.MouseMove+=ContinueDrag; capsule.PreviewMouseLeftButtonUp+=EndDrag;
            capsule.LostMouseCapture+=(s,e)=>{ if(dragging) FinishDrag(); };
            edgeTab.MouseEnter+=(s,e)=>ExpandDock();
            edgeTab.MouseLeftButtonDown+=(s,e)=>{ ExpandDock(); e.Handled=true; };
            capsule.MouseEnter+=(s,e)=>UpdateAppearance(); capsule.MouseLeave+=(s,e)=>UpdateAppearance();
            ContextMenu=CreateMenu(false);
            trayMenu=CreateMenu(true);
            menuTimer.Tick+=(s,e)=>ObserveMenu();
            ownedIcon=System.Drawing.Icon.ExtractAssociatedIcon(Settings.ExecutablePath)??(System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
            tray=new Forms.NotifyIcon { Text="桌面切屏/课件翻页助手",Visible=!test,Icon=ownedIcon };
            tray.MouseUp+=(s,e)=>{ if(e.Button==Forms.MouseButtons.Right) Dispatcher.BeginInvoke(new Action(OpenTrayMenu)); };
            tray.DoubleClick+=(s,e)=>Dispatcher.BeginInvoke(new Action(ResetPosition));
            SourceInitialized+=InitializeWindow;
            SizeChanged+=(s,e)=>{ if(started&&!dragging&&!positioning) ApplyPosition(); };
            LocationChanged+=(s,e)=>PositionGuide(); timer.Tick+=(s,e)=>Poll();
            if(!test) {
                SystemEvents.DisplaySettingsChanged+=DisplayChanged; SystemEvents.UserPreferenceChanged+=PreferenceChanged;
            }
            Closed+=(s,e)=>CleanupResources();
            Closing+=(s,e)=>{ if(!testing) ProcessExitGuard.Arm(); };
            UpdateAppearance();
            ApplyDockVisual(false);
        }
        internal RunMode CurrentMode { get { return settings.Mode; } }
        internal bool IsPageMode { get { return settings.Mode!=RunMode.Desktop&&courseware.Active; } }
        internal ContextMenu TrayMenu { get { return trayMenu; } }
        internal bool IsDockCollapsed { get { return docking.Collapsed; } }
        internal SpotlightGuide Guide { get { return guide; } }
        internal bool IsExiting { get { return closing; } }
        internal void Start()
        {
            if(started) return;
            started=true; new WindowInteropHelper(this).EnsureHandle();
            ApplyPosition(); Poll(); if(!testing) timer.Start();
        }
        private ContextMenu CreateMenu(bool fromTray)
        {
            var menu=WidgetMenu.Create();
            menu.Items.Add(WidgetMenu.Item("桌面切屏","M 1,2 H 13 V 10 H 1 Z M 7,10 V 13 M 4,13 H 10",
                ()=>ChangeMode(RunMode.Desktop,true),()=>settings.Mode==RunMode.Desktop));
            menu.Items.Add(WidgetMenu.Item("课件翻页","M 1,2 H 13 V 10 H 1 Z M 7,10 L 4,13 M 7,10 L 10,13 M 6,4 L 9,6 L 6,8 Z",
                ()=>ChangeMode(RunMode.Courseware,true),()=>settings.Mode==RunMode.Courseware));
            menu.Items.Add(WidgetMenu.Item("自动切换","M 1,4 H 12 M 9,1 L 12,4 L 9,7 M 13,10 H 2 M 5,7 L 2,10 L 5,13",
                ()=>ChangeMode(RunMode.Automatic,true),()=>settings.Mode==RunMode.Automatic));
            menu.Items.Add(new Separator());
            var more=WidgetMenu.Item("更多功能","M 1,3 H 13 M 1,7 H 13 M 1,11 H 13 M 4,1 V 5 M 10,5 V 9 M 6,9 V 13");
            more.Items.Add(WidgetMenu.Item("透明沉浸","M 1,7 C 4,1 10,1 13,7 C 10,13 4,13 1,7 Z M 9,7 A 2,2 0 1 1 5,7 A 2,2 0 1 1 9,7",
                ()=>ChangeTransparency(!settings.TransparentMode,true),()=>settings.TransparentMode));
            more.Items.Add(WidgetMenu.Item("超级置顶","M 2,2 H 12 M 3,8 L 7,4 L 11,8 M 7,4 V 13",
                ()=>ChangeSuperTopmost(!settings.SuperTopmost),()=>settings.SuperTopmost));
            more.Items.Add(WidgetMenu.Item("贴边自动隐藏","M 2,1 V 13 M 5,7 H 13 M 8,4 L 5,7 L 8,10",
                ()=>ChangeAutoHide(!settings.AutoHideEnabled),()=>settings.AutoHideEnabled));
            menu.Items.Add(more);
            if(fromTray) {
                menu.Placement=PlacementMode.MousePoint;
                menu.Items.Add(WidgetMenu.Item("恢复右下角位置","M 12,6 A 5,5 0 1 0 10,11 M 12,2 V 6 H 8",ResetPosition));
                menu.Items.Add(WidgetMenu.Item("开机启动","M 7,1 V 7 M 4,3 A 5,5 0 1 0 10,3",ToggleStartup,StartupEnabled));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(WidgetMenu.Item("退出程序","M 7,2 H 2 V 12 H 7 M 6,7 H 13 M 10,4 L 13,7 L 10,10",RequestExit));
            menu.Opened+=(s,e)=>{
                if(closing) { menu.IsOpen=false; return; }
                if(openMenu!=null&&openMenu!=menu) CloseMenu();
                openMenu=menu; WidgetMenu.Refresh(menu); ExpandDock();
                menuForeground=Native.GetForegroundWindow(); menuDismissal.Open(MouseButtons()); menuTimer.Start();
            };
            Mouse.AddPreviewMouseDownOutsideCapturedElementHandler(menu,(s,e)=>{
                Native.POINT cursor;
                if(!Native.GetCursorPos(out cursor)||!WidgetMenu.ContainsPointer(menu,cursor)) CloseMenu();
            });
            menu.Closed+=(s,e)=>{
                if(openMenu==menu) { openMenu=null; menuTimer.Stop(); }
                ExpandDock();
            };
            return menu;
        }
        private static bool StartupEnabled()
        { try { return Settings.AutoStart; } catch { return false; } }
        internal void OpenTrayMenu()
        {
            if(closing) return;
            CloseMenu(); trayMenu.IsOpen=true;
        }
        internal void ChangeMode(RunMode value,bool persist)
        {
            if(value==settings.Mode) return;
            RunMode previous=settings.Mode; settings.Mode=value;
            if(persist&&!SaveSettings()) { settings.Mode=previous; return; }
            if(persist&&!testing&&value==RunMode.Courseware) { StartupConsent.Request(settings,this); SaveSettings(); }
            courseware.Reset(value==RunMode.Desktop);
            visualGeneration=-1; lastStatus="";
            if(guide!=null) guide.Hide();
            docking.SetSide(settings.AutoHideEnabled ? settings.CurrentDock : DockSide.None,Now);
            ApplyDockVisual(false); ApplyPosition(); Poll();
        }
        internal void ChangeTransparency(bool value,bool persist)
        {
            bool previous=settings.TransparentMode; settings.TransparentMode=value;
            if(persist&&!SaveSettings()) { settings.TransparentMode=previous; return; }
            UpdateAppearance();
        }
        private void ChangeSuperTopmost(bool value)
        {
            bool previous=settings.SuperTopmost; settings.SuperTopmost=value;
            if(!SaveSettings()) { settings.SuperTopmost=previous; return; }
            if(value&&IsVisible) Native.RaiseOverlay(hwnd);
        }
        private void ChangeAutoHide(bool value)
        {
            bool previous=settings.AutoHideEnabled; settings.AutoHideEnabled=value;
            if(!SaveSettings()) { settings.AutoHideEnabled=previous; return; }
            docking.SetSide(value ? settings.CurrentDock : DockSide.None,Now);
            ExpandDock();
        }
        private void InitializeWindow(object sender,EventArgs e)
        {
            hwnd=new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(hwnd,-20,(Native.GetWindowLong(hwnd,-20)&~0x00040080)|0x08000000);
            HwndSource.FromHwnd(hwnd).AddHook(WindowHook);
        }
        private IntPtr WindowHook(IntPtr handle,int message,IntPtr w,IntPtr l,ref bool handled)
        {
            if(SingleInstance.WakeMessage!=0&&message==SingleInstance.WakeMessage) {
                handled=true;
                Dispatcher.BeginInvoke(new Action(ShowRunningStatus));
                return IntPtr.Zero;
            }
            if(message==0x21) { handled=true; return new IntPtr(3); }
            if(message==0x02E0||message==0x007E||message==0x001A)
                Dispatcher.BeginInvoke(new Action(()=>{ if(!closing&&!dragging) ApplyPosition(); }));
            return IntPtr.Zero;
        }
        private void DisplayChanged(object sender,EventArgs e)
        { if(!closing) Dispatcher.BeginInvoke(new Action(ApplyPosition)); }
        private void PreferenceChanged(object sender,UserPreferenceChangedEventArgs e)
        { if(!closing) Dispatcher.BeginInvoke(new Action(ApplyPosition)); }
        private ShowWindow ReadForeground()
        { return foregroundReader==null ? showReader.Foreground() : foregroundReader(); }
        private ShowWindow ObserveForeground()
        {
            var foreground=ReadForeground();
            if(foregroundReader==null&&courseware.Active&&(statusOpen||openMenu!=null)) {
                uint pid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out pid);
                if(pid==(uint)Process.GetCurrentProcess().Id) return showReader.Read(courseware.Target.Handle);
            }
            return foreground;
        }
        internal void Poll()
        {
            if(closing||hwnd==IntPtr.Zero) return;
            courseware.Observe(settings.Mode!=RunMode.Desktop ? ObserveForeground() : null);
            UpdateModeVisual();
            if(settings.Mode!=RunMode.Courseware&&!IsPageMode&&!testing) RefreshDesktop();
            if(!dragging&&(DateTime.UtcNow-lastPositionCheck).TotalSeconds>=2) {
                lastPositionCheck=DateTime.UtcNow;
                string current=string.Join(";",Forms.Screen.AllScreens.Select(s=>s.DeviceName+s.WorkingArea.ToString()+Native.MonitorScale(s)));
                if(current!=layout) { layout=current; ApplyPosition(); }
            }
            UpdateDock();
            if(settings.SuperTopmost&&IsVisible&&openMenu==null&&!statusOpen)
                Native.KeepOverlayAboveWindows(hwnd,guide!=null&&guide.IsVisible ? new WindowInteropHelper(guide).Handle : IntPtr.Zero);
        }
        private void RefreshDesktop()
        {
            try {
                var snapshot=desktopState.Read();
                Guid own=snapshot.IsUninitialized ? Guid.Empty : desktops.WindowDesktop(hwnd);
                if(!snapshot.IsUninitialized&&own!=Guid.Empty&&own!=snapshot.Current) {
                    desktops.Move(hwnd,snapshot.Current);
                    Native.RaiseOverlay(hwnd);
                }
                string status=(snapshot.Index+1)+" / "+snapshot.Count;
                if(status!=lastStatus) {
                    lastStatus=status; tray.Text="桌面切屏/课件翻页助手 · "+(settings.Mode==RunMode.Automatic ? "自动切换 · " : "")+"桌面切屏 · "+status;
                    left.ToolTip=snapshot.Index==0 ? "已在第一个桌面" : "切换到左侧桌面 · "+status;
                    right.ToolTip=snapshot.Index==snapshot.Count-1 ? "新建并进入桌面 · "+status : "切换到右侧桌面 · "+status;
                }
            } catch(Exception error) {
                desktops.Dispose(); lastStatus=""; tray.Text="桌面切屏/课件翻页助手 · 等待 Windows 桌面信息";
                if((DateTime.UtcNow-lastFollowError).TotalSeconds>=15) { Settings.Log("Follow: "+error.Message); lastFollowError=DateTime.UtcNow; }
            }
        }
        private void UpdateModeVisual()
        {
            bool active=IsPageMode;
            bool show=settings.Mode!=RunMode.Courseware||active;
            bool routeChanged=visualCourseware!=active;
            if(routeChanged) {
                visualCourseware=active;
                lastStatus="";
                System.Windows.Automation.AutomationProperties.SetName(left,active ? "上一页" : "左侧桌面");
                System.Windows.Automation.AutomationProperties.SetName(right,active ? "下一页" : "右侧桌面或新建");
                left.ToolTip=active ? "上一页" : "切换到左侧桌面";
                right.ToolTip=active ? "下一页" : "切换到右侧桌面；到末尾时新建";
            }
            if(active) tray.Text=settings.Mode==RunMode.Automatic ? "桌面切屏/课件翻页助手 · 自动切换 · 课件翻页" : "桌面切屏/课件翻页助手 · 课件翻页";
            else if(!show) tray.Text="桌面切屏/课件翻页助手 · 课件翻页 · 等待放映";
            if(!show) {
                if(guide!=null) guide.Hide();
                if(dragging) { FinishDrag(); capsule.ReleaseMouseCapture(); }
                if(ContextMenu.IsOpen) CloseMenu();
                if(IsVisible) Hide();
                return;
            }
            if(!IsVisible) { docking.Expand(Now); Show(); ApplyPosition(); }
            if(active&&visualGeneration!=courseware.Generation) {
                visualGeneration=courseware.Generation; docking.Expand(Now); ApplyDockVisual(false); ApplyPosition();
                Native.RaiseOverlay(hwnd);
            }
            if(active&&courseware.BannerVisible) {
                docking.Expand(Now); ApplyDockVisual(false); PositionGuide();
            } else if(guide!=null&&guide.IsVisible) guide.Hide();
            if(routeChanged&&!active) ApplyPosition();
        }
        private void PositionGuide()
        {
            if(closing||hwnd==IntPtr.Zero||!IsVisible||!IsPageMode||!courseware.BannerVisible) return;
            Native.RECT host;
            if(!Native.GetWindowRect(hwnd,out host)) return;
            double scale=Native.DpiScale(hwnd); int inset=(int)Math.Round(8*scale);
            var visible=new Native.RECT { Left=host.Left+inset,Top=host.Top+inset,Right=host.Right-inset,Bottom=host.Bottom-inset };
            var screen=Forms.Screen.FromHandle(hwnd); var bounds=screen.Bounds;
            if(guide==null) guide=new SpotlightGuide(this,()=>{ courseware.DismissBanner(); guide.Hide(); docking.Expand(Now); },DismissCoursewareTips);
            try {
                guide.ShowFor(new Native.RECT { Left=bounds.Left,Top=bounds.Top,Right=bounds.Right,Bottom=bounds.Bottom },visible,Native.MonitorScale(screen));
            } catch(Exception error) { courseware.DismissBanner(); guide.Hide(); Notify("无法显示课件引导："+error.Message); }
        }
        private void UpdateAppearance()
        { if(!closing) ApplyAppearance(capsule,leftArrow,rightArrow,settings.TransparentMode,capsule.IsMouseOver); }
        internal static void ApplyAppearance(Border surface,Path leftPath,Path rightPath,bool transparent,bool hover)
        {
            // A rectangular invisible interaction layer covers the full button bounds,
            // including corners outside the decorative capsule's rounded background.
            surface.Background=transparent ? Brushes.Transparent : (Brush)new BrushConverter().ConvertFromString("#A82B3038");
            ((Grid)surface.Child).Background=transparent ? new SolidColorBrush(Color.FromArgb(1,0,0,0)) : null;
            surface.BorderBrush=transparent ? null : (Brush)new BrushConverter().ConvertFromString("#38FFFFFF");
            surface.Effect=transparent ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=10,ShadowDepth=2,Opacity=0.22 };
            leftPath.Stroke=rightPath.Stroke=transparent ? Brushes.Black : (Brush)new BrushConverter().ConvertFromString("#FFF4F6FA");
            leftPath.Opacity=rightPath.Opacity=1;
            var divider=(Rectangle)surface.FindName("Divider");
            divider.Fill=transparent ? Brushes.Black : (Brush)new BrushConverter().ConvertFromString("#28FFFFFF");
            var root=(FrameworkElement)surface.Parent;
            foreach(string name in new[] { "LeftButton","RightButton" })
                ((Button)surface.FindName(name)).Style=(Style)root.Resources[transparent ? (object)"ImmersiveButton" : typeof(Button)];
        }
        private void ExpandDock()
        {
            if(closing) return;
            if(docking.Collapsed) nextPageClick=DateTime.UtcNow.AddMilliseconds(230);
            docking.Expand(Now); ApplyDockVisual();
        }
        private void UpdateDock()
        {
            if(!IsVisible) return;
            Native.RECT host; Native.POINT cursor;
            if(!Native.GetWindowRect(hwnd,out host)||!Native.GetCursorPos(out cursor)) return;
            var hit=docking.Collapsed ? Docking.Tab(host,docking.Side,Native.DpiScale(hwnd)) : host;
            TickDock(Now,Docking.Contains(hit,cursor));
        }
        internal void TickDock(long now,bool pointerInside)
        {
            if(closing) return;
            bool wasCollapsed=docking.Collapsed;
            docking.Tick(now,pointerInside,settings.AutoHideEnabled&&!dragging&&!busy&&openMenu==null&&(guide==null||!guide.IsVisible));
            if(wasCollapsed&&!docking.Collapsed) nextPageClick=DateTime.UtcNow.AddMilliseconds(230);
            ApplyDockVisual();
        }
        private void ApplyDockVisual(bool animate=true)
        { motion.Update(docking.Collapsed,docking.Side,animate&&IsVisible); }
        private static int MouseButtons()
        {
            return ((Native.GetAsyncKeyState(1)&0x8000)!=0 ? 1 : 0)|((Native.GetAsyncKeyState(2)&0x8000)!=0 ? 2 : 0)|
                ((Native.GetAsyncKeyState(4)&0x8000)!=0 ? 4 : 0);
        }
        private void ObserveMenu()
        {
            if(openMenu==null||!openMenu.IsOpen) { menuTimer.Stop(); return; }
            Native.POINT cursor; if(!Native.GetCursorPos(out cursor)) return;
            IntPtr foreground=Native.GetForegroundWindow(); uint pid;
            Native.GetWindowThreadProcessId(foreground,out pid);
            ObserveMenuPointer(cursor,MouseButtons(),foreground!=menuForeground&&pid!=(uint)Process.GetCurrentProcess().Id);
        }
        internal void ObserveMenuPointer(Native.POINT pointer,int buttons,bool foregroundChanged)
        {
            if(openMenu==null||!openMenu.IsOpen) return;
            if(menuDismissal.Observe(WidgetMenu.ContainsPointer(openMenu,pointer),buttons,foregroundChanged)) CloseMenu();
        }
        private void CloseMenu()
        {
            var menu=openMenu;
            openMenu=null; menuTimer.Stop();
            if(menu!=null) {
                menu.IsOpen=false;
                if(Mouse.Captured==menu) Mouse.Capture(null);
            }
        }
        private bool NavigationStillValid(RunMode selected,bool page,ShowWindow target,int generation)
        {
            if(closing||settings.Mode!=selected) return false;
            var foreground=ReadForeground();
            if(selected!=RunMode.Desktop&&(foreground==null||Native.GetForegroundWindow()!=foreground.Handle)) return false;
            return page ? courseware.CanSend(foreground,target,generation) :
                selected!=RunMode.Courseware&&courseware.DesktopAllowed(foreground,generation);
        }
        private async Task Navigate(bool goRight)
        {
            if(testing||busy||closing||dragging||docking.Collapsed||!IsVisible||DateTime.UtcNow<nextPageClick) return;
            var target=courseware.Target;
            int generation=courseware.Generation;
            RunMode selected=settings.Mode;
            // A click always keeps its original route, even if the mode changes during awaits.
            bool page=IsPageMode;
            if(page&&target==null) return;
            Func<bool> stillValid=()=>NavigationStillValid(selected,page,target,generation);
            if(!stillValid()) return;
            busy=true; left.IsEnabled=false; right.IsEnabled=false;
            try {
                var clock=Stopwatch.StartNew();
                while(Native.ModifiersDown()&&clock.ElapsedMilliseconds<800) await Task.Delay(40,shutdown.Token);
                if(Native.ModifiersDown()) throw new InvalidOperationException("请先松开 Ctrl、Win、Alt 和 Shift 键，再点击箭头。");
                if(page) {
                    bool sent=Native.SendSingleKey(goRight ? (ushort)0x27 : (ushort)0x25,stillValid);
                    if(sent) Settings.Log("Courseware: "+(goRight ? "Right" : "Left"));
                    return;
                }
                if(!stillValid()) return;
                DesktopSnapshot before=null;
                for(int attempt=0;attempt<4;attempt++) {
                    if(!stillValid()) return;
                    var first=desktopState.Read(); await Task.Delay(50,shutdown.Token);
                    if(!stillValid()) return;
                    var second=desktopState.Read();
                    if(first.SameAs(second)) { before=second; break; }
                }
                if(before==null) throw new InvalidOperationException("虚拟桌面正在变化，请稍后重试。");
                Guid own=before.IsUninitialized ? Guid.Empty : desktops.WindowDesktop(hwnd);
                if(!before.IsUninitialized&&own!=Guid.Empty&&(own!=before.Current||!desktops.IsCurrent(hwnd)))
                    throw new InvalidOperationException("当前桌面尚未同步，请稍后重试。");
                DesktopAction action=before.Action(goRight);
                if(action==DesktopAction.None) return;
                if(before.IsUninitialized && Native.GetShellWindow()==IntPtr.Zero)
                    throw new InvalidOperationException("Windows 桌面尚未就绪，请稍后重试。");
                ushort key=action==DesktopAction.Create ? (ushort)0x44 : (action==DesktopAction.Left ? (ushort)0x25 : (ushort)0x27);
                if(!Native.SendChord(key,stillValid)) return;
                if(before.IsUninitialized) desktopState.MarkInitializationAttempt();
                Guid expected=action==DesktopAction.Left ? before.Ids[before.Index-1] :
                    (action==DesktopAction.Right ? before.Ids[before.Index+1] : Guid.Empty);
                clock.Restart(); bool confirmed=false;
                while(clock.ElapsedMilliseconds<(before.IsUninitialized ? 5000 : 2500)) {
                    await Task.Delay(80,shutdown.Token); if(!stillValid()) return;
                    try {
                        var now=desktopState.Read();
                        confirmed=action==DesktopAction.Create ? before.ConfirmsCreation(now) : now.Current==expected;
                        if(confirmed) { Poll(); await Task.Delay(400,shutdown.Token); break; }
                    } catch(InvalidOperationException) { }
                }
                if(!confirmed) throw new InvalidOperationException("未能确认桌面切换。请稍后重试；本次不会自动补发或重复新建。");
                Settings.Log("Navigation: "+action+" confirmed");
            } catch(OperationCanceledException) { }
            catch(Exception error) { if(!closing) Notify(error.Message); }
            finally {
                if(page) nextPageClick=DateTime.UtcNow.AddMilliseconds(120);
                if(!closing) { busy=false; left.IsEnabled=true; right.IsEnabled=true; }
            }
        }
        private void Notify(string message)
        { Settings.Log(message); if(!testing) tray.ShowBalloonTip(4000,"桌面切屏/课件翻页助手",message,Forms.ToolTipIcon.Info); }
        private bool SaveSettings()
        {
            try { if(!testing) settings.Save(); return true; }
            catch(Exception error) { Notify("无法保存设置："+error.Message); return false; }
        }
        private void DismissCoursewareTips()
        {
            bool previous=settings.SuppressCoursewareTips; settings.SuppressCoursewareTips=true;
            if(!SaveSettings()) { settings.SuppressCoursewareTips=previous; return; }
            courseware.SetTipsSuppressed(true); if(guide!=null) guide.Hide(); docking.Expand(Now);
        }
        private void ResetPosition()
        {
            settings.DefaultPosition=true; settings.CurrentDock=DockSide.None; docking.SetSide(DockSide.None,Now);
            ApplyDockVisual(); ApplyPosition(); SaveSettings();
        }
        private void ToggleStartup()
        { if(testing) return; try { Settings.AutoStart=!Settings.AutoStart; } catch(Exception error) { Notify("无法保存开机启动设置："+error.Message); } }
        internal void RequestExit()
        {
            if(!testing) ProcessExitGuard.Arm();
            if(closing) return;
            Settings.Log("Exit requested PID="+Process.GetCurrentProcess().Id);
            CleanupResources();
            ExitSequence.Run(()=>Close(),()=>{
                if(Application.Current!=null) Application.Current.Shutdown();
            },error=>Settings.Log("Exit: "+error));
        }
        internal void CleanupResources()
        {
            if(resourcesCleaned) return;
            resourcesCleaned=true; closing=true; dragging=false; dragButton=null;
            Settings.Log("Cleanup begin PID="+Process.GetCurrentProcess().Id);
            CleanupSteps.Run(new Action[] {
                ()=>shutdown.Cancel(),()=>timer.Stop(),()=>menuTimer.Stop(),
                ()=>{ if(Mouse.Captured==capsule) Mouse.Capture(null); },
                ()=>CloseMenu(),
                ()=>{ if(!testing) SystemEvents.DisplaySettingsChanged-=DisplayChanged; },
                ()=>{ if(!testing) SystemEvents.UserPreferenceChanged-=PreferenceChanged; },
                ()=>{ if(guide!=null) { guide.Close(); guide=null; } },
                ()=>{ tray.Visible=false; },
                ()=>tray.Dispose(),()=>ownedIcon.Dispose(),()=>desktops.Dispose()
            },error=>Settings.Log("Cleanup: "+error));
            Settings.Log("Cleanup complete PID="+Process.GetCurrentProcess().Id);
        }
        private void ShowRunningStatus()
        {
            if(closing||statusOpen||testing) return;
            CloseMenu();
            statusOpen=true;
            try {
                var status=new FirstUseDialog(false) { Topmost=true };
                if(IsVisible) status.Owner=this;
                if(status.ShowDialog()==true) {
                    if(status.ExitRequested) RequestExit();
                    else ChangeMode(status.SelectedMode,true);
                }
            } finally { statusOpen=false; }
        }
        private Forms.Screen EffectiveScreen()
        {
            if(IsPageMode) {
                var target=courseware.Target.MonitorBounds;
                return Forms.Screen.FromPoint(new System.Drawing.Point((target.Left+target.Right)/2,(target.Top+target.Bottom)/2));
            }
            return settings.DefaultPosition ? Forms.Screen.PrimaryScreen :
                Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==settings.Monitor)??Forms.Screen.PrimaryScreen;
        }
        private void ApplyPosition()
        {
            if(hwnd==IntPtr.Zero||closing||dragging||positioning) return;
            positioning=true;
            try {
                var screen=EffectiveScreen(); double scale=Native.MonitorScale(screen); var area=screen.WorkingArea;
                var work=new Native.RECT { Left=area.Left,Top=area.Top,Right=area.Right,Bottom=area.Bottom };
                bool temporary=IsPageMode&&settings.Monitor!=""&&settings.Monitor!=screen.DeviceName;
                var bounds=Place(settings.DefaultPosition||temporary,settings.OffsetX,settings.OffsetY,work,scale,new Size(Width,Height));
                bounds=Docking.Snap(bounds,work,settings.AutoHideEnabled ? settings.CurrentDock : DockSide.None,scale);
                Native.RECT current;
                if(!Native.GetWindowRect(hwnd,out current)||current.Left!=bounds.Left||current.Top!=bounds.Top)
                    Native.SetWindowPos(hwnd,IntPtr.Zero,bounds.Left,bounds.Top,0,0,0x0015);
            } finally { positioning=false; }
            PositionGuide();
        }
        internal static Native.RECT Place(bool defaultPosition,double offsetX,double offsetY,Native.RECT area,double scale,Size footprint)
        {
            int width=(int)Math.Round(footprint.Width*scale),height=(int)Math.Round(footprint.Height*scale);
            int x=defaultPosition ? area.Right-(int)Math.Round(124*scale) : area.Left+(int)Math.Round(offsetX*scale);
            int y=defaultPosition ? area.Bottom-(int)Math.Round(72*scale) : area.Top+(int)Math.Round(offsetY*scale);
            int inset=(int)Math.Round(8*scale);
            x=Clamp(x,area.Left-inset,Math.Max(area.Left-inset,area.Right-width+inset));
            y=Clamp(y,area.Top-inset,Math.Max(area.Top-inset,area.Bottom-height+inset));
            return new Native.RECT { Left=x,Top=y,Right=x+width,Bottom=y+height };
        }
        internal static int Clamp(int value,int minimum,int maximum) { return Math.Max(minimum,Math.Min(maximum,value)); }
        private static Button HitButton(DependencyObject source)
        {
            while(source!=null) {
                if(source is Button) return (Button)source;
                source=System.Windows.Media.VisualTreeHelper.GetParent(source);
            }
            return null;
        }
        private void BeginDrag(object sender,MouseButtonEventArgs e)
        {
            if(testing) return;
            e.Handled=true;
            if(busy||DateTime.UtcNow<nextPageClick) return;
            dragButton=HitButton(e.OriginalSource as DependencyObject);
            dragMode=settings.Mode; dragGeneration=courseware.Generation;
            Native.GetCursorPos(out dragStart); Native.GetWindowRect(hwnd,out dragRect);
            dragging=true; moved=false; docking.Expand(Now); capsule.CaptureMouse(); e.Handled=true;
        }
        private void ContinueDrag(object sender,MouseEventArgs e)
        {
            if(!dragging) return;
            Native.POINT now; Native.GetCursorPos(out now);
            int dx=now.X-dragStart.X,dy=now.Y-dragStart.Y;
            if(!moved&&!DragThresholdReached(dx,dy)) return;
            moved=true;
            Native.SetWindowPos(hwnd,IntPtr.Zero,dragRect.Left+dx,dragRect.Top+dy,0,0,0x0015);
        }
        internal static bool DragThresholdReached(int dx,int dy) { return (long)dx*dx+(long)dy*dy>=9; }
        private void EndDrag(object sender,MouseButtonEventArgs e)
        {
            if(!dragging) return;
            var click=dragButton; bool navigates=!moved&&click!=null&&settings.Mode==dragMode&&courseware.Generation==dragGeneration;
            FinishDrag(); capsule.ReleaseMouseCapture(); dragButton=null; e.Handled=true;
            if(navigates) {
                Point point=e.GetPosition(click);
                if(point.X>=0&&point.Y>=0&&point.X<=click.ActualWidth&&point.Y<=click.ActualHeight)
                    click.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }
        private void FinishDrag()
        {
            dragging=false; if(!moved) return;
            Native.RECT rect; Native.GetWindowRect(hwnd,out rect); SaveDraggedPosition(rect);
        }
        internal void SaveDraggedPosition(Native.RECT rect)
        {
            var screen=Forms.Screen.FromPoint(new System.Drawing.Point((rect.Left+rect.Right)/2,(rect.Top+rect.Bottom)/2));
            double scale=Native.MonitorScale(screen); var area=screen.WorkingArea;
            var work=new Native.RECT { Left=area.Left,Top=area.Top,Right=area.Right,Bottom=area.Bottom };
            settings.CurrentDock=settings.AutoHideEnabled ? Docking.Detect(rect,work,scale) : DockSide.None;
            rect=Docking.Snap(rect,work,settings.CurrentDock,scale); docking.SetSide(settings.CurrentDock,Now);
            settings.DefaultPosition=false; settings.Monitor=screen.DeviceName;
            settings.OffsetX=(rect.Left-area.Left)/scale; settings.OffsetY=(rect.Top-area.Top)/scale;
            ApplyDockVisual(); ApplyPosition(); SaveSettings();
        }
    }
}
