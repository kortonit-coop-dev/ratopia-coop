using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using RatopiaCoop.Sync;
using UnityEngine;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch(typeof(TechNode))]
    public static class ResearchPatches
    {
        private static bool _isSyncingResearch;

        [HarmonyPatch("NodeClick", typeof(bool))]
        [HarmonyPostfix]
        public static void NodeClick_Postfix(TechNode __instance, bool _sound)
        {
            if (_isSyncingResearch || SaveTransferManager.IsWorldLoading) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;

            if (__instance != null && __instance.m_Info != null)
            {
                try
                {
                    _isSyncingResearch = true;
                    int techIndex = __instance.m_Info.Index;
                    NetDataWriter writer = new NetDataWriter();
                    writer.Put(techIndex);
                    CoopNetManager.Instance.SendPacket(PacketType.ResearchUnlock, writer, DeliveryMethod.ReliableOrdered);
                }
                finally
                {
                    _isSyncingResearch = false;
                }
            }
        }

        public static void ApplyRemoteResearch(int techIndex)
        {
            if (SaveTransferManager.IsWorldLoading) return;
            if (GameMgr.Instance == null || GameMgr.Instance._ResearchUI == null) return;
            var researchUI = GameMgr.Instance._ResearchUI;

            var targetNode = researchUI.List_TechNode?.Find(n => n != null && n.m_Info != null && n.m_Info.Index == techIndex);
            if (targetNode != null)
            {
                try
                {
                    _isSyncingResearch = true;
                    targetNode.NodeClick(true);
                }
                finally
                {
                    _isSyncingResearch = false;
                }
            }
        }
    }
}
