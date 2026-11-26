using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using UnityEngine;

namespace RatopiaCoop.Sync
{
    public class OrderSyncManager : MonoBehaviour
    {
        public static OrderSyncManager Instance { get; private set; } = null!;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (Instance != this)
            {
                Destroy(this);
            }
        }

        private void Start()
        {
            if (CoopNetManager.Instance != null)
            {
                CoopNetManager.Instance.OnPacketReceivedEvent += HandlePacket;
            }
        }

        public void SendOrderArea(int minX, int minY, int maxX, int maxY, WorkMarkKind kind, bool isDelete, int variationIndex = 0)
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected) return;
            if (SaveTransferManager.IsWorldLoading) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(minX);
            writer.Put(minY);
            writer.Put(maxX);
            writer.Put(maxY);
            writer.Put((byte)kind);
            writer.Put(isDelete);
            writer.Put(variationIndex);

            CoopNetManager.Instance.SendPacket(PacketType.OrderDigArea, writer, DeliveryMethod.ReliableOrdered);
        }

        private void HandlePacket(PacketType type, NetDataReader reader)
        {
            if (type == PacketType.OrderDigArea)
            {
                if (SaveTransferManager.IsWorldLoading) return;

                int minX = reader.GetInt();
                int minY = reader.GetInt();
                int maxX = reader.GetInt();
                int maxY = reader.GetInt();
                WorkMarkKind kind = (WorkMarkKind)reader.GetByte();
                bool isDelete = reader.GetBool();
                int variationIndex = reader.AvailableBytes >= 4 ? reader.GetInt() : 0;

                ApplyOrderArea(minX, minY, maxX, maxY, kind, isDelete, variationIndex);
            }
        }

        public void ApplyOrderArea(int minX, int minY, int maxX, int maxY, WorkMarkKind kind, bool isDelete, int variationIndex = 0)
        {
            if (SaveTransferManager.IsWorldLoading) return;
            if (GameMgr.Instance == null || GameMgr.Instance._SysMgr == null || GameMgr.Instance._PoolMgr == null || GameMgr.Instance._TileMgr == null)
                return;

            var sysMgr = GameMgr.Instance._SysMgr;
            var poolMgr = GameMgr.Instance._PoolMgr;
            var tileMgr = GameMgr.Instance._TileMgr;
            var buildingMgr = GameMgr.Instance._BuildingMgr;

            Plugin.Log.LogInfo($"[OrderSyncManager] Applying order area: ({minX},{minY}) to ({maxX},{maxY}), kind: {kind}, delete: {isDelete}");

            var processedWorldObjs = new HashSet<WorldObject>();
            var processedBuildings = new HashSet<Building>();
            var processedTiles = new HashSet<C_Tile>();

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (!tileMgr.IsInsideMap(x, y))
                        continue;

                    Vector2 pos = new Vector2(x, y);
                    Vector2Int posInt = new Vector2Int(x, y);

                    if (isDelete || kind == WorkMarkKind.Cancel)
                    {
                        if (kind == WorkMarkKind.Delete_Mining || kind == WorkMarkKind.Cancel)
                        {
                            var wm = sysMgr.List_WorkMark.Find(t => t != null && t.IsSamePos(x, y));
                            wm?.WorkMarkDestroy(false);
                        }

                        if (kind == WorkMarkKind.Delete_Gathering || kind == WorkMarkKind.Cancel)
                        {
                            var node = tileMgr.GetNode(x, y);
                            if (node?.m_WorldObj != null)
                            {
                                var gm = sysMgr.List_GatherMark.Find(w => w != null && w.m_WorldObj == node.m_WorldObj);
                                if (gm != null)
                                {
                                    gm.WorkMarkDestroy(false);
                                    node.m_WorldObj.CustomMatSet(true, 0);
                                }
                            }
                        }

                        if (kind == WorkMarkKind.Delete_Repair || kind == WorkMarkKind.Cancel)
                        {
                            var tile = tileMgr.GetTile(x, y);
                            if (tile != null && tile.IsBuilding)
                            {
                                var rm = sysMgr.List_RepairMark.Find(w => w != null && w.m_Tile == tile);
                                rm?.WorkMarkDestroy(false);
                            }
                            else if (buildingMgr != null)
                            {
                                var bld = buildingMgr.List_Building.Find(b => b != null && b.List_BuildPos.Contains(posInt));
                                if (bld != null)
                                {
                                    var rm = sysMgr.List_RepairMark.Find(w => w != null && w.m_Building == bld);
                                    rm?.WorkMarkDestroy(false);
                                }
                            }
                        }

                        if (kind == WorkMarkKind.Delete_Demolition || kind == WorkMarkKind.Cancel)
                        {
                            var tile = tileMgr.GetTile(x, y);
                            if (tile != null && tile.IsBuilding)
                            {
                                var dm = sysMgr.List_DemolitionMark.Find(w => w != null && w.m_Tile == tile);
                                dm?.WorkMarkDestroy(false);
                            }
                            else if (buildingMgr != null)
                            {
                                Building? bld = buildingMgr.List_Building.Find(b => b != null && b.List_BuildPos.Contains(posInt));
                                if (bld == null)
                                    bld = buildingMgr.List_HeavyWire.Find(b => b != null && b.List_BuildPos.Contains(posInt));
                                if (bld == null)
                                    bld = buildingMgr.List_Wallpaper.Find(b => b != null && b.List_BuildPos.Contains(posInt));

                                if (bld != null)
                                {
                                    var dm = sysMgr.List_DemolitionMark.Find(w => w != null && (w.m_Building == bld || (w.IsSamePos(x, y) && tileMgr.IsNoTile(x, y))));
                                    dm?.WorkMarkDestroy(false);
                                }
                            }
                        }

                        if (kind == WorkMarkKind.Cancel && buildingMgr != null)
                        {
                            var bp = buildingMgr.List_BP_BlueBuilding?.Find(b => b != null && b.List_BuildPos.Contains(posInt));
                            bp?.CancelBP();
                        }
                    }
                    else
                    {
                        if (kind == WorkMarkKind.Mining)
                        {
                            TileType waterType = tileMgr.GetWaterType(pos);
                            C_Tile? tile = tileMgr.GetTile(x, y);
                            bool isBuildingBlock = tile != null && tile.IsBuilding && !tile.IsNatureLadder;

                            if (waterType == TileType.Water || (tile != null && !isBuildingBlock && !tile.IsPollution))
                            {
                                var existing = sysMgr.List_WorkMark.Find(q => q != null && q.IsSamePos(x, y));
                                existing?.WorkMarkDestroy(false);

                                var mark = poolMgr.Pool_WorkMark.GetNextObj()?.GetComponent<WorkMark>();
                                if (mark != null)
                                {
                                    mark.MarkSet(x, y, false, WorkMarkKind.Mining, variationIndex);
                                    mark.SprOrderSet(9);
                                    if (mark.m_Tile != null)
                                    {
                                        mark.EnableListUpdate();
                                    }
                                    else
                                    {
                                        mark.WorkMarkDestroy(false);
                                    }
                                }
                            }
                        }
                        else if (kind == WorkMarkKind.Gathering)
                        {
                            if (tileMgr.IsPlantExistNode(pos))
                            {
                                var node = tileMgr.GetNode(x, y);
                                if (node?.m_WorldObj != null && node.m_NodeType != NodeType.Wall &&
                                    !processedWorldObjs.Contains(node.m_WorldObj) &&
                                    !sysMgr.List_GatherMark.Exists(w => w != null && w.m_WorldObj == node.m_WorldObj))
                                {
                                    processedWorldObjs.Add(node.m_WorldObj);
                                    var mark = poolMgr.Pool_WorkMark.GetNextObj()?.GetComponent<WorkMark>();
                                    if (mark != null)
                                    {
                                        mark.MarkSet(x, y, false, WorkMarkKind.Gathering);
                                        mark.SprOrderSet(9);
                                        mark.MarkRefresh(node.m_WorldObj);
                                        node.m_WorldObj.CustomMatSet(false, 0);
                                    }
                                }
                            }
                        }
                        else if (kind == WorkMarkKind.Repair && buildingMgr != null)
                        {
                            C_Tile? tile = tileMgr.GetTile(x, y);
                            if (tile != null && tile.IsBuilding && !tile.IsNatureLadder && tile.m_HP < tile.m_MaxHP &&
                                !processedTiles.Contains(tile) &&
                                !sysMgr.List_DemolitionMark.Exists(w => w != null && w.m_Tile == tile) &&
                                !sysMgr.List_RepairMark.Exists(w => w != null && w.m_Tile == tile))
                            {
                                processedTiles.Add(tile);
                                var mark = poolMgr.Pool_WorkMark.GetNextObj()?.GetComponent<WorkMark>();
                                if (mark != null)
                                {
                                    mark.MarkSet(x, y, false, WorkMarkKind.Repair);
                                    mark.SprOrderSet(9);
                                    mark.m_Tile = tile;
                                    mark.m_Tile.MakeEnableList();
                                }
                            }
                            else if (tileMgr.GetTileType(pos) >= TileType.Building && tileMgr.GetTileType(pos) < TileType.Default)
                            {
                                Building? bld = buildingMgr.FindBuildingByBpos(posInt);
                                if (bld != null && bld.IsCanRepair() &&
                                    !processedBuildings.Contains(bld) &&
                                    !sysMgr.List_DemolitionMark.Exists(w => w != null && w.m_Building == bld) &&
                                    !sysMgr.List_RepairMark.Exists(w => w != null && w.m_Building == bld))
                                {
                                    processedBuildings.Add(bld);
                                    var mark = poolMgr.Pool_WorkMark.GetNextObj()?.GetComponent<WorkMark>();
                                    if (mark != null)
                                    {
                                        mark.MarkSet(x, y, false, WorkMarkKind.Repair);
                                        mark.SprOrderSet(9);
                                        mark.MarkRefresh(bld);
                                    }
                                }
                            }
                        }
                        else if (kind == WorkMarkKind.Demolition && buildingMgr != null)
                        {
                            C_Tile? tile = tileMgr.GetTile(x, y);
                            if (tile != null && tile.IsBuilding && !tile.IsNatureLadder &&
                                !processedTiles.Contains(tile) &&
                                !sysMgr.List_DemolitionMark.Exists(w => w != null && w.m_Tile == tile) &&
                                !sysMgr.List_RepairMark.Exists(w => w != null && w.m_Tile == tile))
                            {
                                processedTiles.Add(tile);
                                var mark = poolMgr.Pool_WorkMark.GetNextObj()?.GetComponent<WorkMark>();
                                if (mark != null)
                                {
                                    mark.MarkSet(x, y, false, WorkMarkKind.Demolition);
                                    mark.SprOrderSet(9);
                                    mark.m_Tile = tile;
                                    mark.m_Tile.MakeEnableList();
                                }
                            }
                            else
                            {
                                Building? bld = buildingMgr.List_Building.Find(b => b != null && b.List_BuildPos.Contains(posInt));
                                if (bld != null &&
                                    (bld.m_Info.Name == BuildingName.HomePortal ||
                                     (bld.m_Info.Ability == BuildAbility.Store && buildingMgr.List_Storage.Count == 1)))
                                {
                                    bld = null; // Protected core buildings cannot be demolished
                                }

                                if (bld == null && tile == null)
                                {
                                    bld = buildingMgr.List_HeavyWire.Find(b => b != null && b.Pos_Tile == posInt);
                                }
                                if (bld == null && tile == null &&
                                    (buildingMgr.List_BP_BlueBuilding == null || !buildingMgr.List_BP_BlueBuilding.Exists(bp => bp != null && bp.List_BuildPos.Contains(posInt))))
                                {
                                    bld = buildingMgr.List_Wallpaper.Find(b => b != null && b.Pos_Tile == posInt);
                                }

                                if (bld != null &&
                                    !processedBuildings.Contains(bld) &&
                                    !sysMgr.List_DemolitionMark.Exists(w => w != null && w.m_Building == bld) &&
                                    !sysMgr.List_RepairMark.Exists(w => w != null && w.m_Building == bld))
                                {
                                    processedBuildings.Add(bld);
                                    var mark = poolMgr.Pool_WorkMark.GetNextObj()?.GetComponent<WorkMark>();
                                    if (mark != null)
                                    {
                                        mark.MarkSet(x, y, false, WorkMarkKind.Demolition);
                                        mark.SprOrderSet(9);
                                        mark.MarkRefresh(bld);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            sysMgr.WorkFlowFunction();
            Plugin.Log.LogInfo($"[OrderSyncManager] Work marks applied. Total in list: {sysMgr.List_WorkMark.Count}");
        }

        private void OnDestroy()
        {
            if (CoopNetManager.Instance != null)
            {
                CoopNetManager.Instance.OnPacketReceivedEvent -= HandlePacket;
            }
        }
    }
}
