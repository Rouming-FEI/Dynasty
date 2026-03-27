using Gameplay.Characters;
using Core;
using Core.GAS;
using Core.Command;

namespace Gameplay.Combat
{
    public class DamageCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int TargetPlayerId { get; set; }
        public float Amount { get; set; }
        public DamageType Type { get; set; }

        public DamageCommand() { }
        public DamageCommand(int tick, int playerId, int targetPlayerId, float amount,
            DamageType type = DamageType.Physical)
        {
            Tick = tick; PlayerId = playerId; TargetPlayerId = targetPlayerId; Amount = amount;
            Type = type;
        }

        public void Execute(WorldState state)
        {
            Character source = state.GetCharacterByPlayer(PlayerId);
            Character target = state.GetCharacterByPlayer(TargetPlayerId);
            if (source == null || target == null) return;
            if (!source.CanAct) return; // 死亡/眩晕/塔上(OnTurret 附带 CannotAct)不可攻击
            if (target.IsDead) return;
            if (source.ActionPoints <= 0) return;
            if (source.ASC.Tags.HasTag(GameplayTagConstants.GhostForm)) return; // 灵体状态禁普通攻击

            // 数据幽灵形态攻击（V1-V5 全形态）：内部完成目标校验/扣 AP（S10/S11 语义）
            if (source.Definition?.Id == SkillSystem.DataGhostCharacterId)
            {
                DataGhostFormAttack.Execute(state, source, target);
                return;
            }

            // Spend 1 AP
            source.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            // Unified damage pipeline: immunity -> attack bonus -> shield ->
            // defense -> GE apply -> OnDamageTaken -> death check
            DamagePipeline.ApplyDamage(state, source.Id, target.Id, Amount, Type);
        }
    }

    public class DeathCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }        // The dying player
        public int KillerPlayerId { get; set; }

        public DeathCommand() { }
        public DeathCommand(int tick, int playerId, int killerPlayerId)
        {
            Tick = tick; PlayerId = playerId; KillerPlayerId = killerPlayerId;
        }

        public void Execute(WorldState state)
        {
            Character target = state.GetCharacterByPlayer(PlayerId);
            if (target == null || target.IsDead) return;

            // 木灵种子复活：死亡时种子生效 → 不死亡，复活于种子地点
            if (state.WoodSeedPlayer == PlayerId && state.WoodSeedLocation >= 0)
            {
                target.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0.5f);
                target.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 3f);
                target.CurrentLocationId = state.WoodSeedLocation;
                state.WoodSeedPlayer = -1;
                state.WoodSeedLocation = -1;
                return;
            }

            // 阵法大师血炼复活：阵盘 ≥ 3 → 复活（理智回满，消耗 3 阵盘）
            if (target.ASC.GetBloodSeals() >= 3)
            {
                target.ASC.ModifyBloodSeals(-3);
                target.ASC.SetAttributeBaseValue(AttributeConstants.Sanity,
                    target.ASC.GetAttributeValue(AttributeConstants.MaxSanity));
                return;
            }

            // 代行者主的庇护：致命伤 → 理智 0.5 + 本能 +2（免死）
            if (target.Definition?.Id == 13)
            {
                target.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0.5f);
                target.ASC.ModifyInstinct(2);
                return;
            }

            // Internal death event -> adds Dead tag
            target.ASC.SendEvent(GameplayEventTypes.OnDeath, KillerPlayerId, target.Id, 0f);
            target.ASC.ProcessEvents();

            // Clear remaining AP
            int ap = target.ActionPoints;
            target.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -(float)ap);

            // Global event
            var evt = new GameplayEvent(GameplayEventTypes.PlayerDied, PlayerId, target.Id)
            {
                IntArg1 = KillerPlayerId
            };
            state.Events.Dispatch(GameplayEventTypes.PlayerDied, ref evt);
        }
    }
}
