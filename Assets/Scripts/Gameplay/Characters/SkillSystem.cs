using Core;
using Core.GAS;
using Gameplay.Combat;
using Gameplay.Items;
using Gameplay.Judgment;

namespace Gameplay.Characters
{
    /// <summary>
    /// 技能激活逻辑 — 前置校验（Tag 条件 + AP 消耗）→ 支付消耗 → 对目标应用 GE。
    /// 纯静态类，无状态；Hot Path 零 GC（数组 for 循环 + Spec 池化）。
    /// </summary>
    public static class SkillSystem
    {
        /// <summary>守护技能 Id（GuardianDef 中定义，全技能表内唯一）。</summary>
        public const int GuardianSkillId = 2;
        /// <summary>P7-A 角色 Id（新角色全局常量，禁止裸数字）。</summary>
        public const int ChaosMageCharacterId = 14;
        public const int TwinCharacterId = 15;
        public const int DataGhostCharacterId = 16;
        /// <summary>P7-A 技能 Id（混沌法师 120-122 / 双子 123-124 / 数据幽灵 125-129）。</summary>
        public const int ChaosSkillId = 120;
        public const int ChaosBackfireSkillId = 121;  // 被动占位（逻辑在 ChaosSimulateCommand 内联）
        public const int HellChildSkillId = 122;      // 被动占位（逻辑在 ItemCommands 拦截）
        public const int TwinVampiricSkillId = 123;
        public const int TwinAgileSkillId = 124;
        public const int HandsonicSkillId = 125;
        public const int HarmonicsSkillId = 126;
        public const int DisciplineSkillId = 127;     // Reactive OnPreDamage
        public const int AbyssSkillId = 128;          // Reactive OnKill
        public const int CloneDrainSkillId = 129;     // 周期被动 Interval=1（分身回合末扣 1）
        /// <summary>技能是否可激活：仅限主动技能、施放者存活可行动、满足 RequiredTags、不含 BlockingTags、AP 足够。</summary>
        /// <param name="skillIndex">技能在 CharacterDef.Skills 中的索引；-1 = 不检查冷却（既有调用点兼容）。</param>
        public static bool CanActivate(SkillDef skill, Character c, int skillIndex = -1)
        {
            if (skill == null || c == null || !c.CanAct) return false;
            if (skill.Activation != SkillActivationType.Active) return false; // 被动/响应型技能不可主动施放
            if (skill.RequiredTags != null && !c.ASC.Tags.HasAll(skill.RequiredTags)) return false;
            if (skill.BlockingTags != null && c.ASC.Tags.HasAny(skill.BlockingTags)) return false;
            if (skill.Cost.Type == SkillCostType.AP)
            {
                // 守护满充能时免费给盾，不检查 AP（阈值取 skill.ChargeThreshold，0 值兜底 3）
                bool freeGrant = (skill.Id == GuardianSkillId
                    && (skill.ChargeThreshold > 0 ? skill.ChargeThreshold : 3) <= c.ASC.GetGuardianCharge());
                if (!freeGrant && c.ActionPoints < skill.Cost.Amount) return false;
            }
            if (skillIndex >= 0 && c.ASC.GetCooldown(skillIndex) > 0) return false;
            if (!HasResources(skill, c)) return false;
            return true;
        }

        private static bool HasResources(SkillDef skill, Character c)
        {
            // 主成本 AP 已在上面的守护免费分支里检查（freeGrant 满充能时 0 AP 免费施放），
            // 此处只补充非 AP 主成本 + 额外成本 + 前置条件，避免重复校验破坏守护免费分支。
            if (skill.Cost.Type != SkillCostType.AP && !HasResource(c, skill.Cost)) return false;
            if (skill.ExtraCosts != null)
                for (int i = 0; i < skill.ExtraCosts.Length; i++)
                    if (!HasResource(c, skill.ExtraCosts[i])) return false;
            if (skill.Restrictions != null)
                for (int i = 0; i < skill.Restrictions.Length; i++)
                {
                    SkillRestriction r = skill.Restrictions[i];
                    if (c.ASC.GetAttributeValue(r.AttributeId) < r.MinValue) return false;
                }
            return true;
        }

        private static bool HasResource(Character c, SkillCost cost)
        {
            switch (cost.Type)
            {
                case SkillCostType.AP: return c.ActionPoints >= cost.Amount;
                case SkillCostType.Sanity: return c.ASC.GetAttributeValue(AttributeConstants.Sanity) >= cost.Amount;
                case SkillCostType.ErosionLimit: return c.ASC.GetAttributeValue(AttributeConstants.ErosionLimit) >= cost.Amount;
                case SkillCostType.Charge: return c.ASC.GetGuardianCharge() >= cost.Amount;
                default: return false;
            }
        }

        /// <summary>激活技能：支付消耗后，将技能携带的全部 GE 应用到目标身上。</summary>
        /// <param name="skillIndex">技能在 CharacterDef.Skills 中的索引；-1 = 不记录冷却（既有调用点兼容）。</param>
        public static void Activate(SkillDef skill, Character c, Character target, WorldState state, int skillIndex = -1)
        {
            if (!CanActivate(skill, c, skillIndex)) return;
            if (target == null || target.IsDead) return;

            // 技能释放事件（校验通过即派发；时间线展示用，不影响确定性）
            var skillEvt = new GameplayEvent(GameplayEventTypes.SkillUsed, c.Id, target.Id)
            {
                IntArg1 = skill.Id
            };
            state.Events.Dispatch(GameplayEventTypes.SkillUsed, ref skillEvt);

            // 冷却记录：施放后按 CooldownTurns 锁定（skillIndex >= 0 才记录）
            if (skillIndex >= 0 && skill.CooldownTurns > 0)
                c.ASC.SetCooldown(skillIndex, skill.CooldownTurns);

            // 守护 (Id=2) 充能机制：总价 3AP 可攒 ——
            // - 充能未满时：花 1AP 攒 1 层充能，不关心目标
            // - 充能满时：免费（0AP）给目标上盾，充能清零
            if (skill.Id == GuardianSkillId)
            {
                if ((skill.ChargeThreshold > 0 ? skill.ChargeThreshold : 3) <= c.ASC.GetGuardianCharge())
                {
                    // 满充能：0AP，给目标上盾
                    ShieldSystem.GrantShield(state, target.ASC, c.Id);
                    c.ASC.ResetGuardianCharge();
                }
                else
                {
                    // 未满：1AP 充能，满 3 层后不自动给盾——由玩家在同一回合（如有剩余 AP）
                    // 或下回合免费选择目标给盾
                    if (skill.Cost.Type == SkillCostType.AP)
                        c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -skill.Cost.Amount);
                    c.ASC.AddGuardianCharge();
                }
                return;
            }

            // 旅行家旅途的准备 (Id=41)：1AP 攒 1 充能，攒满阈值层后授予免费旅行标记，
            // 由 MoveCommand 消费该标记实现 0AP 旅行（免费旅行由命令层执行）
            if (skill.Id == 41)
            {
                int threshold = skill.ChargeThreshold > 0 ? skill.ChargeThreshold : 2;
                c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
                c.ASC.AddGuardianCharge();
                if (c.ASC.GetGuardianCharge() >= threshold)
                    c.ASC.Tags.AddTag(GameplayTagConstants.FreeTravel);
                return;
            }

            // 双子附灵（123/124）：必须持有武器；无武器拒绝且不扣 AP（支付前特例）。
            // 两槽共用单一附灵状态：激活即切到该技能对应形态（S3：作用于角色，换武器保留）。
            if (skill.Id == SkillSystem.TwinVampiricSkillId || skill.Id == SkillSystem.TwinAgileSkillId)
            {
                if (c.Inventory.EquippedWeapon == null) return; // 无武器：拒绝不扣 AP
                c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
                c.ASC.Tags.RemoveTag(GameplayTagConstants.TwinVampiric);
                c.ASC.Tags.RemoveTag(GameplayTagConstants.TwinAgile);
                c.ASC.Tags.AddTag(skill.Id == SkillSystem.TwinVampiricSkillId
                    ? GameplayTagConstants.TwinVampiric
                    : GameplayTagConstants.TwinAgile);
                return;
            }

            // 支付消耗（非守护技能）：主成本 + 额外成本
            PayResource(c, skill.Cost);
            if (skill.ExtraCosts != null)
                for (int i = 0; i < skill.ExtraCosts.Length; i++)
                    PayResource(c, skill.ExtraCosts[i]);

            // 静止时空 (Id=50)：扣成本后理智扣至 0.5（SetAttributeBaseValue 同时写 base+current）
            if (skill.Id == 50)
                c.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0.5f);

            // 木灵续存 (Id=81)：安置种子于当前位置（1AP + 1 侵蚀已由 Cost/ExtraCosts 支付）
            if (skill.Id == 81)
            {
                state.WoodSeedPlayer = c.PlayerId;
                state.WoodSeedLocation = c.CurrentLocationId;
                return;
            }

            // 海幻幻剑 (Id=70)：取得归鞘刀并进入归鞘状态（0.5 全减伤由 DamagePipeline 按标签结算）
            if (skill.Id == 70)
            {
                c.Inventory.EquipWeapon(ItemRegistry.Get(12)); // 幻剑（ItemRegistry Id=12，Weapon 伤害 1.5）
                c.ASC.Tags.AddTag(GameplayTagConstants.BladeSheathed);
                return;
            }

            // 海幻蜃景 (Id=71)：1 远程法术伤（Effect 型无减免）+ 目标防御 ≥1 额外 0.5 + 判定胜瞬移
            if (skill.Id == 71)
            {
                DamagePipeline.ApplyDamage(state, c.Id, target.Id, 1f, DamageType.Effect);
                if (target.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense) >= 1f)
                    DamagePipeline.ApplyDamage(state, c.Id, target.Id, 0.5f, DamageType.Effect);
                if (JudgmentDuel.Resolve(state, c, target))
                    c.CurrentLocationId = target.CurrentLocationId;
                return;
            }

            // 束缚 (Id=90)：阵盘不足不可施放；消耗 1 阵盘挂 2 回合束缚 GE
            if (skill.Id == 90)
            {
                if (c.ASC.GetBloodSeals() < 1) return; // 阵盘不足
                c.ASC.ModifyBloodSeals(-1);
                // 挂 2 回合束缚 GE
                GameplayEffect ge = new GameplayEffect
                {
                    Id = 310,
                    DurationType = GameplayEffectDurationType.HasDuration,
                    DurationTurns = 2,
                    Period = new GameplayEffectPeriod(1),
                    Modifiers = System.Array.Empty<AttributeModifier>(),
                    GrantTags = new[] { GameplayTagConstants.Bound }
                };
                GameplayEffectSpec spec = target.ASC.RentSpec();
                spec.Definition = ge;
                spec.SourceId = c.Id;
                spec.TargetId = target.Id;
                spec.RemainingTurns = 2;
                target.ASC.ApplyGameplayEffect(spec);
                return;
            }

            // 圣骑士守护之光 (Id=101)：目标挂 3 回合 GuardLight GE（下次受伤 -2，DamagePipeline 结算）
            if (skill.Id == 101)
            {
                GameplayEffect ge = new GameplayEffect
                {
                    Id = 322,
                    DurationType = GameplayEffectDurationType.HasDuration,
                    DurationTurns = 3,
                    GrantTags = new[] { GameplayTagConstants.GuardLight }
                };
                GameplayEffectSpec spec = target.ASC.RentSpec();
                spec.Definition = ge;
                spec.SourceId = c.Id;
                spec.TargetId = target.Id;
                spec.RemainingTurns = 3;
                target.ASC.ApplyGameplayEffect(spec);
                return;
            }

            // 圣骑士护佑 (Id=103)：当回合无敌 + 充能 +1；全局仅一次
            if (skill.Id == 103)
            {
                if (state.PaladinAegisUsed) return;
                state.PaladinAegisUsed = true;
                c.ASC.Tags.AddTag(GameplayTagConstants.ImmuneDamage);
                c.ASC.AddGuardianCharge();
                return;
            }

            // 技能内判定：胜利应用 OnWinEffects，失败应用 OnLoseEffects（若有）
            if (skill.Judgment != null)
            {
                bool win = JudgmentDuel.Resolve(state, c, target);
                GameplayEffect[] duelEffects = win ? skill.Judgment.OnWinEffects : skill.Judgment.OnLoseEffects;
                if (duelEffects != null)
                {
                    for (int i = 0; i < duelEffects.Length; i++)
                    {
                        GameplayEffect ge = duelEffects[i];
                        if (ge == null) continue;
                        GameplayEffectSpec spec = target.ASC.RentSpec();
                        spec.Definition = ge;
                        spec.SourceId = c.Id;
                        spec.TargetId = target.Id;
                        spec.RemainingTurns = ge.DurationTurns;
                        target.ASC.ApplyGameplayEffect(spec);
                    }
                }
                if (skill.Judgment.CancelOnLose && !win) return; // 败则主效果不应用
            }

            // 应用效果
            if (skill.Effects != null)
            {
                for (int i = 0; i < skill.Effects.Length; i++)
                {
                    GameplayEffect ge = skill.Effects[i];
                    if (ge == null) continue;

                    GameplayEffectSpec spec = target.ASC.RentSpec();
                    spec.Definition = ge;
                    spec.SourceId = c.Id;
                    spec.TargetId = target.Id;
                    spec.RemainingTurns = ge.DurationTurns;
                    target.ASC.ApplyGameplayEffect(spec);

                    // 授予护盾的 GE 统一补走 ShieldSystem.GrantShield，
                    // 保证 OnShieldGranted 事件（ASC 内部 + 全局）正常派发。
                    // Tag 容器为位掩码，重复 AddTag 幂等，无副作用。
                    if (ge.GrantTags != null)
                    {
                        for (int t = 0; t < ge.GrantTags.Length; t++)
                        {
                            if (ge.GrantTags[t] == GameplayTagConstants.HasShield)
                            {
                                ShieldSystem.GrantShield(state, target.ASC, c.Id);
                                break;
                            }
                        }
                    }
                }
            }
        }

        private static void PayResource(Character c, SkillCost cost)
        {
            switch (cost.Type)
            {
                case SkillCostType.AP:
                    c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -cost.Amount);
                    break;
                case SkillCostType.Sanity:
                    c.ASC.ModifyAttributeCurrentValue(AttributeConstants.Sanity, -cost.Amount);
                    break;
                case SkillCostType.ErosionLimit:
                    c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ErosionLimit, -cost.Amount);
                    break;
                case SkillCostType.Charge:
                    c.ASC.ModifyGuardianCharge(-(int)cost.Amount);
                    break;
            }
        }
    }
}
