using ColonySim.Core;

namespace ColonySim.Game;

/// <summary>Carries the active map from the new-game/load screen into <see cref="MapScene"/>.</summary>
public static class GameSession
{
    public static ColonyMap? Map;
}
