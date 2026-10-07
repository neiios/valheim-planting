using HarmonyLib;

namespace BulkPlanting
{
    [HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacementGhost))]
    internal static class UpdatePlacementGhostPatch
    {
        private static void Postfix(Player __instance)
        {
            BulkPlanter.UpdateLayout(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class TryPlacePiecePatch
    {
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            if (!BulkPlanter.IsActive(__instance, out _, out _))
            {
                return true;
            }
            __result = BulkPlanter.PlaceAll(__instance, piece);
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetupPlacementGhost))]
    internal static class SetupPlacementGhostPatch
    {
        private static void Postfix()
        {
            BulkPlanter.DestroyPreviews();
        }
    }

    /// <summary>While the resize modifier is held, the mouse wheel resizes the grid instead of rotating.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacement))]
    internal static class UpdatePlacementPatch
    {
        private static void Prefix(Player __instance, out int __state)
        {
            __state = __instance.m_placeRotation;
        }

        private static void Postfix(Player __instance, int __state)
        {
            if (Plugin.IsResizeModifierHeld() && BulkPlanter.IsActive(__instance, out _, out _))
            {
                __instance.m_placeRotation = __state;
            }
        }
    }
}
