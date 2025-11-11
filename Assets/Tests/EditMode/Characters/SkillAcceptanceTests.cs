using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SkillAcceptance;

namespace Dynasty.Tests.Characters
{
    /// <summary>
    /// 技能验收 NUnit 壳：把 Runner 报告转成 Test Runner 红绿断言（进 CI）。
    /// NotImplemented 不判失败（未实现功能不是回归），仅面板展示。
    /// </summary>
    public class SkillAcceptanceTests
    {
        public static IEnumerable<TestCaseData> SkillCases()
        {
            AcceptanceReport report = SkillAcceptanceRunner.RunAll();
            foreach (AcceptanceSection section in report.Sections)
                foreach (SkillResult skill in section.Skills)
                {
                    string name = $"{section.CharacterName}.{skill.SkillName}";
                    yield return new TestCaseData(section.CharacterName, skill).SetName(name);
                }
        }

        [TestCaseSource(nameof(SkillCases))]
        public void 技能验收_无失败项(string characterName, SkillResult skill)
        {
            List<CheckResult> failures = skill.Checks
                .Where(c => c.Status == CheckStatus.Fail || c.Status == CheckStatus.Error)
                .ToList();
            Assert.IsTrue(failures.Count == 0,
                $"{characterName}/{skill.SkillName} 失败项：\n" +
                string.Join("\n", failures.Select(f => $"  [{f.Name}] {f.Message}")));
        }
    }
}
