using Dynasty.Gameplay;

namespace Dynasty.Core
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
        public int CurrentTick;
        public int CurrentTurn;
        public TurnPhase Phase;
        public int PlayerCount;

        public WorldState(int playerCount, int randomSeed = 42)
        {
            PlayerCount = playerCount;
            Characters = new Character[playerCount];
            Events = new EventBus();
            Commands = new CommandQueue();
            Random = new DeterministicRandom(randomSeed);
            Locations = new LocationManager();
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

        public long ComputeStateHash()
        {
            long hash = 17;
            hash = hash * 31 + CurrentTick;
            hash = hash * 31 + CurrentTurn;
            hash = hash * 31 + (int)Phase;
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
                hash = hash * 31 + (int)(c.ASC.GetAttributeValue(AttributeConstants.PhysicalResist) * 1000);
                hash = hash * 31 + c.ASC.ActiveEffectCount;
            }
            return hash;
        }
    }
}
