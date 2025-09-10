using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Characters;

namespace Gameplay.Locations
{
    /// <summary>
    /// 开/关实验室大门：消耗 1 AP。
    /// 可从实验室内部或与实验室相邻的地点操作（门在实验室入口，远程无法触及）。
    /// </summary>
    public class ToggleDoorCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }

        public ToggleDoorCommand() { }
        public ToggleDoorCommand(int tick, int playerId)
        {
            Tick = tick; PlayerId = playerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct || c.ActionPoints <= 0) return;

            if (state.Locations.Rules[LabRule.LabLocationId] is not LabRule lab) return;
            if (!IsAtOrAdjacentToLab(state, c)) return;

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
            lab.ToggleDoor();
        }

        private static bool IsAtOrAdjacentToLab(WorldState state, Character c)
        {
            if (c.CurrentLocationId == LabRule.LabLocationId) return true;
            int[] connections = state.Locations.GetConnectedLocations(LabRule.LabLocationId);
            for (int i = 0; i < connections.Length; i++)
                if (connections[i] == c.CurrentLocationId) return true;
            return false;
        }
    }

    /// <summary>
    /// 开/关深度潮汐开关：消耗 1 AP。开关设施在实验室内，必须身处实验室才能操作。
    /// 切换会重置潮汐周期计数（见 LabRule.ToggleSwitch）。
    /// </summary>
    public class ToggleTideSwitchCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }

        public ToggleTideSwitchCommand() { }
        public ToggleTideSwitchCommand(int tick, int playerId)
        {
            Tick = tick; PlayerId = playerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct || c.ActionPoints <= 0) return;
            if (c.CurrentLocationId != LabRule.LabLocationId) return;

            if (state.Locations.Rules[LabRule.LabLocationId] is not LabRule lab) return;

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
            lab.ToggleSwitch();
        }
    }

    /// <summary>
    /// 拆除阈值器：消耗 1 AP。设施在实验室内，必须身处实验室才能操作。
    /// 拆除后不可装回，重复拆除无效（不消耗 AP）。
    /// </summary>
    public class RemoveThresholdCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }

        public RemoveThresholdCommand() { }
        public RemoveThresholdCommand(int tick, int playerId)
        {
            Tick = tick; PlayerId = playerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct || c.ActionPoints <= 0) return;
            if (c.CurrentLocationId != LabRule.LabLocationId) return;

            if (state.Locations.Rules[LabRule.LabLocationId] is not LabRule lab) return;
            if (lab.ThresholdRemoved) return; // 已拆除，无效操作不扣 AP

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
            lab.RemoveThreshold();
        }
    }
}
