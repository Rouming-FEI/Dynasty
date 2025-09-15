using Core.GAS;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
using Gameplay.Judgment;
using System;

namespace Core
{
    public class TurnManager
    {
        private JudgmentSystem _judgment;
        private int _consecutiveFlatCount;
        private int _currentActorIndex;
        private int[] _actionOrder;

        public TurnManager()
        {
            _judgment = new JudgmentSystem();
            _consecutiveFlatCount = 0;
            _actionOrder = Array.Empty<int>();
        }

        public void StartGame(WorldState state)
        {
            state.Phase = TurnPhase.JudgmentPhase;
            state.CurrentTurn = 1;

            // Non-hot-path one-time init: registry + def allocations are acceptable here
            ItemRegistry.Initialize();

            // Player 0 = 守护者, Player 1 = 魑魅, Player 2 = 普通人, then cycle
            CharacterDef[] defs =
            {
                GuardianDef.Create(),
                CharmSpiritDef.Create(),
                NormalPersonDef.Create()
            };

            for (int i = 0; i < state.PlayerCount; i++)
            {
                CharacterDef def = defs[i % defs.Length];
                Character c = new Character(i + 1, i, def.Name)
                {
                    CurrentLocationId = i // spawn at own entry point (0-3)
                };
                def.ApplyTo(c);
                GrantInitialItems(state, c, def);
                state.Characters[i] = c;
            }

            // 守护者被动「重装」：开局获得护盾
            Character guardian = state.GetCharacterByPlayer(0);
            if (guardian != null)
                ShieldSystem.GrantShield(state, guardian.ASC, guardian.Id);

            // Pre-allocate action order array (sized once, reused every turn)
            _actionOrder = new int[state.PlayerCount];

            var evt = new GameplayEvent(GameplayEventTypes.TurnStart, -1, -1);
            state.Events.Dispatch(GameplayEventTypes.TurnStart, ref evt);
        }

        private static void GrantInitialItems(WorldState state, Character c, CharacterDef def)
        {
            // 角色定义自带的初始道具（0 = 无）
            if (def.InitialItem > 0)
                AddItemToInventory(c, ItemRegistry.Get(def.InitialItem));

            // 每人开局 1 个血包（安全屋血包尚无拾取机制，先作为初始补给发放）
            AddItemToInventory(c, ItemRegistry.Get(2));

            // 出生地点的初始道具
            int[] locationItems = state.Locations.GetInitialItems(c.CurrentLocationId);
            if (locationItems != null)
                for (int i = 0; i < locationItems.Length; i++)
                    AddItemToInventory(c, ItemRegistry.Get(locationItems[i]));
        }

        private static void AddItemToInventory(Character c, ItemDef item)
        {
            if (item == null) return;
            if (item.Type == ItemType.Weapon)
                c.Inventory.EquipWeapon(item);
            else
                c.Inventory.AddConsumable(item); // 满 4 个即弃（返回 false）
        }

        public bool Advance(WorldState state, Span<ChipAllocation> inputs)
        {
            switch (state.Phase)
            {
                case TurnPhase.JudgmentPhase:
                    ProcessJudgment(state, inputs);
                    break;
                case TurnPhase.ActionPhase:
                    if (AllAPExhausted(state))
                        EndTurn(state);
                    break;
                case TurnPhase.GameOver:
                    return false;
            }
            return state.Phase != TurnPhase.GameOver;
        }

        private void ProcessJudgment(WorldState state, Span<ChipAllocation> allocations)
        {
            JudgmentResult result = _judgment.Resolve(allocations, state.Random, ref _consecutiveFlatCount);
            state.LastJudgment = result;
            state.HasJudgmentResult = true;

            // 拷贝筹码分布供日志显示（定长数组，确定性安全）
            for (int i = 0; i < allocations.Length && i < state.LastAllocations.Length; i++)
                state.LastAllocations[i] = allocations[i];

            // Distribute AP to all players
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character c = state.GetCharacterByPlayer(i);
                if (c != null && !c.IsDead)
                {
                    int ap = result.APGained[i];
                    c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, ap);
                    // Re-set current value to match
                    c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, 0f);
                }
            }

            // Build action order: winners desc, losers desc, neutrals last
            BuildActionOrder(state, result);

            // Fire events
            var evt = new GameplayEvent(GameplayEventTypes.JudgmentResolved, -1, -1);
            state.Events.Dispatch(GameplayEventTypes.JudgmentResolved, ref evt);

            state.Phase = TurnPhase.ActionPhase;
            _currentActorIndex = 0;

            var evt2 = new GameplayEvent(GameplayEventTypes.ActionPhaseStart, -1, -1);
            state.Events.Dispatch(GameplayEventTypes.ActionPhaseStart, ref evt2);
        }

        private void BuildActionOrder(WorldState state, JudgmentResult result)
        {
            int playerCount = state.PlayerCount;
            int idx = 0;

            // Winners first, sorted by effective chips desc; then losers
            for (int pass = 0; pass < 2; pass++)
            {
                // Collect indices for this role group
                Span<int> group = stackalloc int[playerCount];
                int groupCount = 0;
                JudgmentRole targetRole = (pass == 0) ? JudgmentRole.Winner : JudgmentRole.Loser;

                for (int i = 0; i < playerCount; i++)
                    if (result.Roles[i] == targetRole)
                        group[groupCount++] = i;

                // Sort by effective chips desc within group (selection sort, small N)
                for (int i = 0; i < groupCount - 1; i++)
                {
                    int best = i;
                    for (int j = i + 1; j < groupCount; j++)
                    {
                        if (result.EffectiveChips[group[j]] > result.EffectiveChips[group[best]])
                            best = j;
                        else if (result.EffectiveChips[group[j]] == result.EffectiveChips[group[best]])
                            if (state.Random.Next(2) == 0) best = j;
                    }
                    int temp = group[i];
                    group[i] = group[best];
                    group[best] = temp;
                }

                for (int i = 0; i < groupCount; i++)
                    _actionOrder[idx++] = group[i];
            }

            // Neutrals last
            for (int i = 0; i < playerCount; i++)
                if (result.Roles[i] == JudgmentRole.Neutral)
                    _actionOrder[idx++] = i;
        }

        private bool AllAPExhausted(WorldState state)
        {
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character c = state.Characters[i];
                if (c != null && !c.IsDead && c.ActionPoints > 0)
                    return false;
            }
            return true;
        }

        private void EndTurn(WorldState state)
        {
            state.Phase = TurnPhase.TurnEnd;

            var turnEndEvt = new GameplayEvent(GameplayEventTypes.TurnEnd, -1, -1);
            state.Events.Dispatch(GameplayEventTypes.TurnEnd, ref turnEndEvt);

            // Tick all GEs
            for (int i = 0; i < state.PlayerCount; i++)
            {
                state.Characters[i]?.ASC.TickEffects(state);
                state.Characters[i]?.ASC.ProcessEvents();
            }

            // 荆棘素全局 DOT：回合末对移动过且携带标签的角色造成 0.5 效果伤害
            if (state.ThornPotionTurnsRemaining > 0)
            {
                for (int i = 0; i < state.PlayerCount; i++)
                {
                    Character ch = state.Characters[i];
                    if (ch == null || ch.IsDead) continue;
                    if (!state.MovedThisTurn[i]) continue;
                    if (!ch.ASC.Tags.HasTag(GameplayTagConstants.ThornPotionActive)) continue;

                    // 荆棘反噬事件（先派发原因，再走伤害管线，日志顺序自然）
                    var thornEvt = new GameplayEvent(GameplayEventTypes.ThornDotTick, -1, ch.Id);
                    state.Events.Dispatch(GameplayEventTypes.ThornDotTick, ref thornEvt);

                    DamagePipeline.ApplyDamage(state, -1, ch.Id, 0.5f, DamageType.Effect);
                }
                state.ThornPotionTurnsRemaining--;
            }

            // 重置本回合移动记录
            for (int i = 0; i < state.MovedThisTurn.Length; i++)
                state.MovedThisTurn[i] = false;

            // 地点规则回合结束钩子（如实验室深度潮汐周期判定）
            var locationRules = state.Locations.Rules;
            for (int i = 0; i < locationRules.Length; i++)
                locationRules[i]?.OnTurnEnd(state);

            // 结算回合末伤害入队的命令（DOT/荆棘素/潮汐经 DamagePipeline 产生的
            // DeathCommand 等）。不在此执行的话，死亡会延迟到下一行动阶段才生效，
            // 下面的存活人数检查也会基于过期状态。
            state.Commands.ExecuteAll(state);

            // Check game over
            if (state.AliveCharacterCount <= 1)
            {
                state.Phase = TurnPhase.GameOver;
                var evt = new GameplayEvent(GameplayEventTypes.GameOver, -1, -1);
                state.Events.Dispatch(GameplayEventTypes.GameOver, ref evt);
                return;
            }

            // Check max turns
            if (state.CurrentTurn >= GameConfig.MaxTurns)
            {
                state.Phase = TurnPhase.GameOver;
                var evt = new GameplayEvent(GameplayEventTypes.GameOver, -1, -1);
                state.Events.Dispatch(GameplayEventTypes.GameOver, ref evt);
                return;
            }

            // Next turn
            state.CurrentTurn++;
            state.Phase = TurnPhase.JudgmentPhase;
            var evt2 = new GameplayEvent(GameplayEventTypes.TurnStart, -1, -1);
            state.Events.Dispatch(GameplayEventTypes.TurnStart, ref evt2);
        }

        // Public accessor for action order (used by LocalLockstep)
        public int[] ActionOrder => _actionOrder;
        public int CurrentActorIndex => _currentActorIndex;
        public void IncrementActorIndex() => _currentActorIndex++;
    }
}
