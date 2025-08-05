namespace Core.GAS
{
    /// <summary>
    /// 伤害类型。物理/法术/效果伤害可被对应免疫标签抵挡，真实伤害无视免疫与护盾。
    /// </summary>
    public enum DamageType : byte
    {
        Physical,
        Magic,
        Effect,
        True
    }
}
