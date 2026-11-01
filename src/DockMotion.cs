using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopSwitcher
{
    internal sealed class DockMotion
    {
        private readonly Border capsule,tab;
        private readonly TranslateTransform slide=new TranslateTransform();
        private bool initialized,lastCollapsed;
        private DockSide lastSide;
        private int revision;
        internal double Offset { get { return slide.X; } }
        internal DockMotion(Border capsule,Border tab)
        { this.capsule=capsule; this.tab=tab; capsule.RenderTransform=slide; }
        internal void Update(bool collapsed,DockSide side,bool animate)
        {
            if(initialized&&collapsed==lastCollapsed&&side==lastSide&&animate) return;
            animate=animate&&initialized&&side==lastSide;
            initialized=true; lastCollapsed=collapsed; lastSide=side; int token=++revision;
            bool right=side==DockSide.Right;
            tab.HorizontalAlignment=right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            tab.Margin=right ? new Thickness(0,0,8,0) : new Thickness(8,0,0,0);
            tab.CornerRadius=right ? new CornerRadius(4,0,0,4) : new CornerRadius(0,4,4,0);
            capsule.Visibility=tab.Visibility=Visibility.Visible;
            capsule.IsHitTestVisible=!collapsed; tab.IsHitTestVisible=collapsed;
            Animate(slide,TranslateTransform.XProperty,collapsed ? (right ? 100 : -100) : 0,animate,token,null);
            Animate(tab,UIElement.OpacityProperty,collapsed ? 1 : 0,animate,token,null);
            Animate(capsule,UIElement.OpacityProperty,collapsed ? 0 : 1,animate,token,()=>{
                if(token!=revision) return;
                capsule.Visibility=collapsed ? Visibility.Hidden : Visibility.Visible;
                tab.Visibility=collapsed ? Visibility.Visible : Visibility.Hidden;
            });
        }
        private void Animate(Animatable target,DependencyProperty property,double value,bool animate,int token,Action complete)
        {
            double from=(double)target.GetValue(property);
            target.BeginAnimation(property,null); target.SetValue(property,animate ? from : value);
            if(!animate) { if(complete!=null) complete(); return; }
            var animation=new DoubleAnimation(from,value,TimeSpan.FromMilliseconds(200)) {
                EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut },FillBehavior=FillBehavior.HoldEnd };
            animation.Completed+=(s,e)=>{
                if(token!=revision) return;
                target.SetValue(property,value); target.BeginAnimation(property,null);
                if(complete!=null) complete();
            };
            target.BeginAnimation(property,animation);
        }
        private void Animate(UIElement target,DependencyProperty property,double value,bool animate,int token,Action complete)
        {
            double from=(double)target.GetValue(property);
            target.BeginAnimation(property,null); target.SetValue(property,animate ? from : value);
            if(!animate) { if(complete!=null) complete(); return; }
            var animation=new DoubleAnimation(from,value,TimeSpan.FromMilliseconds(200)) {
                EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut },FillBehavior=FillBehavior.HoldEnd };
            animation.Completed+=(s,e)=>{
                if(token!=revision) return;
                target.SetValue(property,value); target.BeginAnimation(property,null);
                if(complete!=null) complete();
            };
            target.BeginAnimation(property,animation);
        }
    }
}
