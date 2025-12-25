using System;
using System.Collections.Generic;
using Core.GAS;
using Gameplay.Characters;

namespace SkillAcceptance
{
    /// <summary>
    /// 验收聚合入口：遍历三个 CharacterDef 的全部技能，
    /// 跑推导式检查 + 特例表检查 + 链路脚本，逐技能 try/catch 隔离异常。
    /// </summary>
    public static class SkillAcceptanceRunner
    {
        private static readonly CharacterDef[] Defs =
        {
            GuardianDef.Create(),
            CharmSpiritDef.Create(),
            NormalPersonDef.Create(),
            SwordGhostDef.Create(),
            MagicianDef.Create(),
            TravelerDef.Create(),
            TimeThiefDef.Create(),
            ListenerDef.Create(),
            SeaMirageDef.Create(),
            WoodSpiritDef.Create(),
            FormationMasterDef.Create(),
            PaladinDef.Create(),
            AgentDef.Create()
        };

        public static AcceptanceReport RunAll() => RunCore(includeLogic: true, includeTurn: true);
        public static AcceptanceReport RunLogicOnly() => RunCore(includeLogic: true, includeTurn: false);
        public static AcceptanceReport RunTurnScripts() => RunCore(includeLogic: false, includeTurn: true);

        private static AcceptanceReport RunCore(bool includeLogic, bool includeTurn)
        {
            var report = new AcceptanceReport();

            if (includeLogic)
            {
                foreach (CharacterDef def in Defs)
                {
                    var section = new AcceptanceSection { CharacterName = def.Name };
                    for (int i = 0; i < def.Skills.Length; i++)
                    {
                        SkillDef skill = def.Skills[i];
                        var skillResult = new SkillResult { SkillId = skill.Id, SkillName = skill.Name };
                        try
                        {
                            skillResult.Checks.AddRange(SkillContractVerifier.Verify(def, skill, i));
                            skillResult.Checks.AddRange(AcceptanceRegistry.GetLogicSpecialCases(def, skill, i));
                        }
                        catch (Exception ex)
                        {
                            skillResult.Checks.Add(CheckResult.Error("验收器异常", ex.ToString()));
                        }
                        section.Skills.Add(skillResult);
                    }
                    report.Sections.Add(section);
                }
            }

            if (includeTurn)
            {
                foreach (TurnScript script in AcceptanceRegistry.GetTurnScripts())
                {
                    List<CheckResult> scriptResults;
                    try
                    {
                        scriptResults = TurnScriptRunner.Run(script);
                    }
                    catch (Exception ex)
                    {
                        scriptResults = new List<CheckResult>
                        {
                            CheckResult.Error(script.ScriptName, ex.ToString())
                        };
                    }
                    AppendToSkill(report, script, scriptResults);
                }
            }

            return report;
        }

        /// <summary>链路脚本结果归并到对应技能；找不到归属技能时挂到「未归属链路检查」区块。</summary>
        private static void AppendToSkill(AcceptanceReport report, TurnScript script, List<CheckResult> results)
        {
            foreach (AcceptanceSection section in report.Sections)
                foreach (SkillResult skill in section.Skills)
                    if (skill.SkillId == script.SkillId)
                    {
                        skill.Checks.AddRange(results);
                        return;
                    }

            // 未归属：先并入已有的「未归属链路检查」区块，再按 SkillId 合并；否则新建
            AcceptanceSection orphanSection = null;
            foreach (AcceptanceSection section in report.Sections)
                if (section.CharacterName == "未归属链路检查") { orphanSection = section; break; }
            if (orphanSection == null)
            {
                orphanSection = new AcceptanceSection { CharacterName = "未归属链路检查" };
                report.Sections.Add(orphanSection);
            }
            foreach (SkillResult existing in orphanSection.Skills)
            {
                if (existing.SkillId == script.SkillId)
                {
                    existing.Checks.AddRange(results);
                    return;
                }
            }
            var orphan = new SkillResult { SkillId = script.SkillId, SkillName = script.ScriptName };
            orphan.Checks.AddRange(results);
            orphanSection.Skills.Add(orphan);
        }
    }
}
