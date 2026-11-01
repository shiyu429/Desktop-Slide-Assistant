using System;

namespace DesktopSwitcher
{
    // A fixed-size transparent host keeps the entire drag range available. Only
    // its visible content changes between the capsule and a narrow edge tab.
    internal sealed class Docking
    {
        internal DockSide Side { get; private set; }
        internal bool Collapsed { get; private set; }
        private long lastInteraction;
        internal void SetSide(DockSide side,long now)
        { Side=side; Expand(now); }
        internal void Expand(long now) { Collapsed=false; lastInteraction=now; }
        internal void Tick(long now,bool pointerInside,bool mayCollapse)
        {
            if(Side==DockSide.None||!mayCollapse) { Expand(now); return; }
            if(pointerInside) { Expand(now); return; }
            if(now-lastInteraction>=900) Collapsed=true;
        }
        internal static DockSide Detect(Native.RECT window,Native.RECT work,double scale)
        {
            // Only actual edge contact (two physical pixels of tolerance) docks.
            int threshold=2,inset=(int)Math.Round(8*scale);
            int left=window.Left+inset-work.Left,right=work.Right-window.Right+inset;
            if(left<=threshold&&left<=right) return DockSide.Left;
            return right<=threshold ? DockSide.Right : DockSide.None;
        }
        internal static Native.RECT Snap(Native.RECT window,Native.RECT work,DockSide side,double scale)
        {
            int width=window.Right-window.Left,height=window.Bottom-window.Top;
            int inset=(int)Math.Round(8*scale);
            int x=side==DockSide.Left ? work.Left-inset : side==DockSide.Right ? work.Right-width+inset : window.Left;
            int y=WidgetWindow.Clamp(window.Top,work.Top-inset,Math.Max(work.Top-inset,work.Bottom-height+inset));
            return new Native.RECT { Left=x,Top=y,Right=x+width,Bottom=y+height };
        }
        internal static Native.RECT Tab(Native.RECT window,DockSide side,double scale)
        {
            int width=Math.Max(1,(int)Math.Round(6*scale));
            int height=(int)Math.Round(32*scale),top=(window.Top+window.Bottom-height)/2;
            int inset=(int)Math.Round(8*scale);
            int x=side==DockSide.Left ? window.Left+inset : window.Right-inset-width;
            return new Native.RECT { Left=x,Top=top,Right=x+width,Bottom=top+height };
        }
        internal static bool Contains(Native.RECT rect,Native.POINT point)
        { return point.X>=rect.Left&&point.X<rect.Right&&point.Y>=rect.Top&&point.Y<rect.Bottom; }
    }
}
