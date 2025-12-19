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
