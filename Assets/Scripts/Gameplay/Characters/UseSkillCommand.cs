using Core;
using Core.Command;
using Core.GAS;

namespace Gameplay.Characters
{
    /// <summary>使用技能：按索引取角色定义中的技能，交由 SkillSystem 校验并激活。</summary>
    public class UseSkillCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int SkillIndex { get; set; }
        public int TargetPlayerId { get; set; }

        public UseSkillCommand() { }
        public UseSkillCommand(int tick, int playerId, int skillIndex, int targetPlayerId)
        {
            Tick = tick; PlayerId = playerId; SkillIndex = skillIndex; TargetPlayerId = targetPlayerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            Character target = state.GetCharacterByPlayer(TargetPlayerId);
            if (c?.Definition?.Skills == null) return;
            if (SkillIndex < 0 || SkillIndex >= c.Definition.Skills.Length) return;

            SkillDef skill = c.Definition.Skills[SkillIndex];
            SkillSystem.Activate(skill, c, target, state);
        }
    }
}
