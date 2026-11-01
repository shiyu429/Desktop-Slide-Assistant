using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DesktopSwitcher
{
    internal sealed class GuideLayout
    {
        internal Rect Hole,Card;
        internal bool CardAbove;
        internal static GuideLayout Calculate(Size screen,Rect capsule,Size card)
        {
            var hole=capsule; hole.Inflate(7,7);
            hole.Intersect(new Rect(0,0,screen.Width,screen.Height));
            double width=Math.Min(card.Width,Math.Max(1,screen.Width-24));
            double height=Math.Min(card.Height,Math.Max(1,screen.Height-24));
            double x=Math.Max(12,Math.Min(screen.Width-width-12,capsule.Right-width));
            double y=hole.Top-height-30;
            bool above=y>=12;
            if(!above) y=hole.Bottom+30;
            y=Math.Max(12,Math.Min(screen.Height-height-12,y));
            return new GuideLayout { Hole=hole,Card=new Rect(x,y,width,height),CardAbove=above };
        }
        internal Geometry Mask(Size screen)
        {
            return new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0,0,screen.Width,screen.Height)),new RectangleGeometry(Hole,19,19));
        }
    }

    internal sealed class SpotlightGuide : Window
    {
        private readonly Canvas canvas=new Canvas { ClipToBounds=true };
        private readonly Path mask=new Path { Fill=new SolidColorBrush(Color.FromArgb(160,0,0,0)) };
        private readonly Path ring=new Path { Stroke=new SolidColorBrush(Color.FromRgb(164,211,255)),StrokeThickness=2,IsHitTestVisible=false };
        private readonly Path connector=new Path { Stroke=new SolidColorBrush(Color.FromRgb(164,211,255)),StrokeThickness=2,IsHitTestVisible=false };
        private readonly Border card;
        private readonly Action dismiss;
        private Native.RECT screenBounds,capsuleBounds;
        private IntPtr hwnd;
        private double scale=1;
        internal GuideLayout Layout { get; private set; }
        internal Button SuppressButton { get; private set; }
        internal SpotlightGuide(Window owner,Action dismiss,Action suppress)
        {
            this.dismiss=dismiss;
            Title="课件翻页引导";
            WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true; Background=Brushes.Transparent;
            ShowInTaskbar=true; ShowActivated=false; Topmost=true; WindowStartupLocation=WindowStartupLocation.Manual;
            Owner=owner;
            var stack=new StackPanel();
            stack.Children.Add(new TextBlock { Text="在这里也可以翻页",FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=18,
                Foreground=Brushes.White,Margin=new Thickness(0,0,0,12) });
            SuppressButton=new Button { Content="不再提醒",Focusable=false,Cursor=Cursors.Hand,
                FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=13,Padding=new Thickness(14,5,14,5),
                HorizontalAlignment=HorizontalAlignment.Right,Background=new SolidColorBrush(Color.FromRgb(61,86,112)),
                Foreground=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(97,130,164)) };
            System.Windows.Automation.AutomationProperties.SetName(SuppressButton,"不再提醒");
            SuppressButton.Click+=(s,e)=>{ e.Handled=true; suppress(); };
            stack.Children.Add(SuppressButton);
            card=new Border { Child=stack,Padding=new Thickness(18,14,18,14),CornerRadius=new CornerRadius(14),
                Background=new SolidColorBrush(Color.FromArgb(248,31,38,47)),BorderBrush=new SolidColorBrush(Color.FromArgb(90,188,221,255)),BorderThickness=new Thickness(1) };
            canvas.Children.Add(mask); canvas.Children.Add(ring); canvas.Children.Add(connector); canvas.Children.Add(card); Content=canvas;
            canvas.PreviewMouseDown+=(s,e)=>{
                if(DismissAt(e.GetPosition(canvas),e.OriginalSource as DependencyObject)) e.Handled=true;
            };
            SourceInitialized+=(s,e)=>{
                hwnd=new WindowInteropHelper(this).Handle;
                Native.SetWindowLong(hwnd,-20,(Native.GetWindowLong(hwnd,-20)&~0x00040080)|0x08000000);
                HwndSource.FromHwnd(hwnd).AddHook(Hook);
                SetBounds();
            };
            SizeChanged+=(s,e)=>UpdateGeometry();
        }
        internal bool DismissAt(Point position,DependencyObject source)
        {
            while(source!=null) {
                if(source==SuppressButton) return false;
                source=VisualTreeHelper.GetParent(source);
            }
            if(Layout!=null&&Layout.Hole.Contains(position)) return false;
            dismiss(); return true;
        }
        private IntPtr Hook(IntPtr handle,int message,IntPtr w,IntPtr l,ref bool handled)
        {
            if(message==0x21) { handled=true; return new IntPtr(3); }
            if(message==0x02E0) Dispatcher.BeginInvoke(new Action(()=>{ if(IsVisible) { SetBounds(); UpdateGeometry(); } }));
            return IntPtr.Zero;
        }
        internal void ShowFor(Native.RECT screen,Native.RECT capsule,double dpiScale)
        {
            bool changed=!Same(screenBounds,screen)||!Same(capsuleBounds,capsule)||scale!=dpiScale;
            screenBounds=screen; capsuleBounds=capsule; scale=dpiScale;
            if(IsVisible&&!changed) return;
            if(!IsVisible) {
                Width=(screen.Right-screen.Left)/scale; Height=(screen.Bottom-screen.Top)/scale;
                Left=screen.Left/scale; Top=screen.Top/scale;
                UpdateGeometry(); Show();
            }
            SetBounds(); UpdateGeometry();
        }
        private void SetBounds()
        {
            if(hwnd!=IntPtr.Zero) Native.SetWindowPos(hwnd,new IntPtr(-1),screenBounds.Left,screenBounds.Top,
                screenBounds.Right-screenBounds.Left,screenBounds.Bottom-screenBounds.Top,0x0010);
        }
        private void UpdateGeometry()
        {
            if(screenBounds.Right<=screenBounds.Left) return;
            double dpi=hwnd==IntPtr.Zero ? scale : Native.DpiScale(hwnd);
            var screen=new Size((screenBounds.Right-screenBounds.Left)/dpi,(screenBounds.Bottom-screenBounds.Top)/dpi);
            var capsule=new Rect((capsuleBounds.Left-screenBounds.Left)/dpi,(capsuleBounds.Top-screenBounds.Top)/dpi,
                (capsuleBounds.Right-capsuleBounds.Left)/dpi,(capsuleBounds.Bottom-capsuleBounds.Top)/dpi);
            card.Measure(new Size(Math.Max(1,screen.Width-24),double.PositiveInfinity));
            Layout=GuideLayout.Calculate(screen,capsule,card.DesiredSize);
            mask.Data=Layout.Mask(screen); ring.Data=new RectangleGeometry(Layout.Hole,19,19);
            Canvas.SetLeft(card,Layout.Card.Left); Canvas.SetTop(card,Layout.Card.Top);
            card.Width=Layout.Card.Width;
            double arrowX=Math.Max(Layout.Hole.Left+10,Math.Min(Layout.Hole.Right-10,Layout.Card.Left+Layout.Card.Width/2));
            double startY=Layout.CardAbove ? Layout.Card.Bottom+5 : Layout.Card.Top-5;
            double endY=Layout.CardAbove ? Layout.Hole.Top-5 : Layout.Hole.Bottom+5;
            double direction=Layout.CardAbove ? -1 : 1;
            var geometry=new StreamGeometry();
            using(var draw=geometry.Open()) {
                draw.BeginFigure(new Point(Layout.Card.Left+Layout.Card.Width/2,startY),false,false);
                draw.LineTo(new Point(arrowX,endY),true,false);
                draw.LineTo(new Point(arrowX-5,endY+direction*7),true,false);
                draw.BeginFigure(new Point(arrowX,endY),false,false);
                draw.LineTo(new Point(arrowX+5,endY+direction*7),true,false);
            }
            connector.Data=geometry;
        }
        private static bool Same(Native.RECT a,Native.RECT b)
        { return a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom; }
    }
}
