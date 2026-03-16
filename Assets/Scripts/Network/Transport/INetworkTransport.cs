using System;
using LiteNetLib.Utils;

namespace Network.Transport
{
    /// <summary>
    /// NetworkGameLoop 依赖的传输最小接口：真实 NetworkTransport 与测试 Loopback 替身共用
    /// （NetworkTransport 为 sealed 具体类，窗口测试不走真实 UDP，故驱动层面向接口）。
    /// 事件回调在 PollEvents 内同步触发；reader 仅回调内有效（AutoRecycle）。
    /// </summary>
    public interface INetworkTransport
    {
        /// <summary>true = 主机（StartHost），false = 客户端（Connect）。</summary>
        bool IsHost { get; }

        /// <summary>
        /// 本地 peerId；未连接 = -1。主机恒 0；客户端 = 其与主机连接分配的 NetPeer.Id
        /// （LiteNetLib 按空闲槽分配，重连/多客户端下可能非 0——客户端发往主机必须用它，不能写死 0）。
        /// </summary>
        int LocalPeerId { get; }

        /// <summary>每帧调用：收包/断线/连接回调。</summary>
        void PollEvents();

        /// <summary>可靠有序发送到指定对端；对端不在连接表返回 false。</summary>
        bool SendReliable(int peerId, NetDataWriter w);

        /// <summary>组播到全部对端（不含本地）；无对端返回 false。</summary>
        bool Broadcast(NetDataWriter w);

        /// <summary>收到可靠数据（peerId, reader）。reader 仅回调内有效。</summary>
        event Action<int, NetDataReader> OnDataReceived;
    }
}
