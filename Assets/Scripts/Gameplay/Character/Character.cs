using Dynasty.Core;

namespace Dynasty.Gameplay
{
    public class Character
    {
        public int Id;
        public int PlayerId;
        public string Name;
        public AbilitySystemComponent ASC;
        public int CurrentLocationId;

        public Character(int id, int playerId, string name)
        {
            Id = id;
            PlayerId = playerId;
            Name = name;
            ASC = new AbilitySystemComponent(id);
            ASC.SetAttributeSet(new DefaultAttributeSet());
            CurrentLocationId = -1;
        }

        public int ActionPoints => (int)ASC.GetAttributeValue(AttributeConstants.ActionPoints);
        public float Sanity => ASC.GetAttributeValue(AttributeConstants.Sanity);
        public float MaxSanity => ASC.GetAttributeValue(AttributeConstants.MaxSanity);
        public float ErosionLimit => ASC.GetAttributeValue(AttributeConstants.ErosionLimit);
        public bool IsDead => ASC.IsDead;
        public bool CanAct => ASC.CanAct;
    }
}
