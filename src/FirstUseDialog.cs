using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace DesktopSwitcher
{
    internal sealed class FirstUseDialog : Window
    {
        internal RunMode SelectedMode { get; private set; }
        private const string ContentXaml=@"<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Margin='26' Background='White'>
          <Grid.Resources>
            <Style TargetType='Button'>
              <Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Background' Value='#F2F6FA'/>
              <Setter Property='BorderBrush' Value='#DCE5EE'/><Setter Property='Padding' Value='18,14'/><Setter Property='Cursor' Value='Hand'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
                <Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='12' Padding='{TemplateBinding Padding}'>
                  <ContentPresenter/>
                </Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#E3EFFB'/></Trigger>
                <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#5094CC'/></Trigger></ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
          </Grid.Resources>
          <StackPanel>
            <TextBlock x:Name='Heading' Text='请选择使用模式' FontSize='22' FontWeight='SemiBold' Foreground='#182A3C'/>
            <TextBlock x:Name='Description' Text='先选择常用模式，之后可在右键或托盘菜单中切换。' Margin='0,8,0,20' FontSize='13' TextWrapping='Wrap' Foreground='#65778A'/>
            <Button x:Name='ChooseDesktop' Margin='0,0,0,12' AutomationProperties.Name='选择桌面切屏'>
              <StackPanel><TextBlock Text='桌面切屏' FontSize='17' FontWeight='SemiBold' Foreground='#21384F'/>
                <TextBlock Text='左右切换虚拟桌面，到末尾时新建桌面。' Margin='0,6,0,0' TextWrapping='Wrap' FontSize='13' Foreground='#65778A'/></StackPanel>
            </Button>
            <Button x:Name='ChooseCourseware' Margin='0,0,0,12' AutomationProperties.Name='选择课件翻页'>
              <StackPanel><TextBlock Text='课件翻页' FontSize='17' FontWeight='SemiBold' Foreground='#21384F'/>
                <TextBlock Text='只在幻灯片放映时显示，用左右按钮翻页。' Margin='0,6,0,0' TextWrapping='Wrap' FontSize='13' Foreground='#65778A'/>
                <TextBlock Text='选择后会询问是否允许开机启动，可拒绝。' Margin='0,6,0,0' TextWrapping='Wrap' FontSize='12' Foreground='#507DA4'/></StackPanel>
            </Button>
            <Button x:Name='ChooseAutomatic' AutomationProperties.Name='选择自动切换'>
              <StackPanel><TextBlock Text='自动切换' FontSize='17' FontWeight='SemiBold' Foreground='#21384F'/>
                <TextBlock Text='平时切换桌面，全屏放映时自动改为翻页。' Margin='0,6,0,0' TextWrapping='Wrap' FontSize='13' Foreground='#65778A'/></StackPanel>
            </Button>
          </StackPanel>
        </Grid>";
        internal static Grid CreateContent() { return (Grid)XamlReader.Parse(ContentXaml); }
        internal bool ExitRequested { get; private set; }
        internal FirstUseDialog(bool firstUse=true)
        {
            DialogAppearance.Apply(this,"请选择使用模式",452);
            var root=CreateContent(); Content=root;
            if(!firstUse) {
                Title="程序正在运行 · 桌面切屏/课件翻页助手";
                ((TextBlock)root.FindName("Heading")).Text="程序已在运行";
                ((TextBlock)root.FindName("Description")).Text="课件翻页在未放映时隐藏悬浮按钮。可切换模式或退出程序。";
                var exit=new Button { Content="退出程序",Margin=new Thickness(0,12,0,0),Padding=new Thickness(12,8,12,8) };
                exit.Click+=(s,e)=>{ ExitRequested=true; DialogResult=true; };
                ((StackPanel)root.Children[0]).Children.Add(exit);
            }
            ((Button)root.FindName("ChooseDesktop")).Click+=(s,e)=>Choose(RunMode.Desktop);
            ((Button)root.FindName("ChooseCourseware")).Click+=(s,e)=>Choose(RunMode.Courseware);
            ((Button)root.FindName("ChooseAutomatic")).Click+=(s,e)=>Choose(RunMode.Automatic);
        }
        private void Choose(RunMode mode) { SelectedMode=mode; DialogResult=true; }
    }
    internal static class DialogAppearance
    {
        internal static void Apply(Window window,string title,double width)
        {
            window.Title=title; window.Width=width; window.SizeToContent=SizeToContent.Height;
            window.ResizeMode=ResizeMode.NoResize; window.WindowStartupLocation=WindowStartupLocation.CenterScreen;
            window.FontFamily=new System.Windows.Media.FontFamily("Microsoft YaHei UI");
            window.Background=System.Windows.Media.Brushes.White;
        }
    }
    internal sealed class StartupConsentDialog : Window
    {
        internal static Grid CreateContent()
        {
            var root=(Grid)XamlReader.Parse(@"<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Margin='26' Background='White'>
              <StackPanel>
                <TextBlock Text='让翻页随时就绪' FontSize='22' FontWeight='SemiBold' Foreground='#182A3C'/>
                <TextBlock Text='是否允许程序在你登录 Windows 时自动启动？' FontSize='13' TextWrapping='Wrap' Foreground='#65778A' Margin='0,10,0,20'/>
                <Border Background='#F2F6FA' BorderBrush='#DCE5EE' BorderThickness='1' CornerRadius='12' Padding='18,16'>
                  <StackPanel><TextBlock Text='课件翻页' FontSize='17' FontWeight='SemiBold' Foreground='#21384F'/>
                    <TextBlock Text='仅在 PowerPoint / WPS 全屏放映时显示翻页按钮，平时保留托盘入口。' FontSize='13' TextWrapping='Wrap' Foreground='#65778A' Margin='0,8,0,0'/>
                    <TextBlock Text='不需要管理员权限，也可以稍后在菜单中开启。' FontSize='12' TextWrapping='Wrap' Foreground='#507DA4' Margin='0,10,0,0'/>
                  </StackPanel>
                </Border>
                <Grid Margin='0,22,0,0'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='12'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
                  <Button x:Name='DeclineStartup' IsCancel='True' IsDefault='True' Padding='12,12'><TextBlock Text='暂不开启' HorizontalAlignment='Center'/></Button>
                  <Button x:Name='AllowStartup' Grid.Column='2' Background='#E3EFFB' BorderBrush='#5094CC' Padding='12,12'><TextBlock Text='允许开机启动' HorizontalAlignment='Center'/></Button>
                </Grid>
              </StackPanel>
            </Grid>");
            root.Resources=FirstUseDialog.CreateContent().Resources;
            return root;
        }
        internal StartupConsentDialog(Window owner)
        {
            DialogAppearance.Apply(this,"开机启动 · 桌面切屏/课件翻页助手",452);
            if(owner!=null&&owner.IsVisible) { Owner=owner; WindowStartupLocation=WindowStartupLocation.CenterOwner; Topmost=owner.Topmost; }
            var root=CreateContent(); Content=root;
            ((Button)root.FindName("AllowStartup")).Click+=(s,e)=>DialogResult=true;
            ((Button)root.FindName("DeclineStartup")).Click+=(s,e)=>DialogResult=false;
        }
    }
    internal static class StartupConsent
    {
        internal static void ApplyChoice(Settings settings,bool alreadyEnabled,Func<bool> ask,Action enable)
        {
            if(settings.Mode!=RunMode.Courseware||settings.CoursewareStartupAsked) return;
            bool allowed=!alreadyEnabled&&ask();
            settings.CoursewareStartupAsked=true;
            if(allowed) enable();
        }
        internal static void Request(Settings settings,Window owner)
        {
            if(settings.Mode!=RunMode.Courseware||settings.CoursewareStartupAsked) return;
            bool alreadyEnabled=false;
            try { alreadyEnabled=Settings.AutoStart; } catch { }
            ApplyChoice(settings,alreadyEnabled,()=>new StartupConsentDialog(owner).ShowDialog()==true,()=>{
                try { Settings.AutoStart=true; }
                catch(Exception error) { MessageBox.Show("未能开启开机启动，可稍后在菜单中重试："+error.Message,"桌面切屏/课件翻页助手",MessageBoxButton.OK,MessageBoxImage.Information); }
            });
        }
    }
}
