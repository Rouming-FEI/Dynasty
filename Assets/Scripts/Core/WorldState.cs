using System;
using Core.GAS;
using Core.Command;
using Gameplay.Characters;
using Gameplay.Judgment;
using Gameplay.Movement;

namespace Core
{
    public enum TurnPhase
    {
        Waiting,
        JudgmentPhase,
        JudgmentResolve,
        ActionPhase,
        TurnEnd,
        GameOver
    }

    public class WorldState
    {
        public Character[] Characters;
        public EventBus Events;
        public CommandQueue Commands;
        public TurnManager TurnManager;
        public DeterministicRandom Random;
        public LocationManager Locations;
        public bool HasJudgmentResult;
        public JudgmentResult LastJudgment;
        /// <summary>最近一轮判定每位玩家的筹码分布（供日志/调试显示）。</summary>
        public ChipAllocation[] LastAllocations;
        public int CurrentTick;
        public int CurrentTurn;
        public TurnPhase Phase;
        public int PlayerCount;

        /// <summary>调试/录制用：每次逻辑 Tick 状态变更完成后回调（检查点语义，录制器挂载；null 零开销）。
        /// 回调不得修改状态、不得抛异常——违反会破坏帧同步一致性。</summary>
        public Action<int> OnTickAdvanced;

        // 陷阱炸弹存储（定长数组，确定性安全）
        public int[] LocationTrapCounts;

        // 荆棘素全局 DOT 回合计数器
        public int ThornPotionTurnsRemaining;

        // 本回合移动记录（用于荆棘素 DOT 判定）
        public bool[] MovedThisTurn;

        // 木灵种子复活状态（-1 = 无种子）
        public int WoodSeedPlayer;    // 安置种子的玩家 PlayerId
        public int WoodSeedLocation;  // 种子所在地点

        // 圣骑士护佑一次性标记（全局仅一次）
        public bool PaladinAegisUsed;

        public WorldState(int playerCount, int randomSeed = 42)
        {
            PlayerCount = playerCount;
            Characters = new Character[playerCount];
            Events = new EventBus();
            Commands = new CommandQueue();
            Random = new DeterministicRandom(randomSeed);
            Locations = new LocationManager();
            LocationTrapCounts = new int[GameConfig.MaxLocations];
            MovedThisTurn = new bool[GameConfig.MaxPlayers];
            LastAllocations = new ChipAllocation[GameConfig.MaxPlayers];
            ThornPotionTurnsRemaining = 0;
            WoodSeedPlayer = -1;
            WoodSeedLocation = -1;
            PaladinAegisUsed = false;
            CurrentTick = 0;
            CurrentTurn = 0;
            Phase = TurnPhase.Waiting;
        }

        public Character GetCharacter(int id)
        {
            for (int i = 0; i < Characters.Length; i++)
                if (Characters[i] != null && Characters[i].Id == id) return Characters[i];
            return null;
        }

        public Character GetCharacterByPlayer(int playerId)
        {
            for (int i = 0; i < Characters.Length; i++)
                if (Characters[i] != null && Characters[i].PlayerId == playerId) return Characters[i];
            return null;
        }

        public AbilitySystemComponent GetASC(int characterId)
        {
            return GetCharacter(characterId)?.ASC;
        }

        public int AliveCharacterCount
        {
            get
            {
                int c = 0;
                for (int i = 0; i < Characters.Length; i++)
                    if (Characters[i] != null && !Characters[i].IsDead) c++;
                return c;
            }
        }

        public void PlantTrap(int locId)
        {
            if (locId >= 0 && locId < LocationTrapCounts.Length)
                LocationTrapCounts[locId]++;
        }

        public bool HasTrap(int locId)
        {
            if (locId < 0 || locId >= LocationTrapCounts.Length) return false;
            return LocationTrapCounts[locId] > 0;
        }

        public void ClearTrap(int locId)
        {
            if (locId >= 0 && locId < LocationTrapCounts.Length)
                LocationTrapCounts[locId] = 0;
        }

        public long ComputeStateHash()
        {
            long hash = 17;
            hash = hash * 31 + CurrentTick;
            hash = hash * 31 + CurrentTurn;
            hash = hash * 31 + (int)Phase;

            // WorldState 层全局状态
            hash = hash * 31 + (long)Random.State;
            hash = hash * 31 + ThornPotionTurnsRemaining;
            hash = hash * 31 + WoodSeedPlayer;
            hash = hash * 31 + WoodSeedLocation;
            hash = hash * 31 + (PaladinAegisUsed ? 1 : 0);
            for (int i = 0; i < LocationTrapCounts.Length; i++)
                hash = hash * 31 + LocationTrapCounts[i];
            for (int i = 0; i < MovedThisTurn.Length; i++)
                hash = hash * 31 + (MovedThisTurn[i] ? 1 : 0);

            // 每角色状态
            for (int i = 0; i < Characters.Length; i++)
            {
                Character c = Characters[i];
                if (c == null) continue;
                hash = hash * 31 + c.Id;
                hash = hash * 31 + c.CurrentLocationId;
                hash = hash * 31 + (c.IsDead ? 1 : 0);
                hash = hash * 31 + (int)(c.Sanity * 1000);
                hash = hash * 31 + c.ActionPoints;
                hash = hash * 31 + (int)(c.ErosionLimit * 1000);
                hash = hash * 31 + (int)(c.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense) * 1000);
                hash = hash * 31 + (int)(c.ASC.GetAttributeValue(AttributeConstants.MagicDefense) * 1000);
                hash = hash * 31 + (int)(c.ASC.GetAttributeValue(AttributeConstants.AttackBonus) * 1000);
                hash = hash * 31 + (int)(c.ASC.GetAttributeValue(AttributeConstants.MaxSanity) * 1000);
                hash = hash * 31 + (int)(c.ASC.GetAttributeValue(AttributeConstants.MaxActionPoints) * 1000);
                hash = hash * 31 + c.ASC.Tags.Mask;
                hash = hash * 31 + c.ASC.GetGuardianCharge();
                // GE 详情（替换原来的 ActiveEffectCount）
                hash = hash * 31 + c.ASC.ComputeEffectsHash();
                // 背包
                hash = hash * 31 + c.Inventory.EquippedWeaponAmmo;
                hash = hash * 31 + (c.Inventory.EquippedWeapon?.Id ?? 0);
                for (int j = 0; j < c.Inventory.Consumables.Count; j++)
                    hash = hash * 31 + c.Inventory.Consumables[j].Id;
            }

            // 地点规则状态
            for (int i = 0; i < Locations.Rules.Length; i++)
                hash = hash * 31 + (Locations.Rules[i]?.StateHash() ?? 0);
            hash = hash * 31 + Locations.ComputeItemsHash();

            return hash;
        }
    }
}
