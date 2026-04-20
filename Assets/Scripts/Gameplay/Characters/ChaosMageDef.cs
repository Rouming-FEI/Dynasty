using Core.GAS;

namespace Gameplay.Characters
{
    /// <summary>混沌法师（Id=14，侵蚀 4 / 普攻 1）。docs/项目介绍.md 7.5。</summary>
    public static class ChaosMageDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = SkillSystem.ChaosMageCharacterId, Name = "混沌法师",
                BaseSanity = 4, BaseErosionLimit = 4,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 0, // 初始无武器（普通攻击徒手 1）
                Skills = new[]
                {
                    // 混乱（1 AP，任意目标/可无目标）：模拟使用一件道具/武器。
                    // 真实语义由 ChaosSimulateCommand 承载；Def 提供消耗/目标描述与 UI/AI 展示。
                    new SkillDef
                    {
                        Id = SkillSystem.ChaosSkillId, Name = "混乱",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Any
                    },
                    // 反噬（被动占位）：判定逻辑内联于 ChaosSimulateCommand（Def 无负载）。
                    new SkillDef { Id = SkillSystem.ChaosBackfireSkillId, Name = "反噬", Activation = SkillActivationType.Passive },
                    // 地狱的子民（被动占位）：拾取/使用拦截在 ItemCommands 按 Definition.Id==14 特例。
                    new SkillDef { Id = SkillSystem.HellChildSkillId, Name = "地狱的子民", Activation = SkillActivationType.Passive }
                }
            };
        }
    }
}
