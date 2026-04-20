using Core.GAS;

namespace Gameplay.Characters
{
    /// <summary>双子（Id=15，侵蚀 3 / 普攻 1）。docs/项目介绍.md 7.6。</summary>
    public static class TwinDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = SkillSystem.TwinCharacterId, Name = "双子",
                BaseSanity = 3, BaseErosionLimit = 3,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1, // 刀（附灵前置由 SkillSystem 特例校验 EquippedWeapon）
                Skills = new[]
                {
                    // 附灵·泣血（1 AP Self）：武器攻击 +0.5、攻击回 0.5（ReactiveSystem 按标签结算）
                    new SkillDef
                    {
                        Id = SkillSystem.TwinVampiricSkillId, Name = "附灵·泣血",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self
                    },
                    // 附灵·灵巧（1 AP Self）：武器可转换成任意可拾取武器（TwinConvertCommand）
                    new SkillDef
                    {
                        Id = SkillSystem.TwinAgileSkillId, Name = "附灵·灵巧",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self
                    }
                }
            };
        }
    }
}
