using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Movement;

namespace Dynasty.Tests
{
    /// <summary>
    /// MoveCommand 邻接校验（防御纵深）：普通移动仅允许相邻地点，
    /// 非相邻目标整体忽略（不移动、不扣 AP、无副作用）；
    /// 旅行家例外——旅行 (Id=40) 与免费旅行标记允许前往任意地点（docs/项目介绍.md 7.4）。
    /// </summary>
    public class MoveValidationTests
    {
        private static void Execute(WorldState state, Core.Command.ICommand cmd)
        {
            state.Commands.Enqueue(cmd);
            state.Commands.ExecuteAll(state);
            state.Commands.Clear();
        }

        [Test]
        public void 移动_非相邻整体忽略不扣AP_相邻正常_旅行家任意地点可达()
        {
            // 下潜入口A(0) 的相邻 = {4,5,6,7,8,9}；入口 1 与 0 互不相邻
            WorldState state = ScenarioBuilder.Create(4, 7).WithAllAt(0).WithAllAP(1).Build();

            // 非相邻：整体忽略——原地不动、AP 分文不扣
            Execute(state, new MoveCommand(0, 0, 1));
            Assert.AreEqual(0, state.GetCharacterByPlayer(0).CurrentLocationId,
                "非相邻目标：应留在原地");
            Assert.AreEqual(1, state.GetCharacterByPlayer(0).ActionPoints,
                "非相邻目标：不得消耗 AP");

            // 对照组：相邻移动正常执行（移动 + 扣 1 AP）
            Execute(state, new MoveCommand(0, 0, 4));
            Assert.AreEqual(4, state.GetCharacterByPlayer(0).CurrentLocationId,
                "相邻目标：正常移动");
            Assert.AreEqual(0, state.GetCharacterByPlayer(0).ActionPoints,
                "相邻目标：正常扣 1 AP");

            // 旅行家例外：持有旅行技能即可前往任意地点（含非相邻）
            var t = state.GetCharacterByPlayer(3);
            t.Definition = TravelerDef.Create();
            t.Definition.ApplyTo(t);
            t.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            Execute(state, new MoveCommand(0, 3, 1));
            Assert.AreEqual(1, t.CurrentLocationId, "旅行家：任意地点可达");
            Assert.AreEqual(0, t.ActionPoints, "旅行家：任意地点移动扣 1 AP");
        }
    }
}
