using System;
using System.IO;
using Gameplay.Characters;
using Gameplay.Items;

namespace Core.Sync
{
    /// <summary>
    /// 相邻 WorldState 快照的玩家块级差异（Delta）。
    /// 格式：[baseTick:int][targetHash:long][flags:uint][全局块][变化玩家块...]
    ///   flags 位 i（i &lt; 31）= 玩家 i 变化；位 31 = 全局块变化（Compute 恒置位——全局状态几乎每回合都变，诚实选择）。
    /// 玩家块按序列化字节比较决定变化；块序列化复用 WorldStateSerializer 内部函数（WriteGlobal/ReadGlobal/WriteCharacter/ReadCharacter）。
    /// Apply 原地应用（baseState 须为 Compute 时 from 的深拷贝），完成后 ComputeStateHash 与 targetHash 自校验，
    /// 不一致抛 InvalidDataException（坏 delta / 遗漏字段早暴露）。
    /// Apply 后 state.TurnManager 对象同一性保持（全局字段原地恢复，不换引用），
    /// 可安全用于已接线的 LocalLockstep（重连客户端场景驱动器不脱钩）。
    /// </summary>
    public static class WorldStateDelta
    {
        /// <summary>全局块变化标志（位 31）。</summary>
        private const uint GlobalChangedFlag = 0x80000000u;

        /// <summary>计算 from → to 的差异：全局块恒变化，玩家块按序列化字节比较。</summary>
        public static byte[] Compute(WorldState from, WorldState to)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (from.PlayerCount != to.PlayerCount)
                throw new InvalidOperationException($"Compute 要求相邻状态玩家数一致（{from.PlayerCount} vs {to.PlayerCount}）");

            try
            {
                using (var ms = new MemoryStream())
                {
                    using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                    {
                        w.Write(from.CurrentTick);          // baseTick：本 delta 所基于的状态 tick
                        w.Write(to.ComputeStateHash());     // targetHash：Apply 自校验目标（8B，换取坏 delta 早暴露）

                        // 第一趟：逐玩家块字节比较，得出变化掩码
                        uint flags = GlobalChangedFlag;     // 全局块恒视为变化（简单正确优先）
                        for (int i = 0; i < to.Characters.Length; i++)
                            if (!CharacterBlockEquals(from.Characters[i], to.Characters[i]))
                                flags |= (1u << i);
                        w.Write(flags);

                        // 全局块 + 变化玩家块（复用序列化器块读写原语）
                        WorldStateSerializer.WriteGlobal(to, w);
                        for (int i = 0; i < to.Characters.Length; i++)
                            if ((flags & (1u << i)) != 0)
                                WorldStateSerializer.WriteCharacter(to.Characters[i], w);
                    }
                    return ms.ToArray();
                }
            }
            catch (EndOfStreamException e)
            {
                // 对称统一「坏数据」语义（当前写路径不会触发，防御未来读路径重构）
                throw new InvalidDataException("Delta 数据截断或损坏", e);
            }
        }

        /// <summary>把 delta 原地应用到 baseState（baseState 须为 Compute 时 from 的深拷贝；
        /// 引用字段 Random/Locations/LastJudgment 等整体换引用，Events/Commands/OnTickAdvanced 等瞬态字段不动；
        /// TurnManager 原地恢复，对象同一性保持）。</summary>
        public static void Apply(WorldState baseState, byte[] deltaBytes)
        {
            if (baseState == null) throw new ArgumentNullException(nameof(baseState));
            if (deltaBytes == null) throw new ArgumentNullException(nameof(deltaBytes));
            ItemRegistry.Initialize();   // 角色重建依赖道具表（幂等）

            try
            {
                using (var ms = new MemoryStream(deltaBytes, false))
                using (var r = new BinaryReader(ms))
                {
                    int baseTick = r.ReadInt32();
                    long targetHash = r.ReadInt64();
                    uint flags = r.ReadUInt32();

                    if (baseState.CurrentTick != baseTick)
                        throw new InvalidDataException(
                            $"Delta baseTick 不匹配（期望应用在 tick {baseTick}，当前 {baseState.CurrentTick}）");

                    uint playerMask = (1u << baseState.Characters.Length) - 1;
                    if ((flags & ~(GlobalChangedFlag | playerMask)) != 0)
                        throw new InvalidDataException($"Delta flags 含未知玩家位 0x{flags:X8}");

                    if ((flags & GlobalChangedFlag) != 0)
                    {
                        WorldState fresh = WorldStateSerializer.ReadGlobal(r);
                        if (fresh.PlayerCount != baseState.PlayerCount)
                            throw new InvalidDataException(
                                $"Delta 玩家数与 base 不一致（{fresh.PlayerCount} vs {baseState.PlayerCount}）");
                        CopyGlobalFields(fresh, baseState);
                    }

                    for (int i = 0; i < baseState.Characters.Length; i++)
                        if ((flags & (1u << i)) != 0)
                            baseState.Characters[i] = WorldStateSerializer.ReadCharacter(r);

                    if (r.BaseStream.Position != r.BaseStream.Length)
                        throw new InvalidDataException("Delta 尾部存在多余字节");

                    long actualHash = baseState.ComputeStateHash();
                    if (actualHash != targetHash)
                        throw new InvalidDataException(
                            $"Delta 应用后哈希自校验失败（期望 {targetHash}，实际 {actualHash}）——坏 delta 或遗漏字段");
                }
            }
            catch (EndOfStreamException e)
            {
                // 截断/损坏的 delta 读越界：统一为「坏 delta」语义（InvalidDataException）
                throw new InvalidDataException("Delta 数据截断或损坏", e);
            }
        }

        /// <summary>比较两个玩家槽的序列化块字节是否相同（含 null 槽哨兵，null == null）。</summary>
        private static bool CharacterBlockEquals(Character a, Character b)
        {
            byte[] bytesA;
            using (var msA = new MemoryStream())
            {
                using (var wA = new BinaryWriter(msA, System.Text.Encoding.UTF8, leaveOpen: true))
                    WorldStateSerializer.WriteCharacter(a, wA);
                bytesA = msA.ToArray();
            }
            byte[] bytesB;
            using (var msB = new MemoryStream())
            {
                using (var wB = new BinaryWriter(msB, System.Text.Encoding.UTF8, leaveOpen: true))
                    WorldStateSerializer.WriteCharacter(b, wB);
                bytesB = msB.ToArray();
            }
            if (bytesA.Length != bytesB.Length) return false;
            for (int i = 0; i < bytesA.Length; i++)
                if (bytesA[i] != bytesB[i]) return false;
            return true;
        }

        /// <summary>把 ReadGlobal 重建出的全局字段整体搬入 baseState（覆盖写；Random/Locations 等引用字段直接换引用，
        /// TurnManager 不换引用——原地恢复三字段以保持对象同一性，已接线的驱动器引用不失效）。</summary>
        private static void CopyGlobalFields(WorldState src, WorldState dst)
        {
            dst.PlayerCount = src.PlayerCount;
            dst.CurrentTick = src.CurrentTick;
            dst.CurrentTurn = src.CurrentTurn;
            dst.Phase = src.Phase;
            dst.Random = src.Random;
            dst.LocationTrapCounts = src.LocationTrapCounts;
            dst.ThornPotionTurnsRemaining = src.ThornPotionTurnsRemaining;
            dst.MovedThisTurn = src.MovedThisTurn;
            dst.WoodSeedPlayer = src.WoodSeedPlayer;
            dst.WoodSeedLocation = src.WoodSeedLocation;
            dst.PaladinAegisUsed = src.PaladinAegisUsed;
            dst.LastAllocations = src.LastAllocations;
            dst.HasJudgmentResult = src.HasJudgmentResult;
            dst.LastJudgment = src.LastJudgment;
            // TurnManager：换引用会让已接线的 LocalLockstep 仍持旧实例 → Apply 后状态与驱动器分叉。
            // 原地恢复三字段（src 由 ReadGlobal 新建即弃；ActionOrder getter 返回内部数组引用，
            // RestoreActionOrder 直接存引用，克隆一份再传，消除对 src 生命周期假设的依赖）。
            if (dst.TurnManager == null)
                dst.TurnManager = new TurnManager();
            dst.TurnManager.RestoreConsecutiveFlatCount(src.TurnManager.ConsecutiveFlatCount);
            dst.TurnManager.RestoreActionOrder((int[])src.TurnManager.ActionOrder.Clone());
            dst.TurnManager.RestoreActorIndex(src.TurnManager.CurrentActorIndex);
            dst.Locations = src.Locations;
        }
    }
}
