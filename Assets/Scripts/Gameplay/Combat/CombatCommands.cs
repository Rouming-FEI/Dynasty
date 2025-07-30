using Dynasty.Core;

namespace Dynasty.Gameplay
{
    public class DamageCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int TargetPlayerId { get; set; }
        public float Amount { get; set; }

        private static GameplayEffect _cachedDamageGE;

        public DamageCommand() { }
        public DamageCommand(int tick, int playerId, int targetPlayerId, float amount)
        {
            Tick = tick; PlayerId = playerId; TargetPlayerId = targetPlayerId; Amount = amount;
        }

        public void Execute(WorldState state)
        {
            Character source = state.GetCharacterByPlayer(PlayerId);
            Character target = state.GetCharacterByPlayer(TargetPlayerId);
            if (source == null || target == null) return;
            if (target.IsDead) return;
            if (source.ActionPoints <= 0) return;

            // Spend 1 AP
            source.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            // Create and apply damage GE
            GameplayEffectSpec spec = target.ASC.RentSpec();
            spec.Definition = GetDamageGE();
            spec.SourceId = source.Id;
            spec.TargetId = target.Id;
            spec.MagnitudeMultiplier = Amount;

            target.ASC.ApplyGameplayEffect(spec);

            // Internal event
            target.ASC.SendEvent(GameplayEventTypes.OnDamageTaken, source.Id, target.Id, Amount);

            // Check death
            if (target.ASC.GetAttributeValue(AttributeConstants.Sanity) <= 0f)
            {
                state.Commands.Enqueue(new DeathCommand(Tick, TargetPlayerId, PlayerId));
            }
        }

        private static GameplayEffect GetDamageGE()
        {
            if (_cachedDamageGE != null) return _cachedDamageGE;
            _cachedDamageGE = new GameplayEffect
            {
                Id = 1,
                DurationType = GameplayEffectDurationType.Instant,
                Modifiers = new[]
                {
                    new AttributeModifier(AttributeConstants.Sanity, AttributeModifierOp.Add, -1f),
                },
            };
            return _cachedDamageGE;
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
