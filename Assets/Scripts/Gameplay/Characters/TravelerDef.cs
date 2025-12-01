using Core.GAS;

namespace Gameplay.Characters
{
    public static class TravelerDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 6, Name = "旅行家",
                BaseSanity = 3, BaseErosionLimit = 3,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1,
                Skills = new[]
                {
                    // 旅行（1AP 任意地点）：MoveCommand 本身不限相邻，普通移动即任意地点；
                    // 技能本体仅作文档占位，行为与普通移动一致（特例表验证任意地点可达）
                    new SkillDef
                    {
                        Id = 40, Name = "旅行",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self
                    },
                    // 旅途的准备（2AP 可攒）：1AP 攒 1 充能，满 2 免费执行一次旅行
                    new SkillDef
                    {
                        Id = 41, Name = "旅途的准备",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Self,
                        ChargeThreshold = 2
                    }
                }
            };
        }
    }
}
