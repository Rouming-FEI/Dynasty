using System;
using System.IO;
using Gameplay.Judgment;

namespace Core.Replay
{
    /// <summary>
    /// .dynreplay 自定义紧凑二进制序列化（长度前缀 + 定长字段）。
    /// 不使用 JsonUtility/BinaryFormatter（项目约束）；float 定长 4 字节无损往返。
    /// </summary>
    public static class ReplaySerializer
    {
        private const int Magic = 0x44525950; // 'DRYP'
        /// <summary>当前序列化格式版本（与 ReplayLog.Version 保持一致）。</summary>
        private const int CurrentVersion = 1;
        /// <summary>从文件读取的任何长度上限（约 100 万；一局回放远小于此，防损坏文件携带巨大正数导致分配 OOM）。</summary>
        private const int MaxArrayLength = 1 << 20;

        /// <summary>分配前校验从文件读取的长度：仅允许 -1（null 哨兵）或 [0, MaxArrayLength]。</summary>
        private static void CheckLength(int len, string what)
        {
            if (len < -1 || len > MaxArrayLength)
                throw new InvalidDataException($"{what} 长度非法: {len}");
        }

        public static void Save(ReplayLog log, string path)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                w.Write(Magic);
                w.Write(CurrentVersion);
                w.Write(log.PlayerCount);
                w.Write(log.Seed);
                w.Write(log.MaxTurns);
                w.Write(log.TotalTicks);

                WriteIntArray(w, log.CharacterDefIds);
                WriteBoolArray(w, log.HumanPlayersMask);
                WriteLongArray(w, log.TickHashes);

                w.Write(log.Chips.Count);
                for (int i = 0; i < log.Chips.Count; i++)
                {
                    ChipAllocation[] allocs = log.Chips[i].Allocations;
                    w.Write(allocs.Length);
                    for (int j = 0; j < allocs.Length; j++)
                    {
                        w.Write(allocs[j].PlayerId);
                        w.Write(allocs[j].Rock);
                        w.Write(allocs[j].Scissors);
                        w.Write(allocs[j].Paper);
                    }
                }

                w.Write(log.Actions.Count);
                for (int i = 0; i < log.Actions.Count; i++)
                {
                    ReplayActionRecord a = log.Actions[i];
                    w.Write(a.Tick);
                    w.Write(a.PlayerId);
                    w.Write((byte)a.Kind);
                    w.Write(a.IntArg1);
                    w.Write(a.IntArg2);
                    w.Write(a.IntArg3);
                    w.Write(a.FloatArg);
                    w.Write(a.Source);
                }

                w.Write(log.Events.Count);
                for (int i = 0; i < log.Events.Count; i++)
                {
                    ReplayEventRecord e = log.Events[i];
                    w.Write(e.Tick);
                    w.Write(e.Turn);
                    w.Write(e.EventType);
                    w.Write(e.SourceId);
                    w.Write(e.TargetId);
                    w.Write(e.Magnitude);
                    w.Write(e.Arg1);
                    w.Write(e.Arg2);
                    w.Write(e.IntArg1);
                    w.Write(e.IntArg2);
                }
            }
        }

        public static ReplayLog Load(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open))
            using (var r = new BinaryReader(fs))
            {
                int magic = r.ReadInt32();
                if (magic != Magic)
                    throw new InvalidDataException($"不是有效的 .dynreplay 文件（magic=0x{magic:X8}）");
                int version = r.ReadInt32();
                if (version != CurrentVersion)
                    throw new InvalidDataException($"不支持的版本 {version}（当前 {CurrentVersion}）");

                var log = new ReplayLog { Version = version };
                log.PlayerCount = r.ReadInt32();
                log.Seed = r.ReadInt32();
                log.MaxTurns = r.ReadInt32();
                log.TotalTicks = r.ReadInt32();
                log.CharacterDefIds = ReadIntArray(r, "CharacterDefIds");
                log.HumanPlayersMask = ReadBoolArray(r, "HumanPlayersMask");
                log.TickHashes = ReadLongArray(r, "TickHashes");

                int chipCount = r.ReadInt32();
                CheckLength(chipCount, "Chips");
                for (int i = 0; i < chipCount; i++)
                {
                    int len = r.ReadInt32();
                    CheckLength(len, "Allocations");
                    var allocs = new ChipAllocation[len];
                    for (int j = 0; j < len; j++)
                    {
                        allocs[j] = new ChipAllocation(r.ReadInt32());
                        allocs[j].Rock = r.ReadInt32();
                        allocs[j].Scissors = r.ReadInt32();
                        allocs[j].Paper = r.ReadInt32();
                    }
                    log.Chips.Add(new ReplayChipRound { Allocations = allocs });
                }

                int actionCount = r.ReadInt32();
                CheckLength(actionCount, "Actions");
                for (int i = 0; i < actionCount; i++)
                {
                    log.Actions.Add(new ReplayActionRecord
                    {
                        Tick = r.ReadInt32(),
                        PlayerId = r.ReadInt32(),
                        Kind = (ReplayActionKind)r.ReadByte(),
                        IntArg1 = r.ReadInt32(),
                        IntArg2 = r.ReadInt32(),
                        IntArg3 = r.ReadInt32(),
                        FloatArg = r.ReadSingle(),
                        Source = r.ReadByte()
                    });
                }

                int eventCount = r.ReadInt32();
                CheckLength(eventCount, "Events");
                for (int i = 0; i < eventCount; i++)
                {
                    log.Events.Add(new ReplayEventRecord
                    {
                        Tick = r.ReadInt32(),
                        Turn = r.ReadInt32(),
                        EventType = r.ReadInt32(),
                        SourceId = r.ReadInt32(),
                        TargetId = r.ReadInt32(),
                        Magnitude = r.ReadSingle(),
                        Arg1 = r.ReadSingle(),
                        Arg2 = r.ReadSingle(),
                        IntArg1 = r.ReadInt32(),
                        IntArg2 = r.ReadInt32()
                    });
                }
                return log;
            }
        }

        private static void WriteIntArray(BinaryWriter w, int[] arr)
        {
            if (arr == null) { w.Write(-1); return; }
            w.Write(arr.Length);
            for (int i = 0; i < arr.Length; i++) w.Write(arr[i]);
        }

        private static void WriteBoolArray(BinaryWriter w, bool[] arr)
        {
            if (arr == null) { w.Write(-1); return; }
            w.Write(arr.Length);
            for (int i = 0; i < arr.Length; i++) w.Write(arr[i]);
        }

        private static void WriteLongArray(BinaryWriter w, long[] arr)
        {
            if (arr == null) { w.Write(-1); return; }
            w.Write(arr.Length);
            for (int i = 0; i < arr.Length; i++) w.Write(arr[i]);
        }

        private static int[] ReadIntArray(BinaryReader r, string what)
        {
            int len = r.ReadInt32();
            CheckLength(len, what);
            if (len < 0) return null;
            var arr = new int[len];
            for (int i = 0; i < len; i++) arr[i] = r.ReadInt32();
            return arr;
        }

        private static bool[] ReadBoolArray(BinaryReader r, string what)
        {
            int len = r.ReadInt32();
            CheckLength(len, what);
            if (len < 0) return null;
            var arr = new bool[len];
            for (int i = 0; i < len; i++) arr[i] = r.ReadBoolean();
            return arr;
        }

        private static long[] ReadLongArray(BinaryReader r, string what)
        {
            int len = r.ReadInt32();
            CheckLength(len, what);
            if (len < 0) return null;
            var arr = new long[len];
            for (int i = 0; i < len; i++) arr[i] = r.ReadInt64();
            return arr;
        }
    }
}
