using System.Collections.Generic;
using System.Linq;

namespace SkillAcceptance
{
    /// <summary>检查项状态：Pass/Fail 参与红绿；Skip 无断言；NotImplemented 文档未实现（黄色）；Error 工具自身异常。</summary>
    public enum CheckStatus : byte { Pass, Fail, Skip, NotImplemented, Error }

    /// <summary>单项检查结果。</summary>
    public class CheckResult
    {
        public string Name;
        public CheckStatus Status;
        public string Message;   // 实测值 vs 期望值 / 跳过原因 / 异常信息

        public CheckResult(string name, CheckStatus status, string message = "")
        {
            Name = name;
            Status = status;
            Message = message;
        }

        public static CheckResult Pass(string name, string message = "")
            => new CheckResult(name, CheckStatus.Pass, message);
        public static CheckResult Fail(string name, string message)
            => new CheckResult(name, CheckStatus.Fail, message);
        public static CheckResult Skip(string name, string message)
            => new CheckResult(name, CheckStatus.Skip, message);
        public static CheckResult NotImplemented(string name, string message)
            => new CheckResult(name, CheckStatus.NotImplemented, message);
        public static CheckResult Error(string name, string message)
            => new CheckResult(name, CheckStatus.Error, message);
    }

    /// <summary>单个技能的验收结果。</summary>
    public class SkillResult
    {
        public int SkillId;
        public string SkillName;
        public readonly List<CheckResult> Checks = new List<CheckResult>();

        public int FailCount => Checks.Count(c => c.Status == CheckStatus.Fail || c.Status == CheckStatus.Error);
        public bool HasFailure => FailCount > 0;
    }

    /// <summary>按角色组织的验收区块。</summary>
    public class AcceptanceSection
    {
        public string CharacterName;
        public readonly List<SkillResult> Skills = new List<SkillResult>();
    }

    /// <summary>全量验收报告（Editor 面板与 NUnit 壳共用）。</summary>
    public class AcceptanceReport
    {
        public readonly List<AcceptanceSection> Sections = new List<AcceptanceSection>();

        public IEnumerable<CheckResult> AllChecks
            => Sections.SelectMany(s => s.Skills).SelectMany(k => k.Checks);
        public int TotalCount => AllChecks.Count();
        public int PassCount => AllChecks.Count(c => c.Status == CheckStatus.Pass);
        public int FailCount => AllChecks.Count(c => c.Status == CheckStatus.Fail || c.Status == CheckStatus.Error);
        public int SkipCount => AllChecks.Count(c => c.Status == CheckStatus.Skip);
        public int NotImplementedCount => AllChecks.Count(c => c.Status == CheckStatus.NotImplemented);
        public string Summary => $"{PassCount} 通过 · {FailCount} 失败 · {SkipCount} 跳过 · {NotImplementedCount} 未实现";
        public bool HasFailure => FailCount > 0;
    }
}
