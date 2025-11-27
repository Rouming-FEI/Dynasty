using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Judgment;

namespace Gameplay.Characters
{
    /// <summary>飞剑：与目标单独判定，胜 → 1.5 物理伤（鬼化攻击加成自动生效）。</summary>
    public class FlyingSwordCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int TargetPlayerId { get; set; }

        public FlyingSwordCommand() { }
        public FlyingSwordCommand(int tick, int playerId, int targetPlayerId)
        {
            Tick = tick; PlayerId = playerId; TargetPlayerId = targetPlayerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            Character t = state.GetCharacterByPlayer(TargetPlayerId);
            if (c == null || t == null || c.IsDead || t.IsDead) return;
            if (c.ActionPoints <= 0) return;
            if (t.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden) &&
                !c.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden)) return;

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            // 目标列表：鬼化且侵蚀 ≤ 3.5 → 第二目标（数组序第一个其他存活角色）
            DuelTarget(state, c, t);
            if (c.ASC.Tags.HasTag(GameplayTagConstants.GhostForm) &&
                c.ASC.GetAttributeValue(AttributeConstants.ErosionLimit) <= 3.5f)
            {
                Character second = null;
                for (int i = 0; i < state.Characters.Length; i++)
                {
                    Character ch = state.Characters[i];
                    if (ch == null || ch.IsDead || ch.Id == c.Id || ch.Id == t.Id) continue;
                    if (ch.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden) &&
                        !c.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden)) continue;
                    second = ch;
                    break;
                }
                if (second != null) DuelTarget(state, c, second);
            }
        }

        private static void DuelTarget(WorldState state, Character c, Character t)
        {
            if (JudgmentDuel.Resolve(state, c, t))
                DamagePipeline.ApplyDamage(state, c.Id, t.Id, 1.5f, DamageType.Physical);
        }
    }
}
