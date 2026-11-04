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
        internal const double ScreenInset=16;
        private const double Gap=24;
        internal Rect Hole,Card;
        internal Geometry Stem;
        internal static GuideLayout Calculate(Size screen,Rect capsule,Size card)
        {
            // Let the window clip the opening at a screen edge. Clamping the rectangle first
            // would move its rounded corners inward and dim part of the button at that edge.
            var hole=capsule;
            hole.Inflate(10,10);
            double width=Math.Min(card.Width,Math.Max(0,screen.Width-2*ScreenInset));
            double height=Math.Min(card.Height,Math.Max(0,screen.Height-2*ScreenInset));
            double top=hole.Top-ScreenInset, bottom=screen.Height-ScreenInset-hole.Bottom;
            double left=hole.Left-ScreenInset, right=screen.Width-ScreenInset-hole.Right;
            var layout=new GuideLayout { Hole=hole };

            if(Math.Max(top,bottom)<height+Gap&&Math.Max(left,right)>=width+Gap) {
                bool placeLeft=left>=right;
                double x=placeLeft ? hole.Left-Gap-width : hole.Right+Gap;
                double y=Fit(capsule.Top+(capsule.Height-height)/2,screen.Height,height);
                layout.Card=new Rect(x,y,width,height);
                double axis=ClampToCard(capsule.Top+capsule.Height/2,y,height);
                layout.Stem=DrawStem(new Point(placeLeft ? layout.Card.Right+5 : x-5,axis),
                    new Point(placeLeft ? hole.Left-7 : hole.Right+7,axis));
                return layout;
            }

            bool above=top>=height+Gap||(bottom<height+Gap&&top>=bottom);
            double room=Math.Max(0,above ? top : bottom);
            height=Math.Min(height,Math.Max(0,room-6));
            double gap=Math.Min(Gap,room-height);
            double cardX=Fit(capsule.Left+(capsule.Width-width)/2,screen.Width,width);
            double cardY=above ? hole.Top-gap-height : hole.Bottom+gap;
            layout.Card=new Rect(cardX,cardY,width,height);
            double anchor=ClampToCard(capsule.Left+capsule.Width/2,cardX,width);
            layout.Stem=gap>=18 ? DrawStem(new Point(anchor,above ? layout.Card.Bottom+5 : cardY-5),
                new Point(anchor,above ? hole.Top-7 : hole.Bottom+7)) : Geometry.Empty;
            return layout;
        }
        private static double Fit(double origin,double available,double length)
        { return Math.Max(ScreenInset,Math.Min(available-ScreenInset-length,origin)); }
        private static double ClampToCard(double point,double origin,double length)
        {
            double inset=Math.Min(14,length/2);
            return Math.Max(origin+inset,Math.Min(origin+length-inset,point));
        }
        private static Geometry DrawStem(Point start,Point end)
        {
            return new GeometryGroup { Children=new GeometryCollection {
                new LineGeometry(start,end),new EllipseGeometry(end,2.2,2.2)
            }};
        }
        internal RectangleGeometry Opening { get { return new RectangleGeometry(Hole,24,24); } }
        internal Geometry Mask(Size screen)
        {
            return new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0,0,screen.Width,screen.Height)),Opening);
        }
    }

    internal sealed class SpotlightGuide : Window
    {
        private const double CardWidth=224;
        private readonly Canvas canvas=new Canvas { ClipToBounds=true };
        private readonly Path mask=new Path { Fill=new SolidColorBrush(Color.FromArgb(161,9,17,32)) };
        private readonly Path halo=new Path { Stroke=new SolidColorBrush(Color.FromArgb(38,117,184,255)),StrokeThickness=6,IsHitTestVisible=false };
        private readonly Path ring=new Path { Stroke=new SolidColorBrush(Color.FromArgb(198,153,204,255)),StrokeThickness=1.4,IsHitTestVisible=false };
        private readonly Path connector=new Path {
            Stroke=new SolidColorBrush(Color.FromRgb(153,204,255)),Fill=new SolidColorBrush(Color.FromRgb(153,204,255)),
            StrokeThickness=1.4,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,IsHitTestVisible=false
        };
        private readonly Border card;
        private readonly Action dismiss;
        private Native.RECT screenBounds,capsuleBounds;
        private IntPtr hwnd;
        private double scale=1;
        internal GuideLayout Layout { get; private set; }
        internal Button SuppressButton { get; private set; }
        private const string CardXaml=@"<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
            Padding='17,14' CornerRadius='20' BorderThickness='1' BorderBrush='#95637EA4' ClipToBounds='True'>
          <Border.Background><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'>
            <GradientStop Offset='0' Color='#FF253750'/><GradientStop Offset='1' Color='#FF131F32'/>
          </LinearGradientBrush></Border.Background>
          <StackPanel TextElement.FontFamily='Microsoft YaHei UI'>
            <StackPanel Orientation='Horizontal'>
              <Ellipse Width='6' Height='6' Fill='#9BCBFF' VerticalAlignment='Center' Margin='0,0,7,0'/>
              <TextBlock Text='课件翻页' FontSize='11' FontWeight='SemiBold' Foreground='#BCD6F6'/>
            </StackPanel>
            <TextBlock Text='点击此处翻页' FontSize='17' FontWeight='SemiBold' Foreground='#F6FAFF' Margin='0,5,0,12' TextWrapping='Wrap'/>
            <Button x:Name='Suppress' Content='不再提醒' AutomationProperties.Name='不再提醒' Focusable='False' Cursor='Hand'
                FontSize='12' Foreground='#E9F3FF' Padding='12,5' HorizontalAlignment='Right' MinHeight='29'>
              <Button.Template><ControlTemplate TargetType='Button'>
                <Border x:Name='Pill' CornerRadius='11' Background='#30435E' BorderBrush='#55759C' BorderThickness='1' Padding='{TemplateBinding Padding}'>
                  <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Pill' Property='Background' Value='#405B7D'/></Trigger>
                  <Trigger Property='IsPressed' Value='True'><Setter TargetName='Pill' Property='Background' Value='#253851'/></Trigger>
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
            canvas.Children.Add(mask); canvas.Children.Add(halo); canvas.Children.Add(ring); canvas.Children.Add(connector); canvas.Children.Add(card); Content=canvas;
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
            card.Width=Math.Min(CardWidth,Math.Max(0,screen.Width-2*GuideLayout.ScreenInset));
            card.ClearValue(HeightProperty);
            card.Measure(new Size(card.Width,double.PositiveInfinity));
            Layout=GuideLayout.Calculate(screen,capsule,card.DesiredSize);
            mask.Data=Layout.Mask(screen); ring.Data=halo.Data=Layout.Opening;
            connector.Data=Layout.Stem;
            Canvas.SetLeft(card,Layout.Card.Left); Canvas.SetTop(card,Layout.Card.Top);
            card.Height=Layout.Card.Height;
        }
        private static bool Same(Native.RECT a,Native.RECT b)
        { return a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom; }
    }
}
