using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path=System.Windows.Shapes.Path;
using System.Windows.Threading;

namespace DesktopSwitcher
{
    internal static class WidgetFeatureTests
    {
        internal static void Run(Action<bool,string> check)
        {
            var root=WidgetWindow.CreateContent(); var size=WidgetWindow.MeasureContent(root);
            check(size==new Size(112,60)&&root.FindName("ModeBanner")==null,"capsule host remains compact and old reminder is removed");
            var capsule=(Border)root.FindName("Capsule"); var tab=(Border)root.FindName("EdgeTab");
            var left=(Path)root.FindName("LeftArrow"); var right=(Path)root.FindName("RightArrow");
            WidgetWindow.ApplyAppearance(capsule,left,right,true,false);
            check(((SolidColorBrush)capsule.Background).Color.A<=8&&left.Opacity>0&&right.Opacity>0,
                "transparent mode keeps a nearly invisible hit surface and both visible arrows");
            var alpha=((SolidColorBrush)capsule.Background).Color.A;
            WidgetWindow.ApplyAppearance(capsule,left,right,true,true);
            check(left.Opacity==1&&right.Opacity==1&&((SolidColorBrush)capsule.Background).Color.A==alpha,
                "hover restores arrow contrast without changing layout or transparency");
            WidgetWindow.ApplyAppearance(capsule,left,right,true,false);
            Save(Render(root,size),"widget-immersive-v1.3.1.png");
            WidgetWindow.ApplyAppearance(capsule,left,right,false,false);
            check(((SolidColorBrush)capsule.Background).Color.A==168&&left.Opacity==1,"normal appearance is restored after transparency toggle");
            capsule.Visibility=Visibility.Hidden; tab.Visibility=Visibility.Visible; tab.Margin=new Thickness(8,0,0,0);
            check(WidgetWindow.MeasureContent(root)==size,"edge hiding cannot reserve extra space or resize host");
            Save(Render(root,size),"edge-left-v1.3.1.png");
            tab.HorizontalAlignment=HorizontalAlignment.Right; tab.Margin=new Thickness(0,0,8,0); tab.CornerRadius=new CornerRadius(4,0,0,4);
            Save(Render(root,size),"edge-right-v1.3.1.png");
            var dock=new Docking(); dock.SetSide(DockSide.Left,1000);
            dock.Tick(1899,false,true); check(!dock.Collapsed,"docking waits for idle delay");
            dock.Tick(1900,false,true); check(dock.Collapsed,"idle dock collapses after 900 milliseconds");
            dock.Tick(1950,true,true); check(!dock.Collapsed,"touching edge immediately expands without clicking");
            dock.Tick(2900,false,false); check(!dock.Collapsed,"dragging busy menu or guide can keep dock expanded");
            dock.SetSide(DockSide.None,3000); dock.Tick(10000,false,true);
            check(!dock.Collapsed,"floating widget never collapses away from left or right edge");
            foreach(double scale in new[] { 1.0,1.25,1.5,1.75,2.0,2.25,3.0 }) {
                var area=new Native.RECT { Left=-1920,Top=-200,Right=0,Bottom=1040 };
                foreach(int offset in new[] { 0,1,10,35,70,120,190 }) {
                    var position=WidgetWindow.Place(false,offset,300,area,scale,size);
                    check(position.Left==area.Left+(int)Math.Round(offset*scale),"left drag range remains available at "+offset+" scale="+scale);
                }
                var edge=WidgetWindow.Place(false,-100,-100,area,scale,size);
                check(edge.Left==area.Left-(int)Math.Round(8*scale)&&edge.Top==area.Top-(int)Math.Round(8*scale),"top and left screen edges remain reachable at scale="+scale);
                check(Docking.Detect(edge,area,scale)==DockSide.Left,"left snap threshold uses visible capsule at scale="+scale);
                var far=WidgetWindow.Place(false,9999,9999,area,scale,size);
                check(Docking.Detect(far,area,scale)==DockSide.Right,"right snap threshold uses visible capsule at scale="+scale);
                foreach(DockSide side in new[] { DockSide.Left,DockSide.Right }) {
                    var snapped=Docking.Snap(edge,area,side,scale); var strip=Docking.Tab(snapped,side,scale);
                    check(side==DockSide.Left ? strip.Left==area.Left : strip.Right==area.Right,"collapsed strip sits exactly at "+side+" scale="+scale);
                    check(strip.Right-strip.Left==(int)Math.Round(6*scale)&&strip.Bottom-strip.Top==(int)Math.Round(32*scale),
                        "edge tab scales independently from full capsule at "+side+" scale="+scale);
                    check(Docking.Contains(strip,new Native.POINT { X=strip.Left,Y=strip.Top })&&!Docking.Contains(strip,new Native.POINT { X=strip.Right,Y=strip.Bottom }),
                        "only visible tab triggers collapsed hover at "+side+" scale="+scale);
                }
            }
            var screen=new Size(800,500); var card=new Size(240,100);
            foreach(var origin in new[] { new Point(0,0),new Point(0,400),new Point(696,0),new Point(696,400),new Point(330,230) }) {
                var target=new Rect(origin,new Size(96,44)); var layout=GuideLayout.Calculate(screen,target,card);
                check(new Rect(new Point(),screen).Contains(layout.Card),"spotlight card stays on screen at "+origin);
                var mask=layout.Mask(screen);
                check(!new[] { target.TopLeft,target.TopRight,target.BottomLeft,target.BottomRight }.Any(p=>mask.FillContains(p)),
                    "rounded opening leaves the entire button rectangle unobstructed at "+origin);
                check(!layout.Hole.IntersectsWith(layout.Card)&&!layout.Hole.IntersectsWith(layout.Stem.Bounds),
                    "spotlight card and dotted connector stay clear of the buttons at "+origin);
                check(origin.Y==0 ? layout.Card.Top>target.Bottom : layout.Card.Bottom<target.Top,
                    "spotlight card chooses available vertical space at "+origin);
            }
            var shortScreen=new Size(700,240);
            var shortLayout=GuideLayout.Calculate(shortScreen,new Rect(302,98,96,44),card);
            check(shortLayout.Card.Width==card.Width&&shortLayout.Card.Height==card.Height&&shortLayout.Card.Right<shortLayout.Hole.Left&&
                !shortLayout.Stem.Bounds.IntersectsWith(shortLayout.Hole),"short screen moves the card and connector to the side without covering the widget");
            string settingsFile=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(WidgetFeatureTests).Assembly.Location),"settings-features-"+Guid.NewGuid()+".xml");
            try {
                var settings=new Settings { Mode=RunMode.Courseware,TransparentMode=true,Dock=DockSide.Right,EdgeAutoHide=false,
                    SuppressCoursewareTips=true,DefaultPosition=false,OffsetX=35,OffsetY=180 };
                settings.Save(settingsFile); var loaded=Settings.Load(settingsFile);
                check(loaded.Mode==RunMode.Courseware&&loaded.TransparentMode&&loaded.Dock==DockSide.Right&&!loaded.EdgeAutoHide&&loaded.SuppressCoursewareTips,
                    "run mode transparency docking and do-not-remind survive restart independently");
                File.WriteAllText(settingsFile,"<Settings><DefaultPosition>false</DefaultPosition><OffsetX>12</OffsetX><OffsetY>34</OffsetY><DisableCoursewareMode>false</DisableCoursewareMode><SuppressCoursewareTips>true</SuppressCoursewareTips></Settings>");
                loaded=Settings.Load(settingsFile);
                check(loaded.Mode==RunMode.Desktop&&!loaded.TransparentMode&&loaded.EdgeAutoHide&&loaded.Dock==DockSide.None&&loaded.OffsetX==12&&loaded.SuppressCoursewareTips,
                    "legacy settings default to explicit desktop mode and retain anchor and reminder choice");
            } finally { if(File.Exists(settingsFile)) File.Delete(settingsFile); }
            AdditionalChecks(check);
            var controller=new CoursewareMode(); controller.SetDisabled(true); var show=FakeShow();
            controller.Observe(show); controller.Observe(show);
            check(!controller.Active&&controller.DesktopAllowed(show,controller.Generation),"desktop mode keeps desktop route even during a slide show");
            int oldToken=controller.Generation; controller.SetDisabled(false);
            check(!controller.DesktopAllowed(show,oldToken),"selecting courseware invalidates a captured desktop click");
            controller.Observe(show); controller.Observe(show); oldToken=controller.Generation;
            controller.DismissBanner();
            check(!controller.BannerVisible&&controller.Active&&controller.CanSend(show,show,oldToken),"clicking dark guide area closes only this guide while paging remains active");
            controller.SetDisabled(true);
            check(!controller.CanSend(show,show,oldToken),"selecting desktop mode invalidates a pending page click");
        }

        private static void AdditionalChecks(Action<bool,string> check)
        {
            var root=WidgetWindow.CreateContent(); var capsule=(Border)root.FindName("Capsule");
            WidgetWindow.ApplyAppearance(capsule,(Path)root.FindName("LeftArrow"),(Path)root.FindName("RightArrow"),true,true);
            check(capsule.BorderBrush==null&&capsule.Effect==null&&((SolidColorBrush)capsule.Background).Color.A==0&&((SolidColorBrush)((Grid)capsule.Child).Background).Color.A==1,
                "immersive mode removes visible background border and shadow while retaining invisible hit layer");
            check(((SolidColorBrush)((Path)root.FindName("RightArrow")).Stroke).Color==Colors.Black&&
                ((SolidColorBrush)((Rectangle)root.FindName("Divider")).Fill).Color==Colors.Black,"all three immersive glyphs use black");
            var image=Render(root,new Size(112,60)); var pixels=new byte[112*60*4]; image.CopyPixels(pixels,448,0);
            bool onlyBlack=true,hasGlyph=false;
            for(int i=0;i<pixels.Length;i+=4) if(pixels[i+3]>1) { hasGlyph=true; onlyBlack&=pixels[i]==0&&pixels[i+1]==0&&pixels[i+2]==0; }
            check(onlyBlack&&hasGlyph,"immersive render contains only black glyphs above the invisible interaction layer");
            foreach(double scale in new[] { 1.0,1.25,1.5,2.25,3.0 }) {
                int inset=(int)Math.Round(8*scale),width=(int)Math.Round(112*scale);
                var work=new Native.RECT { Left=-1920,Top=0,Right=0,Bottom=1080 };
                foreach(int gap in new[] { 3,4,8,16,24,40 }) {
                    var raw=new Native.RECT { Left=work.Left-inset+gap,Top=150,Right=work.Left-inset+gap+width,Bottom=285 };
                    check(Docking.Detect(raw,work,scale)==DockSide.None,"near-left edge stays floating at "+gap+" physical pixels, scale="+scale);
                    raw.Right=work.Right+inset-gap; raw.Left=raw.Right-width;
                    check(Docking.Detect(raw,work,scale)==DockSide.None,"near-right edge stays floating at "+gap+" physical pixels, scale="+scale);
                }
            }
            check(!WidgetWindow.DragThresholdReached(1,1)&&WidgetWindow.DragThresholdReached(3,0)&&WidgetWindow.DragThresholdReached(0,3),
                "small drag threshold distinguishes click from continuous pixel motion without grid snapping");
            var settings=new Settings { Dock=DockSide.Right };
            settings.Mode=RunMode.Courseware;
            check(!settings.AutoHideEnabled&&settings.CurrentDock==DockSide.None,"courseware defaults to no docking independently of desktop preference");
            settings.AutoHideEnabled=true; settings.CurrentDock=DockSide.Left; settings.Mode=RunMode.Desktop;
            check(settings.AutoHideEnabled&&settings.CurrentDock==DockSide.Right,"courseware docking choice does not overwrite desktop preference");

            var close=new MenuDismissal(); var menu=new Native.RECT { Left=100,Top=100,Right=300,Bottom=400 };
            close.Open(2);
            check(!close.Observe(menu,new Native.POINT { X=10,Y=10 },2,false),"opening right-button press cannot dismiss menu by itself");
            close.Observe(menu,new Native.POINT { X=10,Y=10 },0,false);
            check(close.Observe(menu,new Native.POINT { X=10,Y=10 },1,false),"fresh outside press dismisses unselected menu");
            close.Open(0);
            check(!close.Observe(menu,new Native.POINT { X=150,Y=150 },1,false),"inside press leaves command selection to menu");
            check(close.Observe(menu,new Native.POINT { X=150,Y=150 },0,true),"external foreground change dismisses menu even without observed click");

            int asks=0,enables=0; settings=new Settings { Mode=RunMode.Courseware };
            StartupConsent.ApplyChoice(settings,false,()=>{ asks++; return false; },()=>enables++);
            check(settings.CoursewareStartupAsked&&asks==1&&enables==0,"declining startup permission never enables startup");
            StartupConsent.ApplyChoice(settings,false,()=>{ asks++; return true; },()=>enables++);
            check(asks==1&&enables==0,"declined startup permission is not asked repeatedly");
            settings=new Settings { Mode=RunMode.Courseware };
            StartupConsent.ApplyChoice(settings,false,()=>{ asks++; return true; },()=>enables++);
            check(enables==1&&settings.CoursewareStartupAsked,"startup is enabled only after explicit affirmative choice");
            settings=new Settings { Mode=RunMode.Desktop };
            StartupConsent.ApplyChoice(settings,false,()=>{ asks++; return true; },()=>enables++);
            check(enables==1&&!settings.CoursewareStartupAsked,"desktop choice never requests or enables startup");
            settings.Mode=RunMode.Courseware; int before=asks;
            StartupConsent.ApplyChoice(settings,true,()=>{ asks++; return true; },()=>enables++);
            check(asks==before&&enables==1&&settings.CoursewareStartupAsked,"already-enabled startup needs no new permission");

            var welcome=FirstUseDialog.CreateContent();
            welcome.Measure(new Size(452,double.PositiveInfinity)); var size=new Size(452,Math.Ceiling(welcome.DesiredSize.Height));
            check(welcome.FindName("ChooseDesktop") is Button&&welcome.FindName("ChooseCourseware") is Button&&welcome.FindName("ChooseAutomatic") is Button,
                "first-use view offers all three modes");
            Save(Render(welcome,size),"first-use-v1.4.0.png");
            var consent=StartupConsentDialog.CreateContent();
            consent.Measure(new Size(452,double.PositiveInfinity));
            Save(Render(consent,new Size(452,Math.Ceiling(consent.DesiredSize.Height))),"startup-consent-v1.3.3.png");
            check(((Button)consent.FindName("DeclineStartup")).IsCancel&&((Button)consent.FindName("DeclineStartup")).IsDefault,
                "startup consent defaults to declining for Escape and Enter");
            var path=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(WidgetFeatureTests).Assembly.Location),"first-use-settings.xml");
            try {
                settings=new Settings { Mode=RunMode.Courseware,SetupCompleted=true,CoursewareStartupAsked=true,
                    CoursewareEdgeAutoHide=true,CoursewareDock=DockSide.Left,Dock=DockSide.Right };
                settings.Save(path); settings=Settings.Load(path);
                check(settings.SetupCompleted&&settings.CoursewareStartupAsked&&settings.CoursewareEdgeAutoHide&&settings.CoursewareDock==DockSide.Left&&settings.Dock==DockSide.Right,
                    "first-use completion startup consent and per-mode docking persist");
                File.WriteAllText(path,"<Settings><Mode>Courseware</Mode><EdgeAutoHide>true</EdgeAutoHide><Dock>Right</Dock></Settings>");
                settings=Settings.Load(path);
                check(!settings.SetupCompleted&&!settings.AutoHideEnabled&&settings.CurrentDock==DockSide.None&&settings.Dock==DockSide.Right,
                    "upgrading shows mode chooser once and avoids carrying desktop auto-hide into courseware");
                settings.Mode=RunMode.Automatic;
                check(!settings.AutoHideEnabled&&settings.CurrentDock==DockSide.None,"automatic mode starts with edge hiding disabled");
                settings.AutoHideEnabled=true; settings.CurrentDock=DockSide.Left; settings.SuperTopmost=true;
                settings.Save(path); settings=Settings.Load(path);
                check(settings.Mode==RunMode.Automatic&&settings.AutoHideEnabled&&settings.CurrentDock==DockSide.Left&&settings.SuperTopmost,
                    "automatic mode, its own dock preference and super topmost persist");
                settings.Mode=RunMode.Courseware;
                check(!settings.AutoHideEnabled&&settings.CurrentDock==DockSide.None,"automatic docking does not overwrite courseware docking");
            } finally { if(File.Exists(path)) File.Delete(path); }
        }

        internal static void Live(Action<bool,string> check)
        {
            var screen=System.Windows.Forms.Screen.PrimaryScreen; var bounds=screen.Bounds; var area=screen.WorkingArea;
            ShowWindow foreground=new ShowWindow();
            var settings=new Settings { Mode=RunMode.Courseware,DefaultPosition=false,Monitor=screen.DeviceName,
                OffsetX=350,OffsetY=180,Dock=DockSide.Left,SetupCompleted=true };
            var widget=new WidgetWindow(settings,()=>foreground,true);
            try {
                widget.Start(); Pump(120); var handle=new WindowInteropHelper(widget).Handle;
                check(!widget.IsVisible&&widget.Guide==null,"courseware startup stays hidden without showing first-use dialog during tests");
                widget.OpenTrayMenu(); Pump(200); widget.Poll();
                var trayItems=widget.TrayMenu.Items.OfType<MenuItem>().ToArray();
                check(widget.TrayMenu.IsOpen&&trayItems.Any(m=>(string)m.Header=="恢复右下角位置")&&trayItems.Any(m=>(string)m.Header=="开机启动"),
                    "tray menu opens with hidden widget and retains reset and startup options");
                check(trayItems.Single(m=>(string)m.Header=="课件翻页").IsChecked,"tray menu shows selected mode");
                Save(Capture(widget.TrayMenu),"tray-menu-v1.4.0.png");
                widget.TrayMenu.IsOpen=false; Pump(50);
                foreground=FakeShow(); widget.Poll(); check(!widget.IsVisible,"first show observation remains hidden");
                widget.Poll(); Pump(150);
                check(widget.IsVisible&&widget.Guide!=null&&widget.Guide.IsVisible,"confirmed show displays capsule and spotlight");
                var guide=widget.Guide; var guideHandle=new WindowInteropHelper(guide).Handle;
                Native.RECT guideRect,host; Native.GetWindowRect(guideHandle,out guideRect); Native.GetWindowRect(handle,out host);
                check(guideRect.Left==bounds.Left&&guideRect.Top==bounds.Top&&guideRect.Right==bounds.Right&&guideRect.Bottom==bounds.Bottom,
                    "spotlight covers exact physical monitor bounds");
                double scale=Native.DpiScale(handle),guideScale=Native.DpiScale(guideHandle);
                var center=new Point(((host.Left+host.Right)/2.0-guideRect.Left)/guideScale,((host.Top+host.Bottom)/2.0-guideRect.Top)/guideScale);
                var canvas=(Canvas)guide.Content; var mask=(Path)canvas.Children[0];
                check(guide.Layout.Hole.Contains(center)&&!mask.Data.FillContains(center)&&canvas.InputHitTest(center)==null,
                    "spotlight hole aligns to capsule and leaves paging area unobstructed");
                var guideCard=canvas.Children.OfType<Border>().Single();
                var message=((StackPanel)guideCard.Child).Children.OfType<TextBlock>().Single();
                var pill=(Border)guide.SuppressButton.Template.FindName("Pill",guide.SuppressButton);
                check(message.Text=="点击此处翻页"&&guide.SuppressButton.Content.ToString()=="不再提醒","guide uses requested copy and suppression button");
                check(guideCard.CornerRadius.TopLeft>=18&&pill.CornerRadius.TopLeft>=10,"guide card and suppress button render rounded corners");
                check(!guide.DismissAt(center,canvas)&&guide.IsVisible,"clicking highlighted capsule area does not dismiss guide");
                SaveGuidePreview(widget,guide,host,guideRect,guideScale);
                Pump(5400); widget.Poll();
                check(guide.IsVisible&&widget.IsVisible,"spotlight stays visible beyond former five-second timeout");
                Native.RECT after; Native.GetWindowRect(handle,out after);
                check(Same(host,after),"persistent spotlight does not move or resize capsule");
                guide.DismissAt(new Point(20,20),canvas); widget.Poll();
                check(!guide.IsVisible&&widget.IsVisible&&!settings.SuppressCoursewareTips,"outside click closes only current guide");
                widget.TickDock(long.MaxValue/2,false); Pump(240);
                check(!widget.IsDockCollapsed&&widget.IsVisible,"courseware mode does not auto-hide even with desktop docking enabled");
                foreground=new ShowWindow(); widget.Poll(); check(!widget.IsVisible,"leaving show hides courseware capsule");
                foreground=FakeShow(); widget.Poll(); widget.Poll(); Pump(80);
                check(guide.IsVisible,"new show entry restores unsuppressed guide");
                guide.SuppressButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); widget.Poll();
                check(!guide.IsVisible&&settings.SuppressCoursewareTips&&widget.IsVisible,"do-not-remind closes guide while paging remains available");
                foreground=new ShowWindow(); widget.Poll(); foreground=FakeShow(); widget.Poll(); widget.Poll();
                check(!guide.IsVisible&&widget.IsVisible,"later entries retain do-not-remind choice");
                widget.ChangeTransparency(true,false);
                var root=(Grid)widget.Content; var capsule=(Border)root.FindName("Capsule"); var tab=(Border)root.FindName("EdgeTab");
                check(capsule.BorderBrush==null&&capsule.Effect==null&&((SolidColorBrush)capsule.Background).Color.A==0&&((SolidColorBrush)((Grid)capsule.Child).Background).Color.A==1,
                    "immersive view has no visible frame background or shadow");
                check(((SolidColorBrush)((Path)root.FindName("LeftArrow")).Stroke).Color==Colors.Black&&
                    ((SolidColorBrush)((Rectangle)root.FindName("Divider")).Fill).Color==Colors.Black,"immersive arrows and divider are black");
                check(widget.InputHitTest(new Point(30,30))!=null,"invisible interaction surface keeps button hit area available");
                widget.ChangeMode(RunMode.Desktop,false); Pump(100);
                check(widget.IsVisible&&widget.CurrentMode==RunMode.Desktop&&!guide.IsVisible,"desktop selection displays capsule in any application");
                Native.GetWindowRect(handle,out host);
                check(host.Left+(int)Math.Round(8*scale)==area.Left,"desktop docking aligns visible capsule to physical left edge");
                var menuItems=widget.ContextMenu.Items.OfType<MenuItem>().ToArray();
                var menus=menuItems.Select(m=>(string)m.Header).ToArray();
                var more=menuItems.Single(m=>(string)m.Header=="更多功能");
                check(menus.SequenceEqual(new[] { "桌面切屏","课件翻页","自动切换","更多功能","退出程序" })&&
                    more.Items.OfType<MenuItem>().Select(m=>(string)m.Header).SequenceEqual(new[] { "透明沉浸","超级置顶","贴边自动隐藏" }),
                    "widget menu exposes three modes and groups optional features; tray-only commands are absent");
                check(capsule.Cursor==Cursors.Arrow&&((Rectangle)root.FindName("Divider")).Cursor==null&&((Button)root.FindName("LeftButton")).Cursor==Cursors.Hand,
                    "drag surface uses normal arrow and paging button uses hand cursor");

                widget.ContextMenu.PlacementTarget=widget; widget.ContextMenu.IsOpen=true; Pump(100);
                check(widget.ContextMenu.IsOpen,"context menu remains open until selection or dismissal");
                var source=PresentationSource.FromVisual(widget.ContextMenu) as HwndSource; Native.RECT menuBounds;
                check(source!=null&&Native.GetWindowRect(source.Handle,out menuBounds),"context menu has measurable native bounds");
                Native.GetWindowRect(source.Handle,out menuBounds);
                var selected=menuItems[0];
                var checkMark=(Border)selected.Template.FindName("Check",selected);
                check(selected.IsChecked&&checkMark.ActualWidth>=20&&checkMark.ActualHeight>=20,"selected item has a full-size check badge");
                Save(Capture(widget.ContextMenu),"widget-menu-v1.4.0.png");
                more.IsSubmenuOpen=true; Pump(100);
                var popup=(Popup)more.Template.FindName("PART_Popup",more);
                var subSource=PresentationSource.FromVisual(popup.Child) as HwndSource;
                check(popup.IsOpen&&subSource!=null,"more features opens a separate submenu popup");
                Native.RECT subBounds; Native.GetWindowRect(subSource.Handle,out subBounds);
                var subPoint=new Native.POINT { X=(subBounds.Left+subBounds.Right)/2,Y=(subBounds.Top+subBounds.Bottom)/2 };
                check(WidgetMenu.ContainsPointer(widget.ContextMenu,subPoint),"submenu bounds count as inside the open menu");
                widget.ObserveMenuPointer(subPoint,0,false); widget.ObserveMenuPointer(subPoint,1,false);
                check(widget.ContextMenu.IsOpen,"inside click is not mistaken for outside dismissal");
                Save(Capture((FrameworkElement)popup.Child),"more-menu-v1.4.0.png");
                more.IsSubmenuOpen=false; Pump(70);
                widget.ObserveMenuPointer(new Native.POINT { X=menuBounds.Left-30,Y=menuBounds.Top-30 },0,false);
                widget.ObserveMenuPointer(new Native.POINT { X=menuBounds.Left-30,Y=menuBounds.Top-30 },1,false);
                Pump(60);
                check(!widget.ContextMenu.IsOpen,"outside click closes unselected context menu");
                check(Mouse.Captured!=widget.ContextMenu,"closing context menu releases mouse capture");
                widget.ContextMenu.IsOpen=true; Pump(80);
                widget.ObserveMenuPointer(new Native.POINT(),0,true);
                check(!widget.ContextMenu.IsOpen,"foreground application change dismisses context menu");
                Pump(160);

                widget.TickDock(long.MaxValue/2,false); Pump(60);
                double hideMid=((TranslateTransform)capsule.RenderTransform).X;
                check(hideMid<0&&hideMid>-100&&capsule.Opacity>0&&capsule.Opacity<1,"left hide moves and fades through intermediate animation frame");
                Pump(230); Native.GetWindowRect(handle,out after);
                Console.WriteLine("Collapse state="+widget.IsDockCollapsed+" capsule="+capsule.Visibility+" tab="+tab.Visibility+
                    " host="+after.Left+","+after.Top+","+after.Right+","+after.Bottom+" expected="+host.Left+","+host.Top+","+host.Right+","+host.Bottom);
                check(widget.IsDockCollapsed&&capsule.Visibility==Visibility.Hidden&&tab.Visibility==Visibility.Visible&&Same(host,after),
                    "left collapse finishes as edge tab without changing host rectangle");
                tab.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent=Mouse.MouseEnterEvent }); Pump(60);
                check(((TranslateTransform)capsule.RenderTransform).X>-100&&((TranslateTransform)capsule.RenderTransform).X<0,
                    "left reveal animates continuously from edge");
                widget.TickDock(long.MaxValue/2,false); Pump(40);
                double beforeReverse=((TranslateTransform)capsule.RenderTransform).X;
                tab.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent=Mouse.MouseEnterEvent });
                check(Math.Abs(((TranslateTransform)capsule.RenderTransform).X-beforeReverse)<2,"rapid animation reversal starts from current visual position");
                Pump(260);
                check(!widget.IsDockCollapsed&&capsule.Visibility==Visibility.Visible&&tab.Visibility==Visibility.Hidden&&Math.Abs(((TranslateTransform)capsule.RenderTransform).X)<0.01,
                    "reversed animation finishes fully expanded");

                int inset=(int)Math.Round(8*scale),width=host.Right-host.Left,height=host.Bottom-host.Top;
                foreach(int gap in new[] { 20,8,4,3 }) {
                    var raw=new Native.RECT { Left=area.Left-inset+gap,Top=area.Top+300,Right=area.Left-inset+gap+width,Bottom=area.Top+300+height };
                    widget.SaveDraggedPosition(raw); Native.GetWindowRect(handle,out after);
                    check(settings.Dock==DockSide.None&&after.Left==raw.Left&&after.Top==raw.Top,"drag release preserves exact position with edge gap "+gap+" pixels");
                }
                var touch=new Native.RECT { Left=area.Left-inset,Top=area.Top+320,Right=area.Left-inset+width,Bottom=area.Top+320+height };
                widget.SaveDraggedPosition(touch); Native.GetWindowRect(handle,out after);
                check(settings.Dock==DockSide.Left&&after.Left+inset==area.Left,"actual edge contact activates docking");
                foreground=new ShowWindow(); widget.ChangeMode(RunMode.Courseware,false); widget.Poll();
                check(!widget.IsVisible&&!guide.IsVisible,"courseware selection outside a show hides capsule immediately");
                widget.ChangeMode(RunMode.Automatic,false); widget.Poll();
                check(widget.IsVisible&&!widget.IsPageMode&&!settings.AutoHideEnabled,"automatic mode displays desktop buttons and starts with docking disabled");
                foreground=FakeShow(); widget.Poll();
                check(widget.IsVisible&&!widget.IsPageMode,"automatic mode does not route the first observation as paging");
                widget.Poll(); check(widget.IsVisible&&widget.IsPageMode&&((Button)root.FindName("RightButton")).ToolTip.Equals("下一页"),
                    "automatic mode changes buttons to paging on confirmed full-screen slideshow");
                foreground=new ShowWindow(); widget.Poll();
                check(widget.IsVisible&&!widget.IsPageMode&&((Button)root.FindName("RightButton")).ToolTip.ToString().Contains("桌面"),
                    "automatic mode restores desktop buttons without hiding when slideshow loses foreground");
                var blocker=new Window { ShowActivated=false,Topmost=true,ShowInTaskbar=false,Width=200,Height=120,WindowStyle=WindowStyle.None };
                try {
                    blocker.Show(); Pump(60); var blockerHandle=new WindowInteropHelper(blocker).Handle;
                    Native.GetWindowRect(handle,out host);
                    Native.SetWindowPos(blockerHandle,new IntPtr(-1),host.Left,host.Top,200,120,0x0010);
                    check(IsAbove(blockerHandle,handle),"ordinary topmost window can cover the normal widget");
                    var beforeFocus=Native.GetForegroundWindow();
                    var super=more.Items.OfType<MenuItem>().Single(m=>(string)m.Header=="超级置顶");
                    super.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    check(settings.SuperTopmost&&IsAbove(handle,blockerHandle),"super topmost command raises the widget above another topmost window");
                    Native.RaiseOverlay(blockerHandle); widget.Poll();
                    check(IsAbove(handle,blockerHandle)&&Native.GetForegroundWindow()==beforeFocus,"super topmost recovers its position without taking foreground focus");
                    super.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Native.RaiseOverlay(blockerHandle); widget.Poll();
                    check(!settings.SuperTopmost&&IsAbove(blockerHandle,handle),"turning off super topmost stops reasserting priority");
                } finally { blocker.Close(); }
                check(Native.GetForegroundWindow()!=handle&&Native.GetForegroundWindow()!=guideHandle,"self-owned display and animation never activate capsule or guide");
                Console.WriteLine("Current DPI="+scale+"; guide stays until dismissed; no keyboard input sent.");
            } finally { widget.Close(); }

            var rightSettings=new Settings { Mode=RunMode.Desktop,Dock=DockSide.Right,TransparentMode=true,
                DefaultPosition=false,Monitor=screen.DeviceName,OffsetY=220,SetupCompleted=true };
            var rightWidget=new WidgetWindow(rightSettings,()=>new ShowWindow(),true);
            try {
                rightWidget.Start(); Pump(100); var handle=new WindowInteropHelper(rightWidget).Handle;
                Native.RECT host; Native.GetWindowRect(handle,out host); double scale=Native.DpiScale(handle);
                check(host.Right-(int)Math.Round(8*scale)==area.Right,"right dock places visible capsule exactly at work-area edge");
                rightWidget.TickDock(long.MaxValue/2,false); Pump(260);
                var root=(Grid)rightWidget.Content; var tab=(Border)root.FindName("EdgeTab");
                Point edgeRight=tab.PointToScreen(new Point(tab.ActualWidth,0));
                check(rightWidget.IsDockCollapsed&&Math.Abs(edgeRight.X-area.Right)<=1,"right collapsed tab stays at exact physical edge");
                var pixels=Capture(root); var centerPixel=new byte[4]; pixels.CopyPixels(new Int32Rect(56,30,1,1),centerPixel,4,0);
                check(centerPixel[3]==0,"collapsed immersive capsule leaves no painted reserved area");
                tab.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent=Mouse.MouseEnterEvent }); Pump(260);
                Native.RECT after; Native.GetWindowRect(handle,out after);
                check(!rightWidget.IsDockCollapsed&&Same(host,after),"right reveal animation retains native position and size");
            } finally { rightWidget.Close(); }
        }
        private static bool IsAbove(IntPtr upper,IntPtr lower)
        {
            int visited=0;
            for(var next=Native.GetWindow(lower,3);next!=IntPtr.Zero&&visited++<128;next=Native.GetWindow(next,3))
                if(next==upper) return true;
            return false;
        }
        private static ShowWindow FakeShow()
        {
            var screen=System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            var rect=new Native.RECT { Left=screen.Left,Top=screen.Top,Right=screen.Right,Bottom=screen.Bottom };
            return new ShowWindow { Handle=new IntPtr(12345),ProcessId=42,ProcessStarted=123456,ProcessName="POWERPNT",ClassName="screenClass",
                Exists=true,Reliable=true,Visible=true,Bounds=rect,MonitorBounds=rect };
        }
        private static bool Same(Native.RECT a,Native.RECT b)
        { return a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom; }
        private static void Pump(int milliseconds)
        {
            var frame=new DispatcherFrame(); var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick+=(s,e)=>{ timer.Stop(); frame.Continue=false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        private static RenderTargetBitmap Render(FrameworkElement element,Size size)
        {
            element.Measure(size); element.Arrange(new Rect(size)); element.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);
            bitmap.Render(element); return bitmap;
        }
        private static void SaveGuidePreview(WidgetWindow widget,SpotlightGuide guide,Native.RECT host,Native.RECT screen,double scale)
        {
            // Compose only app-owned visuals against a neutral background, never capture the desktop.
            var size=new Size(guide.ActualWidth,guide.ActualHeight);
            var guideImage=Capture((FrameworkElement)guide.Content);
            var widgetImage=Capture((FrameworkElement)widget.Content);
            var visual=new DrawingVisual();
            using(var draw=visual.RenderOpen()) {
                draw.DrawRectangle(new SolidColorBrush(Color.FromRgb(75,95,115)),null,new Rect(size));
                draw.DrawImage(widgetImage,new Rect((host.Left-screen.Left)/scale,(host.Top-screen.Top)/scale,widget.Width,widget.Height));
                draw.DrawImage(guideImage,new Rect(size));
            }
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var focus=Rect.Union(guide.Layout.Card,guide.Layout.Hole);
            focus.Inflate(28,28);
            focus.Intersect(new Rect(size));
            int x=(int)Math.Floor(focus.Left),y=(int)Math.Floor(focus.Top);
            var crop=new Int32Rect(x,y,(int)Math.Ceiling(focus.Right)-x,(int)Math.Ceiling(focus.Bottom)-y);
            Save(new CroppedBitmap(bitmap,crop),"spotlight-v1.4.1.png");
        }
        private static RenderTargetBitmap Capture(FrameworkElement element)
        {
            // Never remeasure or rearrange live content merely to make a preview.
            element.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);
            bitmap.Render(element); return bitmap;
        }
        private static void Save(BitmapSource bitmap,string name)
        {
            if(name.StartsWith("first-use-")||name.StartsWith("widget-immersive-")) {
                var visual=new DrawingVisual();
                using(var draw=visual.RenderOpen()) {
                    draw.DrawRectangle(Brushes.White,null,new Rect(0,0,bitmap.PixelWidth,bitmap.PixelHeight));
                    draw.DrawImage(bitmap,new Rect(0,0,bitmap.PixelWidth,bitmap.PixelHeight));
                }
                var preview=new RenderTargetBitmap(bitmap.PixelWidth,bitmap.PixelHeight,96,96,PixelFormats.Pbgra32);
                preview.Render(visual); bitmap=preview;
            }
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var output=File.Create(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(WidgetFeatureTests).Assembly.Location),name))) encoder.Save(output);
        }
    }
}
