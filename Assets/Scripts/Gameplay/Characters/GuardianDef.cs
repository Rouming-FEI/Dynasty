using Core.GAS;

namespace Gameplay.Characters
{
    public static class GuardianDef
    {
        public static CharacterDef Create()
        {
            return new CharacterDef
            {
                Id = 1, Name = "守护者",
                BaseSanity = 3, BaseErosionLimit = 3,
                BasePhysicalDefense = 0, BaseMagicDefense = 0,
                InitialItem = 1, // 刀
                Skills = new[]
                {
                    // 重装 (被动) — StartGame 时通过 ASC 加 HasShield 标签
                    new SkillDef { Id = 1, Name = "重装", Activation = SkillActivationType.Passive },

                    // 守护 (主动, 3AP 可攒) — 每次消耗 1AP 攒 1 充能（跨回合保留），
                    // 攒满 3 充能免费兑换 1 护盾并清零（文档 7.3「3AP 可攒」）。
                    // 守护之光（Reactive）属文档 7.10 圣骑士，此前误置于此，已移除。
                    new SkillDef
                    {
                        Id = 2, Name = "守护",
                        Cost = new SkillCost(SkillCostType.AP, 1),
                        Activation = SkillActivationType.Active,
                        Target = SkillTargetType.Any, // 可为任意角色赋予护盾
                        ChargeThreshold = 3
                    },
                }
            };
        }
    }
}
