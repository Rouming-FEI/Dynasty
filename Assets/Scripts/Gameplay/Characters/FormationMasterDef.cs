using Core.GAS;

namespace Gameplay.Characters
{
    public static class FormationMasterDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 11, Name = "阵法大师",
                BaseSanity = 3, BaseErosionLimit = 2,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1,
                Skills = new[]
                {
                    // 束缚（1AP：目标 2 回合不可移动 + 每回合末 0.5 物理效果伤）
                    new SkillDef
                    {
                        Id = 90, Name = "束缚",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Enemy
                    },
                    // 血炼（被动：阵盘计数，初始 2；束缚 -1 / 减伤 -1 / 复活 -3）
                    new SkillDef { Id = 91, Name = "血炼", Activation = SkillActivationType.Passive },
                    // 阵法回路（被动：应急装置不加血限；杀人判定）
                    new SkillDef
                    {
                        Id = 92, Name = "阵法回路",
                        Activation = SkillActivationType.Reactive,
                        ReactiveTrigger = ReactiveTriggerType.OnKill
                    }
                }
            };
        }
    }
}
