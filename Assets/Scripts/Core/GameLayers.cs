using UnityEngine;

/// <summary>
/// Central lookup for every layer this project relies on.
///
/// Nothing else in the codebase should call LayerMask.NameToLayer or
/// hardcode a layer index. Layers get reordered in the Tag Manager and
/// magic numbers silently rot when that happens.
/// </summary>
public static class GameLayers
{
    // =========================================================
    // NAMES
    // =========================================================

    public const string DefaultName = "Default";
    public const string ObstacleName = "Obstacle";
    public const string NpcName = "NPC";
    public const string PlayerName = "Player";
    public const string VentName = "Vent";
    public const string HidespotName = "Hidespot";


    // =========================================================
    // INDICES
    // =========================================================

    public static readonly int Default;
    public static readonly int Obstacle;
    public static readonly int Npc;
    public static readonly int Player;
    public static readonly int Vent;
    public static readonly int Hidespot;


    // =========================================================
    // MASKS
    // =========================================================

    /// <summary>
    /// Anything that should block line of sight and sound.
    /// </summary>
    public static readonly LayerMask SightBlockers;

    /// <summary>
    /// Anything the player or an NPC can physically collide with.
    /// </summary>
    public static readonly LayerMask Environment;

    public static readonly LayerMask PlayerMask;

    public static readonly LayerMask NpcMask;


    // =========================================================
    // SETUP
    // =========================================================

    static GameLayers()
    {
        Default = Resolve(DefaultName);
        Obstacle = Resolve(ObstacleName);
        Npc = Resolve(NpcName);
        Player = Resolve(PlayerName);

        /*
         * Vent and Hidespot are Phase 4 / Phase 5 layers.
         *
         * They are expected to be missing during early phases,
         * so they resolve quietly instead of warning.
         */

        Vent = Resolve(VentName, warnIfMissing: false);
        Hidespot = Resolve(HidespotName, warnIfMissing: false);

        SightBlockers = MaskOf(Default, Obstacle);
        Environment = MaskOf(Default, Obstacle);
        PlayerMask = MaskOf(Player);
        NpcMask = MaskOf(Npc);
    }


    private static int Resolve(
        string layerName,
        bool warnIfMissing = true)
    {
        int index = LayerMask.NameToLayer(layerName);

        if (index < 0 && warnIfMissing)
        {
            Debug.LogWarning(
                $"[GameLayers] Layer '{layerName}' does not exist. " +
                "Add it in Project Settings > Tags and Layers."
            );
        }

        return index;
    }


    /// <summary>
    /// Builds a mask from layer indices, skipping any that failed
    /// to resolve. A missing layer contributes nothing rather than
    /// corrupting the whole mask.
    /// </summary>
    private static LayerMask MaskOf(params int[] layers)
    {
        int mask = 0;

        foreach (int layer in layers)
        {
            if (layer >= 0)
            {
                mask |= 1 << layer;
            }
        }

        return mask;
    }


    // =========================================================
    // HELPERS
    // =========================================================

    public static bool IsInMask(
        int layer,
        LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }


    /// <summary>
    /// True once the layers a given phase depends on all exist.
    /// Used by the debug overlay to nag about missing setup.
    /// </summary>
    public static bool CoreLayersReady =>
        Default >= 0 &&
        Obstacle >= 0 &&
        Npc >= 0 &&
        Player >= 0;
}
