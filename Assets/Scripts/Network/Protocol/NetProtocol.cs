using System;
using Core.Replay;
using Gameplay.Judgment;
using LiteNetLib.Utils;

namespace Network.Protocol
{
    /// <summary>消息类型（byte 值 0..16，对应 [Type:byte] 头字节；未知值由 ReadType 兜底为 Ping）。</summary>
    public enum NetMsgType : byte
    {
        Hello, JoinAccept, JoinReject, GameStart, RoundStart,
        ChipSubmit, ChipBatch, ActionTurn, ActionSubmit, ActionBatch,
        TurnEndBatch, GameOver, Snapshot, ResyncRequest, ResyncReply, Ping, Pong
    }

    /// <summary>
    /// 消息协议编解码：格式 [Type:byte][Payload...]，可靠有序通道（LiteNetLib 保证整包，无需长度前缀）。
    /// 全部手写、零 GC（无 LINQ/反射/装箱，枚举显式 cast）；数组长度读取带防护（参考 ReplaySerializer.CheckLength）。
    /// 说明：WriteXxx 为纯载荷方法，调用方先 WriteHeader；唯一例外 WriteChipBatch 为完整消息（内含 header）。
    /// </summary>
    public static class NetProtocol
    {
        /// <summary>协议版本（Hello 首字段，客户端与主机不一致时拒绝加入）。</summary>
        public const int ProtocolVersion = 1;

        /// <summary>从网络包读取的数组长度上限（约 100 万；防损坏/恶意包携带巨大长度导致分配 OOM）。</summary>
        private const int MaxArrayLength = 1 << 20;

        // ---- 头 ----

        /// <summary>写消息头 [Type:byte]。</summary>
        public static void WriteHeader(NetDataWriter w, NetMsgType type) => w.Put((byte)type);

        /// <summary>读消息头；未知类型返回 Ping 兜底（避免按错误载荷解析）。</summary>
        public static NetMsgType ReadType(NetDataReader r)
        {
            byte v = r.GetByte();
            if (v <= (byte)NetMsgType.Pong)
                return (NetMsgType)v;
            return NetMsgType.Ping;
        }

        /// <summary>分配前校验从网络包读取的长度：仅允许 [0, MaxArrayLength]。</summary>
        private static void CheckLength(int len, string what)
        {
            if (len < 0 || len > MaxArrayLength)
                throw new InvalidOperationException($"{what} 长度非法: {len}");
        }

        // ---- Hello (C→H) ----

        public static void WriteHello(NetDataWriter w, int protoVersion, int requestedDefId)
        {
            w.Put(protoVersion);
            w.Put(requestedDefId);
        }

        public static void ReadHello(NetDataReader r, out int protoVersion, out int requestedDefId)
        {
            protoVersion = r.GetInt();
            requestedDefId = r.GetInt();
        }

        // ---- JoinAccept (H→C) ----

        public static void WriteJoinAccept(NetDataWriter w, int playerId, int seed, int[] defIds, int judgmentMs, int actionMs)
        {
            w.Put(playerId);
            w.Put(seed);
            int count = defIds != null ? defIds.Length : 0;
            w.Put(count);
            for (int i = 0; i < count; i++)
                w.Put(defIds[i]);
            w.Put(judgmentMs);
            w.Put(actionMs);
        }

        public static void ReadJoinAccept(NetDataReader r, out int playerId, out int seed, out int[] defIds, out int judgmentMs, out int actionMs)
        {
            playerId = r.GetInt();
            seed = r.GetInt();
            int count = r.GetInt();
            CheckLength(count, "JoinAccept.DefIds");
            defIds = new int[count];
            for (int i = 0; i < count; i++)
                defIds[i] = r.GetInt();
            judgmentMs = r.GetInt();
            actionMs = r.GetInt();
        }

        // ---- JoinReject (H→C) ----

        public static void WriteJoinReject(NetDataWriter w, byte reason) => w.Put(reason);

        public static byte ReadJoinReject(NetDataReader r) => r.GetByte();

        // ---- GameStart (H→C) ----

        public static void WriteGameStart(NetDataWriter w, int turn) => w.Put(turn);

        public static int ReadGameStart(NetDataReader r) => r.GetInt();

        // ---- RoundStart (H→C) ----

        public static void WriteRoundStart(NetDataWriter w, int turn, int windowMs)
        {
            w.Put(turn);
            w.Put(windowMs);
        }

        public static void ReadRoundStart(NetDataReader r, out int turn, out int windowMs)
        {
            turn = r.GetInt();
            windowMs = r.GetInt();
        }

        // ---- ChipSubmit (C→H) ----

        public static void WriteChipSubmit(NetDataWriter w, int playerId, int rock, int scissors, int paper)
        {
            w.Put(playerId);
            w.Put(rock);
            w.Put(scissors);
            w.Put(paper);
        }

        public static void ReadChipSubmit(NetDataReader r, out int playerId, out int rock, out int scissors, out int paper)
        {
            playerId = r.GetInt();
            rock = r.GetInt();
            scissors = r.GetInt();
            paper = r.GetInt();
        }

        // ---- ChipBatch (H→C) ----
        // 完整消息（内含 header）：[ChipBatch][count:int][(playerId,rock,scissors,paper)...]

        public static void WriteChipBatch(NetDataWriter w, int count, params ChipAllocation[] allocs)
        {
            WriteHeader(w, NetMsgType.ChipBatch);
            w.Put(count);
            for (int i = 0; i < count; i++)
            {
                ChipAllocation a = allocs[i];
                w.Put(a.PlayerId);
                w.Put(a.Rock);
                w.Put(a.Scissors);
                w.Put(a.Paper);
            }
        }

        /// <summary>读批次数量（配合 ReadChip 逐条消费）。</summary>
        public static int ReadChipCount(NetDataReader r) => r.GetInt();

        /// <summary>读单条筹码分配。</summary>
        public static ChipAllocation ReadChip(NetDataReader r)
        {
            var a = new ChipAllocation(r.GetInt());
            a.Rock = r.GetInt();
            a.Scissors = r.GetInt();
            a.Paper = r.GetInt();
            return a;
        }

        /// <summary>读整个批次（不含 header；调用方先 ReadType）。</summary>
        public static void ReadChipBatch(NetDataReader r, out ChipAllocation[] allocs)
        {
            int count = r.GetInt();
            CheckLength(count, "ChipBatch");
            allocs = new ChipAllocation[count];
            for (int i = 0; i < count; i++)
                allocs[i] = ReadChip(r);
        }

        // ---- ActionTurn (H→C) ----

        public static void WriteActionTurn(NetDataWriter w, int playerId, int windowMs)
        {
            w.Put(playerId);
            w.Put(windowMs);
        }

        public static void ReadActionTurn(NetDataReader r, out int playerId, out int windowMs)
        {
            playerId = r.GetInt();
            windowMs = r.GetInt();
        }

        // ---- ReplayActionRecord 原子编解码（ActionSubmit/ActionBatch 共用载荷）----

        /// <summary>字段序：Tick,PlayerId,Kind(byte),IntArg1,IntArg2,IntArg3,FloatArg,Source(byte)（与 ReplaySerializer 一致）。</summary>
        public static void WriteActionRecord(NetDataWriter w, ReplayActionRecord rec)
        {
            w.Put(rec.Tick);
            w.Put(rec.PlayerId);
            w.Put((byte)rec.Kind);
            w.Put(rec.IntArg1);
            w.Put(rec.IntArg2);
            w.Put(rec.IntArg3);
            w.Put(rec.FloatArg);
            w.Put(rec.Source);
        }

        public static ReplayActionRecord ReadActionRecord(NetDataReader r)
        {
            return new ReplayActionRecord
            {
                Tick = r.GetInt(),
                PlayerId = r.GetInt(),
                Kind = (ReplayActionKind)r.GetByte(),
                IntArg1 = r.GetInt(),
                IntArg2 = r.GetInt(),
                IntArg3 = r.GetInt(),
                FloatArg = r.GetFloat(),
                Source = r.GetByte()
            };
        }

        // ---- ActionSubmit (C→H) ----

        public static void WriteActionSubmit(NetDataWriter w, ReplayActionRecord rec) => WriteActionRecord(w, rec);

        public static ReplayActionRecord ReadActionSubmit(NetDataReader r) => ReadActionRecord(r);

        // ---- ActionBatch (H→C) ----

        public static void WriteActionBatch(NetDataWriter w, ReplayActionRecord rec, byte isPass)
        {
            WriteActionRecord(w, rec);
            w.Put(isPass);
        }

        public static void ReadActionBatch(NetDataReader r, out ReplayActionRecord rec, out byte isPass)
        {
            rec = ReadActionRecord(r);
            isPass = r.GetByte();
        }

        /// <summary>超时跳过标记（isPass=1 表示该玩家本回合超时未行动）。</summary>
        public static void WritePassFlag(NetDataWriter w, byte isPass) => w.Put(isPass);

        public static byte ReadPassFlag(NetDataReader r) => r.GetByte();

        // ---- TurnEndBatch (H→C，空载荷) ----

        public static void WriteTurnEndBatch(NetDataWriter w) { }

        public static void ReadTurnEndBatch(NetDataReader r) { }

        // ---- GameOver (H→C) ----

        public static void WriteGameOver(NetDataWriter w, int winnerPlayerId) => w.Put(winnerPlayerId);

        public static int ReadGameOver(NetDataReader r) => r.GetInt();

        // ---- Snapshot (H→C)：字节数组为 WorldStateSerializer 产物（Task 9），本层仅透传 ----

        public static void WriteSnapshot(NetDataWriter w, int tick, byte[] bytes, int len)
        {
            w.Put(tick);
            w.Put(len);
            if (len > 0)
                w.Put(bytes, 0, len);
        }

        public static void ReadSnapshot(NetDataReader r, out int tick, out byte[] bytes)
        {
            tick = r.GetInt();
            int len = r.GetInt();
            CheckLength(len, "Snapshot");
            bytes = new byte[len];
            if (len > 0)
                r.GetBytes(bytes, len);
        }

        // ---- ResyncRequest (C→H) ----

        public static void WriteResyncRequest(NetDataWriter w, int lastTick, int lastTurn)
        {
            w.Put(lastTick);
            w.Put(lastTurn);
        }

        public static void ReadResyncRequest(NetDataReader r, out int lastTick, out int lastTurn)
        {
            lastTick = r.GetInt();
            lastTurn = r.GetInt();
        }

        // ---- ResyncReply (H→C)：快照与日志字节均透传 ----

        public static void WriteResyncReply(NetDataWriter w, int snapshotTick, byte[] snapshotBytes, int snapshotLen, byte[] logBytes, int logLen)
        {
            w.Put(snapshotTick);
            w.Put(snapshotLen);
            if (snapshotLen > 0)
                w.Put(snapshotBytes, 0, snapshotLen);
            w.Put(logLen);
            if (logLen > 0)
                w.Put(logBytes, 0, logLen);
        }

        public static void ReadResyncReply(NetDataReader r, out int snapshotTick, out byte[] snapshotBytes, out int logCount, out byte[] logBytes)
        {
            snapshotTick = r.GetInt();
            int snapshotLen = r.GetInt();
            CheckLength(snapshotLen, "ResyncReply.Snapshot");
            snapshotBytes = new byte[snapshotLen];
            if (snapshotLen > 0)
                r.GetBytes(snapshotBytes, snapshotLen);
            logCount = r.GetInt();
            CheckLength(logCount, "ResyncReply.Log");
            logBytes = new byte[logCount];
            if (logCount > 0)
                r.GetBytes(logBytes, logCount);
        }

        // ---- Ping/Pong (双向) ----

        public static void WritePing(NetDataWriter w, int timestampMs) => w.Put(timestampMs);

        public static int ReadPing(NetDataReader r) => r.GetInt();

        public static void WritePong(NetDataWriter w, int timestampMs) => w.Put(timestampMs);

        public static int ReadPong(NetDataReader r) => r.GetInt();
    }
}
