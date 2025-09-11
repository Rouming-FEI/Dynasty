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
