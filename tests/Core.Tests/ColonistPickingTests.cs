using System.Numerics;

namespace ColonySim.Core.Tests;

public class ColonistPickingTests
{
    static readonly Vector3 Down = Vector3.UnitZ; // looking along +Z

    [Fact] public void Hit_ReturnsId()
    {
        var list = new[] { (7, new Vector3(5, 10, 20)) };
        Assert.Equal(7, ColonistPicking.Pick(new Vector3(5, 10.9f, 0), Down, list, 100));
    }

    [Fact] public void Miss_BesideTheBox_OrBeyondMaxDistance_ReturnsZero()
    {
        var list = new[] { (7, new Vector3(5, 10, 20)) };
        Assert.Equal(0, ColonistPicking.Pick(new Vector3(5.4f, 10.9f, 0), Down, list, 100)); // box is 0.3 each side
        Assert.Equal(0, ColonistPicking.Pick(new Vector3(5, 10.9f, 0), Down, list, 10));
        Assert.Equal(0, ColonistPicking.Pick(new Vector3(5, 10.9f, 0), Down, [], 100));
    }

    [Fact] public void TwoInLine_NearestWins()
    {
        var list = new[] { (1, new Vector3(5, 10, 30)), (2, new Vector3(5, 10, 20)) };
        Assert.Equal(2, ColonistPicking.Pick(new Vector3(5, 10.5f, 0), Down, list, 100));
    }

    [Fact] public void HeadHeight_IsInsideTheBox_JustAboveIsNot()
    {
        var list = new[] { (3, new Vector3(5, 10, 20)) };
        Assert.Equal(3, ColonistPicking.Pick(new Vector3(5, 11.79f, 0), Down, list, 100));
        Assert.Equal(0, ColonistPicking.Pick(new Vector3(5, 11.81f, 0), Down, list, 100));
        Assert.Equal(0, ColonistPicking.Pick(new Vector3(5, 9.99f, 0), Down, list, 100));
    }

    [Fact] public void AngledRay_HitsThroughTheHead()
    {
        var list = new[] { (4, new Vector3(5, 10, 20)) };
        var origin = new Vector3(5, 20, 0);
        var dir = Vector3.Normalize(new Vector3(0, 11.5f - 20, 20));
        Assert.Equal(4, ColonistPicking.Pick(origin, dir, list, 100));
    }
}
