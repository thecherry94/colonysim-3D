using ColonySim.Core;

namespace ColonySim.Core.Tests;

public class HashTests
{
    [Fact] public void Of_IsDeterministic_AndSeedSensitive() { Assert.Equal(Hash.Of(1,2,3,4), Hash.Of(1,2,3,4)); Assert.NotEqual(Hash.Of(1,2,3,4), Hash.Of(2,2,3,4)); }
    [Fact] public void Unit_IsInZeroOne() { for (int i = 0; i < 1000; i++) { var u = Hash.Unit(7, i, -i, i * 3); Assert.InRange(u, 0f, 0.99999994f); } }
}
