namespace Core.GAS
{
    public enum AttributeModifierOp : byte
    {
        Add,
        Multiply,
        Override
    }

    public readonly struct AttributeModifier
    {
        public readonly int AttributeId;
        public readonly AttributeModifierOp Op;
        public readonly float Magnitude;
        public readonly int Channel;

        public AttributeModifier(int attributeId, AttributeModifierOp op, float magnitude, int channel = 0)
        {
            AttributeId = attributeId;
            Op = op;
            Magnitude = magnitude;
            Channel = channel;
        }
    }
}
