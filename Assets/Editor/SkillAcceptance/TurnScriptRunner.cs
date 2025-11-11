using System;
using System.Collections.Generic;
using AI;
using Core;
using Gameplay.Movement;
using Network.Local;

namespace SkillAcceptance
{
    /// <summary>
    /// 链路脚本执行器：构造 WorldState + LocalLockstep，注入固定筹码与脚本行动输入，
    /// 循环 RunTurn 直到脚本完成或 GameOver。断言失败不中断，跑完全部步骤汇总。
    /// </summary>
    public static class TurnScriptRunner
    {
        public static List<CheckResult> Run(TurnScript script)
        {
            var results = new List<CheckResult>();
            var state = new WorldState(script.PlayerCount, script.Seed);
            var ais = new SimpleAI[script.PlayerCount];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.StartGame();

            // 固定筹码：按回合索引，不足时复用最后一行
            lockstep.JudgmentInputProvider = playerId =>
            {
                int round = Math.Min(state.CurrentTurn - 1, script.JudgmentInputs.Length - 1);
                return script.JudgmentInputs[round][playerId];
            };

            foreach (Action<WorldState> o in script.Overrides) o(state);

            int stepIndex = 0;
            bool fillMode = false;
            ActStep pendingAct = null;
            bool pendingConsumed = false;

            // 行动输入：fillMode 全员跳过；否则匹配 pendingAct.PlayerId 时交出指令，
            // 其余行动者用 WasteAP 跳过（不阻塞脚本推进）。
            lockstep.ManualInputProvider = playerId =>
            {
                if (fillMode) return new WasteAPCommand(state.CurrentTick, playerId);
                if (pendingAct != null && pendingAct.PlayerId == playerId)
                {
                    pendingConsumed = true;
                    return pendingAct.Command;
                }
                return new WasteAPCommand(state.CurrentTick, playerId);
            };

            while (stepIndex < script.Steps.Count && state.Phase != TurnPhase.GameOver)
            {
                ScriptStep step = script.Steps[stepIndex];

                if (step is AssertStep a)
                {
                    try
                    {
                        float value = a.Getter(state);
                        results.Add(Math.Abs(value - a.Expected) <= a.Tolerance
                            ? CheckResult.Pass($"{script.ScriptName}-{a.Name}", $"实测 {value}（期望 {a.Expected}）")
                            : CheckResult.Fail($"{script.ScriptName}-{a.Name}", $"实测 {value}（期望 {a.Expected}）"));
                    }
                    catch (Exception ex)
                    {
                        results.Add(CheckResult.Error($"{script.ScriptName}-{a.Name}", ex.Message));
                    }
                    stepIndex++;
                    continue;
                }

                if (step is AssertTrueStep at)
                {
                    try
                    {
                        bool value = at.Predicate(state);
                        results.Add(value
                            ? CheckResult.Pass($"{script.ScriptName}-{at.Name}", "条件成立")
                            : CheckResult.Fail($"{script.ScriptName}-{at.Name}", "条件不成立"));
                    }
                    catch (Exception ex)
                    {
                        results.Add(CheckResult.Error($"{script.ScriptName}-{at.Name}", ex.Message));
                    }
                    stepIndex++;
                    continue;
                }

                if (step is EndTurnStep)
                {
                    fillMode = true;
                    int guard = 0;
                    while (state.Phase == TurnPhase.ActionPhase && guard++ < 1000)
                    {
                        if (!lockstep.RunTurn()) break;
                    }
                    fillMode = false;
                    stepIndex++;
                    continue;
                }

                if (step is ActStep act)
                {
                    pendingAct = act;
                    pendingConsumed = false;
                    int guard = 0;
                    while (!pendingConsumed && state.Phase != TurnPhase.GameOver && guard++ < 1000)
                    {
                        if (!lockstep.RunTurn()) break;
                    }
                    if (!pendingConsumed)
                        results.Add(CheckResult.Fail($"{script.ScriptName}-行动未执行",
                            $"指令未能消费（行动者 {act.PlayerId}，Phase {state.Phase}）"));
                    pendingAct = null;
                    stepIndex++;
                    continue;
                }

                stepIndex++; // 未知步骤类型防御性跳过
            }

            return results;
        }
    }
}
