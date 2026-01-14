using Gameplay.Characters;
using Gameplay.Items;
using Gameplay.Locations;
using Core;
using Core.GAS;
using Core.Command;

namespace Gameplay.Movement
{
    public class MoveCommand : ICommand
    {
        /// <summary>旅行家「旅行」技能 Id：持有者可 1AP 前往任意地点（docs/项目介绍.md 7.4）。</summary>
        private const int TravelSkillId = 40;

        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int TargetLocationId { get; set; }

        public MoveCommand() { }
        public MoveCommand(int tick, int playerId, int targetId)
        {
            Tick = tick; PlayerId = playerId; TargetLocationId = targetId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanMove) return;
            if (c.ASC.Tags.HasTag(GameplayTagConstants.Bound)) return; // 束缚：不可移动
            if (!state.Locations.IsValidLocation(TargetLocationId)) return;
            // 邻接校验（防御纵深，UI 按钮过期时拦截非法传送）：普通移动仅允许相邻地点。
            // 例外：旅行家凭旅行 (Id=40) 或免费旅行标记可前往任意地点（技能验收特例
            // 「MoveCommand 任意地点可达」）；非法目标整体忽略——不扣 AP、不触发规则，无日志。
            if (!c.ASC.Tags.HasTag(GameplayTagConstants.FreeTravel) && !HasTravelSkill(c))
            {
                int[] connected = state.Locations.GetConnectedLocations(c.CurrentLocationId);
                bool adjacent = false;
                for (int i = 0; i < connected.Length; i++)
                {
                    if (connected[i] == TargetLocationId) { adjacent = true; break; }
                }
                if (!adjacent) return;
            }
            if (c.ActionPoints <= 0) return;

            // 地点准入检查（如实验室大门关闭时拒绝进入），拒绝时不消耗 AP
            LocationRule targetRule = state.Locations.Rules[TargetLocationId];
            if (targetRule != null && !targetRule.CanEnter(state, c)) return;

            // 免费旅行标记：旅行家攒满充能后的 0 AP 移动
            bool freeTravel = c.ASC.Tags.HasTag(GameplayTagConstants.FreeTravel);
            if (freeTravel)
            {
                c.ASC.Tags.RemoveTag(GameplayTagConstants.FreeTravel);
                c.ASC.ResetGuardianCharge(); // 免费旅行消费后清空充能（仅旅行家持有该标记）
            }
            else
                c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            int oldLoc = c.CurrentLocationId;

            // 离开旧地点：触发地点规则 OnLeave（如藏匿处移除隐身标签）
            if (oldLoc >= 0 && oldLoc < state.Locations.LocationCount)
                state.Locations.Rules[oldLoc]?.OnLeave(state, c);

            c.CurrentLocationId = TargetLocationId;

            // 进入新地点：触发地点规则 OnEnter（如藏匿处添加隐身标签）
            state.Locations.Rules[TargetLocationId]?.OnEnter(state, c);

            // 标记本回合已移动（供荆棘素 DOT 判定）
            if (PlayerId >= 0 && PlayerId < state.MovedThisTurn.Length)
                state.MovedThisTurn[PlayerId] = true;

            // 到达新地点后检查触发式炸弹
            TrapSystem.CheckTrigger(state, TargetLocationId, c);

            var evt = new GameplayEvent(GameplayEventTypes.EntityMoved, PlayerId, c.Id)
            {
                IntArg1 = oldLoc,
                IntArg2 = TargetLocationId
            };
            state.Events.Dispatch(GameplayEventTypes.EntityMoved, ref evt);

            // 到达新地点后掉落物可通过 Locations.GetRemainingItemCount / GetLocatedItems 查询，供 UI 展示
        }

        /// <summary>是否持有旅行技能（任意地点移动的资格来源；FreeTravel 免费标记亦仅旅行家可获得）。</summary>
        private static bool HasTravelSkill(Character c)
        {
            SkillDef[] skills = c.Definition?.Skills;
            if (skills == null) return false;
            for (int i = 0; i < skills.Length; i++)
                if (skills[i] != null && skills[i].Id == TravelSkillId) return true;
            return false;
        }
    }

    public class WasteAPCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }

        public WasteAPCommand() { }
        public WasteAPCommand(int tick, int playerId) { Tick = tick; PlayerId = playerId; }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || c.ActionPoints <= 0) return;
            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
        }
    }
}
