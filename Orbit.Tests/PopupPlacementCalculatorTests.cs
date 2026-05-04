using System.Windows;
using Xunit;

namespace Orbital.Tests;

public class PopupPlacementCalculatorTests
{
    [Fact]
    public void CalculatePosition_BottomRight_PlacesAtBottomRightWithMargin()
    {
        Rect workArea = new(0, 0, 1920, 1080);
        Size popup = new(300, 200);
        var anchors = new PopupAnchorContext(new Point(500, 500), null, null);

        Point pos = PopupPlacementCalculator.CalculatePosition(PopupPlacementMode.BottomRight, anchors, workArea, popup);

        Assert.Equal(1600, pos.X);
        Assert.Equal(860, pos.Y);
    }

    [Fact]
    public void CalculatePosition_NearSelection_WhenSelectionMissing_FallsBackToCursor()
    {
        Rect workArea = new(0, 0, 1920, 1080);
        Size popup = new(300, 200);
        var anchors = new PopupAnchorContext(new Point(100, 200), null, null);

        Point pos = PopupPlacementCalculator.CalculatePosition(PopupPlacementMode.NearSelection, anchors, workArea, popup);

        Assert.Equal(112, pos.X);
        Assert.Equal(212, pos.Y);
    }

    [Fact]
    public void CalculatePosition_NearActionBar_WhenActionBarMissing_FallsBackToCursor()
    {
        Rect workArea = new(0, 0, 1920, 1080);
        Size popup = new(300, 200);
        var anchors = new PopupAnchorContext(new Point(50, 60), null, null);

        Point pos = PopupPlacementCalculator.CalculatePosition(PopupPlacementMode.NearActionBar, anchors, workArea, popup);

        Assert.Equal(62, pos.X);
        Assert.Equal(72, pos.Y);
    }

    [Fact]
    public void ClampToWorkingArea_ClampsOutsideCoordinates()
    {
        Rect workArea = new(0, 0, 1000, 700);
        Size popup = new(200, 100);

        Point pos = PopupPlacementCalculator.ClampToWorkingArea(new Point(950, 650), workArea, popup);

        Assert.Equal(796, pos.X);
        Assert.Equal(596, pos.Y);
    }
}
