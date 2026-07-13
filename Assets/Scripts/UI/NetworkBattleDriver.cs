using Core;
using Core.Command;
using Gameplay.Judgment;

namespace UI
{
    /// <summary>
    /// 网络对局中 BattleUI 的状态/输入来源（由 NetworkUIController 实现并注册）：
    /// 网络模式不 Bind lockstep（钩子归 NetworkGameLoop），BattleUI 经此接口读状态、交输入。
    /// </summary>
    public interface INetworkBattleDriver
    {
        /// <summary>当前对局状态（loop.State；开局后才非空）。</summary>
        WorldState State { get; }

        /// <summary>本端真人槽位（主机会话恒 0；客户端 = JoinAccept 分配）。</summary>
        int LocalPlayerId { get; }

        /// <summary>当前行动者 playerId（-1 = 无）。</summary>
        int CurrentActorPlayerId { get; }

        /// <summary>判定提交出口（loop.SubmitChip；窗口关闭/轮次不符时内部忽略并报错）。</summary>
        void SubmitChip(ChipAllocation alloc);

        /// <summary>行动命令提交出口（loop.SubmitAction；命令转 ReplayActionRecord，Unknown 拒绝）。</summary>
        void SubmitCommand(ICommand cmd);
    }
}
