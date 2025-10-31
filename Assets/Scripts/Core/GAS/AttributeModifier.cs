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
        /// <summary>依赖的 modifier Channel（0 = 无依赖）。被依赖者先结算。</summary>
        public readonly int DependsOn;

        public AttributeModifier(int attributeId, AttributeModifierOp op, float magnitude, int channel = 0, int dependsOn = 0)
        {
            AttributeId = attributeId;
            Op = op;
            Magnitude = magnitude;
            Channel = channel;
            DependsOn = dependsOn;
        }
    }
}
