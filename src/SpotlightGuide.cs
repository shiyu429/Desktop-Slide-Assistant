using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DesktopSwitcher
{
    internal sealed class GuideLayout
    {
        internal const double ScreenInset=12;
        private const double Gap=30;
        internal Rect Hole,Card;
        internal Geometry Arrow;
        internal static GuideLayout Calculate(Size screen,Rect capsule,Size card)
        {
            // Let the window clip the opening at a screen edge. Clamping the rectangle first
            // would move its rounded corners inward and dim part of the button at that edge.
            var hole=capsule;
            hole.Inflate(7,7);
            var visible=Rect.Intersect(capsule,new Rect(screen));
            bool hasVisibleCapsule=!visible.IsEmpty&&visible.Width>0&&visible.Height>0;
            if(!hasVisibleCapsule) visible=capsule;
            double width=Math.Min(card.Width,Math.Max(0,screen.Width-2*ScreenInset));
            double height=Math.Min(card.Height,Math.Max(0,screen.Height-2*ScreenInset));
            double top=hole.Top-ScreenInset, bottom=screen.Height-ScreenInset-hole.Bottom;
            double left=hole.Left-ScreenInset, right=screen.Width-ScreenInset-hole.Right;
            var layout=new GuideLayout { Hole=hole };

            if(Math.Max(top,bottom)<height+Gap&&Math.Max(left,right)>=width+Gap) {
                bool placeLeft=left>=right;
                double x=placeLeft ? hole.Left-Gap-width : hole.Right+Gap;
                double y=Fit(visible.Top+(visible.Height-height)/2,screen.Height,height);
                layout.Card=new Rect(x,y,width,height);
                double targetY=ArrowAxis(visible.Top,visible.Height,y,height);
                layout.Arrow=hasVisibleCapsule ? DrawArrow(new Point(placeLeft ? layout.Card.Right+5 : x-5,ClampToCard(targetY,y,height)),
                    new Point(placeLeft ? hole.Left-5 : hole.Right+5,targetY)) : Geometry.Empty;
                return layout;
            }

            bool above=top>=height+Gap||(bottom<height+Gap&&top>=bottom);
            double room=Math.Max(0,above ? top : bottom);
            height=Math.Min(height,Math.Max(0,room-6));
            double gap=Math.Min(Gap,room-height);
            double cardX=Fit(visible.Left+(visible.Width-width)/2,screen.Width,width);
            double cardY=above ? hole.Top-gap-height : hole.Bottom+gap;
            layout.Card=new Rect(cardX,cardY,width,height);
            double tipX=ArrowAxis(visible.Left,visible.Width,cardX,width);
            layout.Arrow=hasVisibleCapsule&&gap>=18 ? DrawArrow(new Point(ClampToCard(tipX,cardX,width),above ? layout.Card.Bottom+5 : cardY-5),
                new Point(tipX,above ? hole.Top-5 : hole.Bottom+5)) : Geometry.Empty;
            return layout;
        }
        private static double Fit(double origin,double available,double length)
        { return Math.Max(ScreenInset,Math.Min(available-ScreenInset-length,origin)); }
        private static double ClampToCard(double point,double origin,double length)
        {
            double inset=Math.Min(14,length/2);
            return Math.Max(origin+inset,Math.Min(origin+length-inset,point));
        }
        private static double ArrowAxis(double visibleStart,double visibleLength,double cardStart,double cardLength)
        {
            double inset=Math.Min(7,visibleLength/2);
            double axis=ClampToCard(visibleStart+visibleLength/2,cardStart,cardLength);
            return Math.Max(visibleStart+inset,Math.Min(visibleStart+visibleLength-inset,axis));
        }
        private static Geometry DrawArrow(Point start,Point tip)
        {
            var direction=tip-start;
            direction.Normalize();
            var head=tip-7*direction;
            var wing=5*new Vector(-direction.Y,direction.X);
            var geometry=new StreamGeometry();
            using(var draw=geometry.Open()) {
                draw.BeginFigure(start,false,false);
                draw.LineTo(tip,true,false);
                draw.BeginFigure(head-wing,false,false);
                draw.LineTo(tip,true,false);
                draw.LineTo(head+wing,true,false);
            }
            return geometry;
        }
        internal RectangleGeometry Opening { get { return new RectangleGeometry(Hole,19,19); } }
        internal Geometry Mask(Size screen)
        {
            return new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0,0,screen.Width,screen.Height)),Opening);
        }
    }

    internal sealed class SpotlightGuide : Window
    {
        private readonly Canvas canvas=new Canvas { ClipToBounds=true };
        private readonly Path mask=new Path { Fill=new SolidColorBrush(Color.FromArgb(160,0,0,0)) };
        private readonly Path ring=new Path { Stroke=new SolidColorBrush(Color.FromRgb(164,211,255)),StrokeThickness=2,IsHitTestVisible=false };
        private readonly Path connector=new Path {
            Stroke=new SolidColorBrush(Color.FromRgb(164,211,255)),StrokeThickness=2,
            StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,
            StrokeLineJoin=PenLineJoin.Round,IsHitTestVisible=false
        };
        private readonly Border card;
        private readonly Action dismiss;
        private Native.RECT screenBounds,capsuleBounds;
        private IntPtr hwnd;
        private double scale=1;
        internal GuideLayout Layout { get; private set; }
        internal Button SuppressButton { get; private set; }
        private const string CardXaml=@"<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
            Padding='22,16' CornerRadius='14' Background='#F81F262F' BorderThickness='1' BorderBrush='#5ABCDDFF' ClipToBounds='True'>
          <StackPanel TextElement.FontFamily='Microsoft YaHei UI'>
            <TextBlock Text='点击此处翻页' FontSize='19' Foreground='White' Margin='0,0,0,16' TextWrapping='Wrap'/>
            <Button x:Name='Suppress' Content='不再提醒' AutomationProperties.Name='不再提醒' Focusable='False' Cursor='Hand'
                FontSize='13' Foreground='White' Padding='14,5' HorizontalAlignment='Right'>
              <Button.Template><ControlTemplate TargetType='Button'>
                <Border x:Name='ButtonSurface' CornerRadius='8' Background='#3D5670' BorderBrush='#6182A4' BorderThickness='1' Padding='{TemplateBinding Padding}'>
                  <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ButtonSurface' Property='Background' Value='#4A6583'/></Trigger>
                  <Trigger Property='IsPressed' Value='True'><Setter TargetName='ButtonSurface' Property='Background' Value='#304862'/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Button.Template>
            </Button>
          </StackPanel>
        </Border>";
        internal SpotlightGuide(Window owner,Action dismiss,Action suppress)
        {
            this.dismiss=dismiss;
            Title="课件翻页引导";
            WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true; Background=Brushes.Transparent;
            ShowInTaskbar=true; ShowActivated=false; Topmost=true; WindowStartupLocation=WindowStartupLocation.Manual;
            Owner=owner;
            card=(Border)XamlReader.Parse(CardXaml);
            SuppressButton=(Button)card.FindName("Suppress");
            SuppressButton.Click+=(s,e)=>{ e.Handled=true; suppress(); };
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
            if(Layout!=null&&Layout.Opening.FillContains(position)) return false;
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
            card.ClearValue(WidthProperty);
            card.ClearValue(HeightProperty);
            card.Measure(new Size(Math.Max(0,screen.Width-2*GuideLayout.ScreenInset),double.PositiveInfinity));
            Layout=GuideLayout.Calculate(screen,capsule,card.DesiredSize);
            mask.Data=Layout.Mask(screen); ring.Data=Layout.Opening;
            connector.Data=Layout.Arrow;
            Canvas.SetLeft(card,Layout.Card.Left); Canvas.SetTop(card,Layout.Card.Top);
            card.Width=Layout.Card.Width; card.Height=Layout.Card.Height;
        }
        private static bool Same(Native.RECT a,Native.RECT b)
        { return a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom; }
    }
}
