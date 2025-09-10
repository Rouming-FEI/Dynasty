using System.Collections.Generic;
using Gameplay.Locations;

namespace Gameplay.Movement
{
    public struct LocationData
    {
        public int Id;
        public string Name;
        public int[] Connections;
        public int[] InitialItems;
    }

    /// <summary>
    /// 地点掉落物的分组表示（ItemId + 数量），用于 UI 展示和查询。
    /// 内部运行时存储为扁平 int 列表以支持按 index 拾取和移除。
    /// </summary>
    public struct LocatedItem
    {
        public int ItemId;
        public int Count;
    }

    public class LocationManager
    {
        private LocationData[] _locations;
        /// <summary>运行时可变物品列表，按 locationId 索引。元素为 ItemId。</summary>
        private List<int>[] _runtimeItems;
        /// <summary>地点特殊规则，按 locationId 索引。无规则的地点为 null。</summary>
        public LocationRule[] Rules;

        public int LocationCount => _locations.Length;

        public LocationManager()
        {
            _locations = new LocationData[]
            {
                // 下潜入口 (0-3) — 连通安全屋、地狱前哨、以及所有新增地点
                new() { Id = 0, Name = "下潜入口A",  Connections = new[] { 4, 5, 6, 7, 8, 9 } },
                new() { Id = 1, Name = "下潜入口B",  Connections = new[] { 4, 5, 6, 7, 8, 9 } },
                new() { Id = 2, Name = "下潜入口C",  Connections = new[] { 4, 5, 6, 7, 8, 9 } },
                new() { Id = 3, Name = "下潜入口D",  Connections = new[] { 4, 5, 6, 7, 8, 9 } },
                // 安全屋(4) — Hub 节点，连通所有地点；血包×2 + 应急处理装置×2（适配 3 人测试）
                new() { Id = 4, Name = "安全屋",     Connections = new[] { 0, 1, 2, 3, 5, 6, 7, 8, 9 }, InitialItems = new[] { 2, 2, 8, 8 } },
                // 地狱前哨(5) — 原有地点，更新连通关系
                new() { Id = 5, Name = "地狱前哨",   Connections = new[] { 0, 1, 2, 3, 4, 6, 7, 8, 9 } },
                // 藏匿处(6) — 无初始掉落物
                new() { Id = 6, Name = "藏匿处",     Connections = new[] { 0, 1, 2, 3, 4 } },
                // 实验室(7) — 荆棘素×2 + 触发式炸弹×2
                new() { Id = 7, Name = "实验室",     Connections = new[] { 0, 1, 2, 3, 4 }, InitialItems = new[] { 6, 6, 7, 7 } },
                // 地狱前哨(8) — 枪×2 + 核弹×2
                new() { Id = 8, Name = "地狱前哨",   Connections = new[] { 0, 1, 2, 3, 4 }, InitialItems = new[] { 3, 3, 5, 5 } },
                // 地狱工坊(9) — 施工锤×3
                new() { Id = 9, Name = "地狱工坊",   Connections = new[] { 0, 1, 2, 3, 4 }, InitialItems = new[] { 4, 4, 4 } },
            };

            // 初始化运行时物品列表（从 InitialItems 拷贝）
            _runtimeItems = new List<int>[_locations.Length];
            for (int i = 0; i < _locations.Length; i++)
            {
                _runtimeItems[i] = new List<int>();
                int[] items = _locations[i].InitialItems;
                if (items != null)
                {
                    for (int j = 0; j < items.Length; j++)
                        _runtimeItems[i].Add(items[j]);
                }
            }

            // 注册地点规则（按 LocationId 索引，无规则的地点保持 null）
            Rules = new LocationRule[_locations.Length];
            Rules[6] = new HideoutRule();
            Rules[LabRule.LabLocationId] = new LabRule();
        }

        public string GetName(int locationId)
        {
            if (locationId < 0 || locationId >= _locations.Length) return "未知";
            return _locations[locationId].Name;
        }

        public bool IsValidLocation(int locationId)
        {
            return locationId >= 0 && locationId < _locations.Length;
        }

        public int[] GetConnectedLocations(int locationId)
        {
            if (locationId < 0 || locationId >= _locations.Length) return System.Array.Empty<int>();
            return _locations[locationId].Connections;
        }

        public int[] GetInitialItems(int locationId)
        {
            if (locationId < 0 || locationId >= _locations.Length) return null;
            return _locations[locationId].InitialItems;
        }

        /// <summary>
        /// 返回指定地点当前可拾取的物品（分组计数），用于 UI 展示。
        /// </summary>
        public LocatedItem[] GetLocatedItems(int locationId)
        {
            if (locationId < 0 || locationId >= _runtimeItems.Length) return System.Array.Empty<LocatedItem>();
            var list = _runtimeItems[locationId];
            if (list.Count == 0) return System.Array.Empty<LocatedItem>();

            // 手动分组计数，避免 Dictionary 分配（HOT PATH 兼容）
            var result = new List<LocatedItem>();
            for (int i = 0; i < list.Count; i++)
            {
                int itemId = list[i];
                bool found = false;
                for (int j = 0; j < result.Count; j++)
                {
                    if (result[j].ItemId == itemId)
                    {
                        result[j] = new LocatedItem { ItemId = itemId, Count = result[j].Count + 1 };
                        found = true;
                        break;
                    }
                }
                if (!found)
                    result.Add(new LocatedItem { ItemId = itemId, Count = 1 });
            }
            return result.ToArray();
        }

        /// <summary>
        /// 拾取指定地点的第 itemIndex 个物品。成功返回 ItemDef，失败返回 null。
        /// 拾取后该物品从运行时列表中移除。
        /// </summary>
        public Items.ItemDef TryPickupItem(int locationId, int itemIndex)
        {
            if (locationId < 0 || locationId >= _runtimeItems.Length) return null;
            var list = _runtimeItems[locationId];
            if (itemIndex < 0 || itemIndex >= list.Count) return null;
            int itemId = list[itemIndex];
            list.RemoveAt(itemIndex);
            return Items.ItemRegistry.Get(itemId);
        }

        /// <summary>
        /// 返回指定地点当前剩余物品数量。
        /// </summary>
        public int GetRemainingItemCount(int locationId)
        {
            if (locationId < 0 || locationId >= _runtimeItems.Length) return 0;
            return _runtimeItems[locationId].Count;
        }

        /// <summary>
        /// 向指定地点投放物品（ItemId × count）。用于武器投掷等场景。
        /// </summary>
        public void DropItem(int locationId, int itemId, int count)
        {
            if (locationId < 0 || locationId >= _runtimeItems.Length) return;
            var list = _runtimeItems[locationId];
            for (int i = 0; i < count; i++)
                list.Add(itemId);
        }

        /// <summary>
        /// 清除指定地点的所有掉落物。用于拆除（Demolish）场景。
        /// </summary>
        public void ClearItems(int locationId)
        {
            if (locationId < 0 || locationId >= _runtimeItems.Length) return;
            _runtimeItems[locationId].Clear();
        }

        public LocationData[] GetAll() => _locations;
    }
}
