using System;

namespace DesktopSwitcher
{
    // Complements WPF's outside-capture event on nonactivating overlay windows.
    // No hooks, foreground activation, input injection or click recording.
    internal sealed class MenuDismissal
    {
        private int previousButtons;
        internal void Open(int buttons) { previousButtons=buttons; }
        internal bool Observe(Native.RECT menu,Native.POINT pointer,int buttons,bool foregroundChanged)
        {
            bool newPress=(buttons&~previousButtons)!=0; previousButtons=buttons;
            return foregroundChanged||(newPress&&!Docking.Contains(menu,pointer));
        }
    }
}
