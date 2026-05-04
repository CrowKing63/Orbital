using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace Orbital
{
    public static class PopupPlacementCalculator
    {
        public static Point CalculatePosition(
            PopupPlacementMode mode,
            PopupAnchorContext anchors,
            Rect workArea,
            Size popupSize)
        {
            Point nearCursor = new(anchors.CursorDip.X + 12, anchors.CursorDip.Y + 12);
            Point placement = mode switch
            {
                PopupPlacementMode.BottomRight => new Point(workArea.Right - popupSize.Width - 20, workArea.Bottom - popupSize.Height - 20),
                PopupPlacementMode.NearActionBar => GetNearActionBarOrFallback(anchors, nearCursor, popupSize),
                PopupPlacementMode.NearSelection => GetNearSelectionOrFallback(anchors, nearCursor, popupSize),
                PopupPlacementMode.NearCursor => nearCursor,
                _ => nearCursor,
            };

            return ClampToWorkingArea(placement, workArea, popupSize);
        }

        private static Point GetNearSelectionOrFallback(PopupAnchorContext anchors, Point fallback, Size popupSize)
        {
            if (anchors.SelectionDip is not Rect selection || selection.Width <= 0 || selection.Height <= 0)
            {
                return fallback;
            }

            double yAbove = selection.Top - popupSize.Height - 10;
            if (yAbove >= 0)
            {
                return new Point(selection.Left, yAbove);
            }

            return new Point(selection.Left, selection.Bottom + 10);
        }

        private static Point GetNearActionBarOrFallback(PopupAnchorContext anchors, Point fallback, Size popupSize)
        {
            if (anchors.ActionBarDip is not Rect actionBar || actionBar.Width <= 0 || actionBar.Height <= 0)
            {
                return fallback;
            }

            return new Point(actionBar.Left, actionBar.Bottom + 10);
        }

        public static Point ClampToWorkingArea(Point preferred, Rect workArea, Size popupSize)
        {
            double x = Math.Max(workArea.Left + 4, Math.Min(preferred.X, workArea.Right - popupSize.Width - 4));
            double y = Math.Max(workArea.Top + 4, Math.Min(preferred.Y, workArea.Bottom - popupSize.Height - 4));
            return new Point(x, y);
        }
    }
}
