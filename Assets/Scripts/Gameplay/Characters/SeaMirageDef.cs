using Core.GAS;

namespace Gameplay.Characters
{
    public static class SeaMirageDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 9, Name = "海幻",
                BaseSanity = 3, BaseErosionLimit = 3,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1,
                Skills = new[]
                {
                    // 幻剑（1AP 取得归鞘刀；归鞘持刀 0.5 全减伤；拔刀 1AP 激活蜃景；归鞘 0AP）
                    new SkillDef
                    {
                        Id = 70, Name = "幻剑",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self,
                        BlockingTags = new[] { GameplayTagConstants.BladeSheathed, GameplayTagConstants.BladeDrawn }
                    },
                    // 蜃景（1AP：1 远程法术伤 + 判定胜瞬移；目标物理防御 ≥1 额外 0.5）
                    new SkillDef
                    {
                        Id = 71, Name = "蜃景",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Enemy,
                        BlockingTags = new[] { GameplayTagConstants.BladeSheathed }, // 拔刀后才可用
                        RequiredTags = new[] { GameplayTagConstants.BladeDrawn }
                    }
                }
            };
        }
    }
}
