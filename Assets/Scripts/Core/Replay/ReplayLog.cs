using System.Collections.Generic;
using Gameplay.Judgment;

namespace Core.Replay
{
    /// <summary>输入命令类型（仅玩家/AI 输入层命令；引擎内部命令不录制，回放时重新产生）。</summary>
    public enum ReplayActionKind : byte
    {
        UseSkill, Move, WasteAP, UseItem, PickupItem, ThrowHammer, Demolish, Reload,
        FireGun, SeedPlant, SeedRemove, DrawBlade, SheatheBlade, Unbind,
        ToggleDoor, ToggleTideSwitch, RemoveThreshold, MountTurret, LoadTurret,
        AimTurret, FireTurret, DismountTurret, Attack,
        ChaosSimulate,
        TwinConvert,
        /// <summary>未识别/不支持的命令类型。</summary>
        Unknown = 255
    }

    /// <summary>一轮判定筹码快照（作废重判轮也各占一项，与引擎轮次一一对齐）。</summary>
    public struct ReplayChipRound
    {
        public ChipAllocation[] Allocations;
    }

    /// <summary>一条输入命令记录。IntArg 含义按 Kind 解释（见 ReplayActionFactory）。</summary>
    public struct ReplayActionRecord
    {
        public int Tick;
        public int PlayerId;
        public ReplayActionKind Kind;
        public int IntArg1;
        public int IntArg2;
        public int IntArg3;
        public float FloatArg;
        /// <summary>输入来源：0=AI 决策（回放不注入、AI 重决策），1=真人输入（回放注入）。</summary>
        public byte Source;
    }

    /// <summary>事件时间线记录（字段与 GameplayEvent 一一对应 + Tick/Turn 锚点）。</summary>
    public struct ReplayEventRecord
    {
        public int Tick;
        public int Turn;
        public int EventType;
        public int SourceId;
        public int TargetId;
        public float Magnitude;
        public float Arg1;
        public float Arg2;
        public int IntArg1;
        public int IntArg2;
    }

    public sealed class ReplayLog
    {
        /// <summary>回放格式版本（与 ReplaySerializer.CurrentVersion 保持一致）。</summary>
        public int Version = 1;
        public int PlayerCount;
        public int Seed;
        /// <summary>按玩家位序的角色 Def Id；0 = 默认轮换（StartGame 无参路径）。</summary>
        public int[] CharacterDefIds;
        /// <summary>录制时快照；回放端沿用引擎 GameConfig.MaxTurns（静态值），
        /// 本字段仅供展示——GameConfig 变更后旧日志回放行为可能不一致。</summary>
        public int MaxTurns;
        public int TotalTicks;
        /// <summary>按玩家位序的真人掩码（录制时快照 lockstep.HumanPlayers）。</summary>
        public bool[] HumanPlayersMask;
        public List<ReplayChipRound> Chips = new List<ReplayChipRound>();
        public List<ReplayActionRecord> Actions = new List<ReplayActionRecord>();
        public List<ReplayEventRecord> Events = new List<ReplayEventRecord>();
        /// <summary>每 Tick 的状态哈希（索引 = Tick-1；长度 == TotalTicks）。</summary>
        public long[] TickHashes;
    }
}
