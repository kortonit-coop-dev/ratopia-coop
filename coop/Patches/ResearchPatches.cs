using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using RatopiaCoop.Sync;
using UnityEngine;

namespace RatopiaCoop.Patches
{
    [HarmonyPatch(typeof(Tech_RPInfo))]
    public static class ResearchPatches
    {
        private static bool _isSyncingResearch;

        [HarmonyPatch(nameof(Tech_RPInfo.NowUpgradeNode), typeof(TechInfo))]
        [HarmonyPostfix]
        public static void NowUpgradeNode_Postfix(TechInfo _info)
        {
            if (_isSyncingResearch || SaveTransferManager.IsWorldLoading) return;
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            if (GameMgr.Instance?._TileMgr != null && GameMgr.Instance._TileMgr.m_GameLoading) return;

            if (_info != null)
            {
                try
                {
                    _isSyncingResearch = true;
                    NetDataWriter writer = new NetDataWriter();
                    writer.Put(_info.Index);
                    writer.Put(_info.Category);
                    writer.Put(_info.Tech_Name1);
                    CoopNetManager.Instance.SendPacket(PacketType.ResearchUnlock, writer, DeliveryMethod.ReliableOrdered);
                }
                finally
                {
                    _isSyncingResearch = false;
                }
            }
        }

        public static void ApplyRemoteResearch(int techIndex, int category = -1, int techName1 = -1)
        {
            if (SaveTransferManager.IsWorldLoading) return;
            if (GameMgr.Instance == null || GameMgr.Instance._ResearchUI == null || GameMgr.Instance._DB_Mgr == null) return;
            var researchUI = GameMgr.Instance._ResearchUI;
            var db = GameMgr.Instance._DB_Mgr;

            TechInfo? info = FindTechInfo(db.List_Tech_DB, techIndex, category, techName1)
                ?? FindTechInfo(db.List_ScientistTech_DB, techIndex, category, techName1)
                ?? FindTechInfo(db.List_MagicianTech_DB, techIndex, category, techName1);

            if (info == null || !info.IsLock) return;

            try
            {
                _isSyncingResearch = true;
                researchUI.m_Tech_RPInfo?.NowUpgradeNode(info);
                var targetNode = researchUI.List_TechNode?.Find(n => n != null && n.m_Info == info);
                targetNode?.TechNodeUpdate();
                researchUI.LineUpdate();
                researchUI.NodeUpdate();
            }
            finally
            {
                _isSyncingResearch = false;
            }
        }

        private static TechInfo? FindTechInfo(System.Collections.Generic.List<TechInfo>? list, int index, int category, int name1)
        {
            if (list == null) return null;
            return list.Find(x => x != null && x.Index == index &&
                (category < 0 || x.Category == category) &&
                (name1 < 0 || x.Tech_Name1 == name1));
        }
    }
}
