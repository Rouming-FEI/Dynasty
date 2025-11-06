using Core;
using Core.GAS;
using Gameplay.Characters;

namespace SkillAcceptance
{
    /// <summary>验收场景助手：走真实 TurnManager.StartGame 基线再叠加覆盖，与生产初始化路径一致。</summary>
    public static class AcceptanceScenes
    {
        /// <summary>3 人局（守护者/魑魅/普通人），直接站在行动阶段。与测试程序集 ScenarioBuilder.Build 同模式但自包含。</summary>
        public static WorldState BuildState(int seed = 42)
        {
            var state = new WorldState(3, seed);
            var turnManager = new TurnManager();
            state.TurnManager = turnManager;
            turnManager.StartGame(state);
            state.Phase = TurnPhase.ActionPhase;
            return state;
        }

        /// <summary>找到使用指定 CharacterDef 的玩家索引（按 Definition.Id 匹配）。</summary>
        public static int FindPlayerOfDef(WorldState state, CharacterDef def)
        {
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character c = state.Characters[i];
                if (c != null && c.Definition != null && c.Definition.Id == def.Id) return i;
            }
            return -1;
        }

        /// <summary>第一个非施放者的存活角色。</summary>
        public static Character FindOther(WorldState state, int casterPlayer)
        {
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character c = state.Characters[i];
                if (c != null && !c.IsDead && i != casterPlayer) return c;
            }
            return null;
        }
    }
}
