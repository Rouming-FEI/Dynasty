using System;
using System.Collections.Generic;
using System.IO;
using Gameplay.Judgment;

namespace Core.Replay
{
    /// <summary>
    /// 主机会话日志：记录全部已广播的输入批次（判定筹码 + 行动命令）。
    /// 重连端用「快照 + 日志重放」追赶，不重新决策（P6 设计 3.6）。
    /// 简化 ReplayLog——只存「已广播的输入批次」；TurnEndBatch 隐含（消费最后行动批次时
    /// 引擎自动跨过回合末），不入日志。
    /// 格式：[count:int][(tick:int)(flag:byte)(chip 载荷 | action 载荷)...]；
    /// BinaryWriter/BinaryReader 紧凑二进制，长度前缀带防护（防损坏数据 OOM）。
    /// </summary>
    public sealed class SessionLog
    {
        /// <summary>一条已广播的输入批次。</summary>
        public sealed class Entry
        {
            /// <summary>批次产生时的引擎 Tick（判定批次 = 结算推进后的 tick；作废重判轮不推进 tick，
            /// 同 tick 多条判定条目属正常）。</summary>
            public int Tick;
            public bool IsChipBatch;
            /// <summary>IsChipBatch=true：全员判定筹码（长度=PlayerCount）。</summary>
            public ChipAllocation[] Chips;
            /// <summary>IsChipBatch=false：行动命令记录（超时标记约定：Source==255 且 Kind==WasteAP）。</summary>
            public ReplayActionRecord Action;
        }

        /// <summary>从日志读取的任何长度兜底上限（约 100 万；防损坏数据携带巨大正数导致分配 OOM）。</summary>
        private const int MaxArrayLength = 1 << 20;

        public readonly List<Entry> Entries = new List<Entry>();

        public int Count => Entries.Count;

        /// <summary>记录一次判定批次广播（tick = 广播时引擎 tick；OnJudgmentCommitted 钩子接入）。</summary>
        public void AddChipBatch(int tick, ChipAllocation[] chips)
        {
            Entries.Add(new Entry { Tick = tick, IsChipBatch = true, Chips = chips });
        }

        /// <summary>记录一次行动批次广播（record.Tick = 执行 tick；超时标记由调用方置 Source=255 后传入）。</summary>
        public void AddAction(int tick, ReplayActionRecord record)
        {
            Entries.Add(new Entry { Tick = tick, IsChipBatch = false, Action = record });
        }

        /// <summary>截取 fromTick 之后（含）的条目序列化字节（供 ResyncReply 载荷；
        /// 快照 tick 后开始 = SerializeFrom(snapshotTick)，含边界条目——快照 tick 与批次 tick 不重合）。</summary>
        public byte[] SerializeFrom(int fromTick)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    int count = 0;
                    for (int i = 0; i < Entries.Count; i++)
                        if (Entries[i].Tick >= fromTick) count++;
                    w.Write(count);
                    for (int i = 0; i < Entries.Count; i++)
                    {
                        Entry e = Entries[i];
                        if (e.Tick < fromTick) continue;
                        w.Write(e.Tick);
                        w.Write(e.IsChipBatch ? (byte)1 : (byte)0);
                        if (e.IsChipBatch)
                        {
                            ChipAllocation[] chips = e.Chips ?? new ChipAllocation[0];
                            w.Write(chips.Length);
                            for (int j = 0; j < chips.Length; j++)
                            {
                                w.Write(chips[j].PlayerId);
                                w.Write(chips[j].Rock);
                                w.Write(chips[j].Scissors);
                                w.Write(chips[j].Paper);
                            }
                        }
                        else
                        {
                            ReplayActionRecord a = e.Action;
                            w.Write(a.Tick);
                            w.Write(a.PlayerId);
                            w.Write((byte)a.Kind);
                            w.Write(a.IntArg1);
                            w.Write(a.IntArg2);
                            w.Write(a.IntArg3);
                            w.Write(a.FloatArg);
                            w.Write(a.Source);
                        }
                    }
                }
                return ms.ToArray();
            }
        }

        /// <summary>反序列化（客户端重连端）。损坏/截断数据抛 InvalidDataException。</summary>
        public static List<Entry> Deserialize(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            try
            {
                using (var ms = new MemoryStream(bytes, false))
                using (var r = new BinaryReader(ms))
                {
                    int count = r.ReadInt32();
                    CheckLength(count, "EntryCount");
                    var entries = new List<Entry>(count);
                    for (int i = 0; i < count; i++)
                    {
                        int tick = r.ReadInt32();
                        byte flag = r.ReadByte();
                        if (flag == 1)
                        {
                            int chipCount = r.ReadInt32();
                            CheckLength(chipCount, "Chips", GameConfig.MaxPlayers);
                            var chips = new ChipAllocation[chipCount];
                            for (int j = 0; j < chipCount; j++)
                            {
                                chips[j] = new ChipAllocation(r.ReadInt32());
                                chips[j].Rock = r.ReadInt32();
                                chips[j].Scissors = r.ReadInt32();
                                chips[j].Paper = r.ReadInt32();
                            }
                            entries.Add(new Entry { Tick = tick, IsChipBatch = true, Chips = chips });
                        }
                        else if (flag == 0)
                        {
                            var a = new ReplayActionRecord
                            {
                                Tick = r.ReadInt32(),
                                PlayerId = r.ReadInt32(),
                                Kind = (ReplayActionKind)r.ReadByte(),
                                IntArg1 = r.ReadInt32(),
                                IntArg2 = r.ReadInt32(),
                                IntArg3 = r.ReadInt32(),
                                FloatArg = r.ReadSingle(),
                                Source = r.ReadByte()
                            };
                            entries.Add(new Entry { Tick = tick, IsChipBatch = false, Action = a });
                        }
                        else
                        {
                            throw new InvalidDataException($"未知日志条目类型 {flag}");
                        }
                    }
                    return entries;
                }
            }
            catch (EndOfStreamException e)
            {
                // 截断/损坏的日志读越界：统一为「坏数据」语义（InvalidDataException）
                throw new InvalidDataException("SessionLog 数据截断或损坏", e);
            }
        }

        private static void CheckLength(int len, string what, int cap = MaxArrayLength)
        {
            if (len < 0 || len > cap)
                throw new InvalidDataException($"{what} 长度非法: {len}");
        }
    }
}
