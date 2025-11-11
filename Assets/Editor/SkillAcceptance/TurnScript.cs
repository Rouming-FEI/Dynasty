using System;
using System.Collections.Generic;
using Core;
using Core.Command;
using Gameplay.Judgment;

namespace SkillAcceptance
{
    /// <summary>链路脚本步骤基类。</summary>
    public abstract class ScriptStep
    {
        public string Label;
    }

    /// <summary>行动步骤：向指定玩家入队一条指令。</summary>
    public sealed class ActStep : ScriptStep
    {
        public int PlayerId;
        public ICommand Command;

        public ActStep(int playerId, ICommand command, string label = null)
        {
            PlayerId = playerId;
            Command = command;
            Label = label;
        }
    }

    /// <summary>数值断言：|取值 - 期望| &lt;= 容差。</summary>
    public sealed class AssertStep : ScriptStep
    {
        public string Name;
        public float Expected;
        public float Tolerance = 0.001f;
        public Func<WorldState, float> Getter;

        public AssertStep(string name, float expected, Func<WorldState, float> getter)
        {
            Name = name;
            Expected = expected;
            Getter = getter;
        }
    }

    /// <summary>布尔断言。</summary>
    public sealed class AssertTrueStep : ScriptStep
    {
        public string Name;
        public Func<WorldState, bool> Predicate;

        public AssertTrueStep(string name, Func<WorldState, bool> predicate)
        {
            Name = name;
            Predicate = predicate;
        }
    }

    /// <summary>回合推进：填满行动阶段直到回合末结算完成。</summary>
    public sealed class EndTurnStep : ScriptStep
    {
        public EndTurnStep() { Label = "回合结束"; }
    }

    /// <summary>
    /// 链路验收脚本：固定筹码表 + 覆盖项 + 步骤序列，驱动 LocalLockstep 完整回合循环。
    /// JudgmentInputs 按回合索引（CurrentTurn-1），不足时复用最后一行。
    /// </summary>
    public class TurnScript
    {
        public int SkillId;          // 归并到技能验收结果
        public string SkillName;
        public string ScriptName;    // 如「鬼葬-三档(1人)」
        public int PlayerCount = 3;
        public int Seed = 42;
        public ChipAllocation[][] JudgmentInputs;
        public List<Action<WorldState>> Overrides = new List<Action<WorldState>>();
        public List<ScriptStep> Steps = new List<ScriptStep>();
    }
}
