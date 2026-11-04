using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace DesktopSwitcher
{
    internal static class WidgetMenu
    {
        private const string MenuXaml=@"<ContextMenu xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
            StaysOpen='False' FontFamily='Microsoft YaHei UI' FontSize='13' Foreground='#253449' SnapsToDevicePixels='True' UseLayoutRounding='True'>
          <ContextMenu.Resources>
            <Style x:Key='MenuCard' TargetType='Border'>
              <Setter Property='Background' Value='#FFFCFDFF'/><Setter Property='BorderBrush' Value='#CBD6E3'/>
              <Setter Property='BorderThickness' Value='1'/><Setter Property='CornerRadius' Value='12'/><Setter Property='Padding' Value='6'/>
            </Style>
            <Style x:Key='MenuScroll' TargetType='ScrollViewer'>
              <Setter Property='VerticalScrollBarVisibility' Value='Auto'/><Setter Property='HorizontalScrollBarVisibility' Value='Disabled'/>
              <Setter Property='Focusable' Value='False'/>
            </Style>
            <Style TargetType='Separator'>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Separator'>
                <Border Height='1' Background='#DFE7F0' Margin='8,5'/>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            <Style TargetType='MenuItem'>
              <Setter Property='Foreground' Value='#253449'/><Setter Property='MinWidth' Value='202'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>
                <Grid>
                  <Border x:Name='Row' Background='Transparent' CornerRadius='7' MinHeight='36' Padding='7,3,9,3'>
                    <Grid>
                      <Grid.ColumnDefinitions><ColumnDefinition Width='28'/><ColumnDefinition Width='*'/><ColumnDefinition Width='16'/></Grid.ColumnDefinitions>
                      <Path x:Name='Icon' Data='{TemplateBinding Tag}' Width='15' Height='15' Stretch='Uniform' Stroke='#657891' StrokeThickness='1.5'
                            StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' HorizontalAlignment='Left' Margin='3,0,0,0' VerticalAlignment='Center'/>
                      <Border x:Name='Check' Width='21' Height='21' CornerRadius='6' Background='#DCEBFF' HorizontalAlignment='Left' VerticalAlignment='Center' Visibility='Collapsed'>
                        <Path Data='M 0,4 L 4,8 L 11,0' Stroke='#2162B4' StrokeThickness='1.7' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                              StrokeLineJoin='Round' HorizontalAlignment='Center' VerticalAlignment='Center'/>
                      </Border>
                      <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
                      <Path x:Name='Chevron' Grid.Column='2' Data='M 0,0 L 4,4 L 0,8' Stroke='#657891' StrokeThickness='1.5' StrokeStartLineCap='Round'
                            StrokeEndLineCap='Round' StrokeLineJoin='Round' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/>
                    </Grid>
                  </Border>
                  <Popup x:Name='PART_Popup' AllowsTransparency='True' Focusable='False' Placement='Right' HorizontalOffset='-2' PlacementTarget='{Binding ElementName=Row}'
                         IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
                    <Border Style='{StaticResource MenuCard}'>
                      <ScrollViewer Style='{StaticResource MenuScroll}'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' KeyboardNavigation.TabNavigation='Cycle'/></ScrollViewer>
                    </Border>
                  </Popup>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property='HasItems' Value='True'><Setter TargetName='Chevron' Property='Visibility' Value='Visible'/></Trigger>
                  <Trigger Property='IsChecked' Value='True'><Setter TargetName='Icon' Property='Visibility' Value='Collapsed'/><Setter TargetName='Check' Property='Visibility' Value='Visible'/></Trigger>
                  <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Row' Property='Background' Value='#E9F2FF'/></Trigger>
                  <Trigger Property='IsSubmenuOpen' Value='True'><Setter TargetName='Row' Property='Background' Value='#E9F2FF'/></Trigger>
                  <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Row' Property='Opacity' Value='0.5'/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
          </ContextMenu.Resources>
          <ContextMenu.Template><ControlTemplate TargetType='ContextMenu'>
            <Border Style='{StaticResource MenuCard}'>
              <ScrollViewer Style='{StaticResource MenuScroll}'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' KeyboardNavigation.TabNavigation='Cycle'/></ScrollViewer>
            </Border>
          </ControlTemplate></ContextMenu.Template>
        </ContextMenu>";

        internal static ContextMenu Create() { return (ContextMenu)XamlReader.Parse(MenuXaml); }
        internal static MenuItem Item(string title,string geometry,Action action=null,Func<bool> selected=null)
        {
            var item=new MenuItem { Header=title,Tag=Geometry.Parse(geometry),IsCheckable=selected!=null };
            if(action!=null) item.Click+=(s,e)=>{ if(e.Source==item) action(); };
            if(selected!=null) item.SetValue(SelectionProperty,selected);
            return item;
        }
        private static readonly DependencyProperty SelectionProperty=DependencyProperty.RegisterAttached(
            "Selection",typeof(Func<bool>),typeof(WidgetMenu),new PropertyMetadata(null));
        internal static void Refresh(ItemsControl menu)
        {
            foreach(object child in menu.Items) {
                var item=child as MenuItem;
                if(item==null) continue;
                var selected=(Func<bool>)item.GetValue(SelectionProperty);
                if(selected!=null) item.IsChecked=selected();
                if(item.HasItems) Refresh(item);
            }
        }
        internal static bool ContainsPointer(ItemsControl menu,Native.POINT pointer)
        {
            var source=PresentationSource.FromVisual(menu) as HwndSource;
            if(ContainsPointer(source,pointer)) return true;
            foreach(object child in menu.Items) {
                var item=child as MenuItem;
                if(item==null||!item.IsSubmenuOpen||item.Template==null) continue;
                var popup=item.Template.FindName("PART_Popup",item) as Popup;
                if(popup!=null&&popup.Child!=null&&ContainsPointer(PresentationSource.FromVisual(popup.Child) as HwndSource,pointer)) return true;
            }
            return false;
        }
        private static bool ContainsPointer(HwndSource source,Native.POINT pointer)
        { Native.RECT bounds; return source!=null&&Native.GetWindowRect(source.Handle,out bounds)&&Docking.Contains(bounds,pointer); }
    }
}
