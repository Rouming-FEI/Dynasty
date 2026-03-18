using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Network.Transport
{
    /// <summary>LiteNetLib 薄封装：Server/Client 统一模型，可靠有序单通道（channel 0）。
    /// peerId 直接采用 LiteNetLib 的 NetPeer.Id。纯 C#（LiteNetLib 无 UnityEngine 依赖），127.0.0.1 回环可测。
    /// 所有回调在 PollEvents 内同步触发；回调异常由 SafeInvoke 捕获，避免逃逸 PollEvents、丢同批事件、污染 reader 池。
    /// 注意：主机 LocalPeerId 固定为 0，而 LiteNetLib 首个对端 peer.Id 也是 0——
    /// 上层不得用 peerId 直接当玩家身份（玩家身份用 NetworkSession 的 playerId）；
    /// Broadcast/OnDataReceived 语义保证主机自己不出现在对端集合中。</summary>
    public sealed class NetworkTransport : INetEventListener, INetworkTransport
    {
        private NetManager _manager;
        private string _connectKey;
        private readonly Dictionary<int, NetPeer> _peers = new Dictionary<int, NetPeer>();

        /// <summary>本地 peerId；未连接 = -1。主机恒 0；客户端 = 本端连接表键（当前 build 恒 0，
        /// 重连新建 NetManager 后本端 id 仍从 0 计，勿假设非 0）。</summary>
        public int LocalPeerId { get; private set; } = -1;

        /// <summary>实际监听端口（StartHost(0) 后为系统分配端口）。</summary>
        public int LocalPort => _manager?.LocalPort ?? 0;

        /// <summary>Start 后（Stop 前）为 true。</summary>
        public bool IsRunning => _manager != null;

        /// <summary>true = 主机（StartHost），false = 客户端（Connect）。</summary>
        public bool IsHost { get; private set; }

        /// <summary>远端 peer 建立连接（peerId）。</summary>
        public event Action<int> OnPeerConnected;

        /// <summary>远端 peer 断开（peerId；含连接被拒/超时/主动断开）。</summary>
        public event Action<int> OnPeerDisconnected;

        /// <summary>收到可靠数据（peerId, reader）。reader 仅回调内有效（AutoRecycle=true）。</summary>
        public event Action<int, NetDataReader> OnDataReceived;

        public void StartHost(int port, int maxPeers, string connectKey)
        {
            Stop();
            _connectKey = connectKey;
            IsHost = true;
            _manager = new NetManager(this) { AutoRecycle = true };
            _manager.Start(port);
            LocalPeerId = 0;
        }

        /// <summary>连接主机。LiteNetLib 2.1.4 的 Connect 异步发起（返回 peer 仅表示请求已发出），
        /// 真正连通/失败由 PollEvents 派发 OnPeerConnected / OnPeerDisconnected；timeoutMs 由调用方配合超时回调处理。</summary>
        public bool Connect(string host, int port, string connectKey, int timeoutMs)
        {
            Stop();
            _connectKey = connectKey;
            IsHost = false;
            _manager = new NetManager(this) { AutoRecycle = true };
            _manager.Start();
            NetPeer peer = _manager.Connect(host, port, connectKey);
            if (peer == null)
            {
                Stop();
                return false;
            }
            LocalPeerId = peer.Id;
            return true;
        }

        public void Stop()
        {
            if (_manager != null)
            {
                _manager.Stop();
                _manager = null;
                _peers.Clear();
                LocalPeerId = -1;
                IsHost = false;
            }
        }

        /// <summary>每帧调用：收包/断线/连接回调。</summary>
        public void PollEvents() => _manager?.PollEvents();

        /// <summary>可靠有序发送到指定对端；peer 不在连接表返回 false。</summary>
        public bool SendReliable(int peerId, NetDataWriter w)
        {
            if (_peers.TryGetValue(peerId, out NetPeer peer))
            {
                peer.Send(w, 0, DeliveryMethod.ReliableOrdered);
                return true;
            }
            return false;
        }

        /// <summary>组播到全部对端（不含本地）；无对端返回 false。</summary>
        public bool Broadcast(NetDataWriter w)
        {
            bool any = false;
            foreach (KeyValuePair<int, NetPeer> kv in _peers)
            {
                kv.Value.Send(w, 0, DeliveryMethod.ReliableOrdered);
                any = true;
            }
            return any;
        }

        public void DisconnectPeer(int peerId)
        {
            if (_peers.TryGetValue(peerId, out NetPeer peer))
            {
                peer.Disconnect();
                _peers.Remove(peerId);
            }
        }

        // ---- INetEventListener（显式实现，避免与同名事件冲突）----

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            _peers[peer.Id] = peer;
            SafeInvoke(OnPeerConnected, peer.Id);
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            _peers.Remove(peer.Id);
            SafeInvoke(OnPeerDisconnected, peer.Id);
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            SafeInvoke(OnDataReceived, peer.Id, reader);
        }

        // ---- 事件安全派发：逐事件 try/catch，回调异常不逃逸 PollEvents；参数拷贝引用防订阅者取消订阅竞态 ----

        private void SafeInvoke(Action<int> handler, int id)
        {
            try { handler?.Invoke(id); }
            catch (Exception e) { System.Diagnostics.Debug.WriteLine($"NetworkTransport 回调异常: {e}"); }
        }

        private void SafeInvoke(Action<int, NetDataReader> handler, int id, NetDataReader r)
        {
            try { handler?.Invoke(id, r); }
            catch (Exception e) { System.Diagnostics.Debug.WriteLine($"NetworkTransport 回调异常: {e}"); }
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            // connectKey 作为连接请求首包数据（客户端 Connect 内部用 NetDataWriter.FromString 编码）
            bool ok = false;
            try
            {
                ok = request.Data.GetString() == _connectKey;
            }
            catch
            {
                ok = false;
            }
            if (ok)
                request.Accept();
            else
                request.Reject();
        }

        // ---- 上游 2.1.4 原为接口默认实现（DIM），.NET Framework 4.x 不支持 → 显式补齐空实现（语义同 DIM 默认）----

        void INetEventListener.OnMessageDelivered(NetPeer peer, object userData) { }

        void INetEventListener.OnNtpResponse(NtpPacket packet) { }

        void INetEventListener.OnPeerAddressChanged(NetPeer peer, IPEndPoint previousAddress) { }
    }
}
