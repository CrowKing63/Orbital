using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace Orbital
{
    public enum PopupPlacementMode
    {
        BottomRight,
        NearCursor,
        NearSelection,
        NearActionBar,
    }

    public readonly record struct PopupAnchorContext(
        Point CursorDip,
        Rect? SelectionDip,
        Rect? ActionBarDip);
}
