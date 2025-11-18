using System;
using System.Collections.Generic;
using AI;
using Core;
using Core.Command;
using Gameplay.Characters;
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
            ActStep pendingAct = null;
            bool pendingConsumed = false;

            // 行动输入：匹配 pendingAct.PlayerId 时交出指令（仅一次，随后置空防重复消费）；
            // 满充能 0 AP 的守护者会被 GetNextActor 持续视为可行动（WasteAP 无效），
            // 自动免费施放守护给盾化解，避免 RunTurn 行动循环空转；其余行动者 WasteAP 跳过。
            lockstep.ManualInputProvider = playerId =>
            {
                if (pendingAct != null && pendingAct.PlayerId == playerId)
                {
                    ICommand cmd = pendingAct.Command;
                    pendingAct = null;
                    pendingConsumed = true;
                    return cmd;
                }
                Character actor = state.GetCharacterByPlayer(playerId);
                if (actor != null && actor.ActionPoints <= 0 && actor.ASC.GetGuardianCharge() >= 3)
                {
                    int idx = FindGuardianSkillIndex(actor);
                    if (idx >= 0)
                        return new UseSkillCommand(state.CurrentTick, playerId, idx, playerId);
                    // 非守护者异常充能（脚本配置错误）：清空充能强制解除 eligible，
                    // 避免 RunTurn 行动循环无 guard 空转挂起
                    actor.ASC.ResetGuardianCharge();
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
                    // RunTurn 单次调用推进一个完整回合（判定→行动→回合末）
                    if (state.Phase != TurnPhase.GameOver)
                        lockstep.RunTurn();
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

        /// <summary>按技能 Id 查找守护技能索引（免费给盾兜底用）；无则 -1。</summary>
        private static int FindGuardianSkillIndex(Character c)
        {
            if (c.Definition?.Skills == null) return -1;
            for (int i = 0; i < c.Definition.Skills.Length; i++)
                if (c.Definition.Skills[i].Id == SkillSystem.GuardianSkillId) return i;
            return -1;
        }
    }
}
