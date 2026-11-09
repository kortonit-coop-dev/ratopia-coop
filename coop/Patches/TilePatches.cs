using HarmonyLib;
using RatopiaCoop.Network;
using RatopiaCoop.Sync;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch]
    public static class TilePatches
    {
        public static bool IsClientManualMining { get; set; }

        public static bool IsSafeToSyncTiles()
        {
            if (WorldSyncManager.Instance == null || WorldSyncManager.Instance.IsApplyingRemoteTile)
                return false;

            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected)
                return false;

            // Never sync tiles during world save transfer, map generation, or save loading
            if (SaveTransferManager.IsWorldLoading)
                return false;

            // Must be in active game scene
            if (SceneManager.GetActiveScene().name != "GameScene")
                return false;

            var tileMgr = GameMgr.Instance?._TileMgr;
            if (tileMgr == null)
                return false;

            return true;
        }

        // Hook C_Tile.DestroyTile which is called directly when a citizen/queen mines a block or when terrain changes
        [HarmonyPatch(typeof(C_Tile), "DestroyTile")]
        [HarmonyPostfix]
        public static void C_Tile_DestroyTile_Postfix(C_Tile __instance)
        {
            if (__instance == null) return;

            // Never sync UI preview dummy tiles
            if (__instance.m_TileType == TileType.DummyGreen || __instance.m_TileType == TileType.DummyRed)
                return;

            if (!IsSafeToSyncTiles()) return;

            int x = __instance.m_X;
            int y = __instance.m_Y;
            bool isWater = (__instance.m_TileType == TileType.Water);

            if (CoopNetManager.Instance.IsHost)
            {
                WorldSyncManager.Instance?.SendTileDestroyed(x, y, isWater);
            }
            else if (CoopNetManager.Instance.IsClient && IsClientManualMining)
            {
                // Client player manually mined a block with their character -> request Host to destroy it
                WorldSyncManager.Instance?.SendRequestMineTile(x, y, isWater);
            }
        }

        [HarmonyPatch(typeof(TileMgr), "MakeTile", typeof(TileType), typeof(Vector2), typeof(int))]
        [HarmonyPostfix]
        public static void MakeTile_Postfix(TileType _type, Vector2 pos, int _att_num)
        {
            if (_type == TileType.None || _type == TileType.DummyGreen || _type == TileType.DummyRed)
                return;

            if (!IsSafeToSyncTiles()) return;
            if (!CoopNetManager.Instance.IsHost) return;

            int x = Mathf.RoundToInt(pos.x);
            int y = Mathf.RoundToInt(pos.y);
            WorldSyncManager.Instance?.SendTileCreated((int)_type, x, y, _att_num);
        }
    }
}
