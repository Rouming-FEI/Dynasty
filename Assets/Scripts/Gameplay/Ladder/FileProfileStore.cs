using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Gameplay.Ladder
{
    /// <summary>档案文件存储（Simulation 内纯 .NET IO——asmdef noEngineReferences 允许 System.IO）。
    /// Unity 侧传 Application.persistentDataPath 为目录；测试传临时目录。
    /// 格式：KV 行 + 版本头（零依赖、人类可读、无 BOM；v1 字段见各字段名）。
    /// 容错：文件缺失/版本头不符/坏编码 → 整档零值（HasActiveRun=false）；坏行/坏值 → 行级降级（该字段归零、其余保留）；
    /// 任何损坏不抛——存档异常不应炸游戏。Save 走临时文件 + 同卷原子替换，崩溃/断电不留半截档。</summary>
    public sealed class FileProfileStore : IProfileStore
    {
        private const string Header = "dynasty-ladder-profile v1";
        private readonly string _directory;

        public FileProfileStore(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentException("目录路径不能为空", nameof(directoryPath));
            _directory = directoryPath;
        }

        private string FilePath => Path.Combine(_directory, "ladder-profile.dat");

        public LadderProfile Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return default(LadderProfile);
                string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                if (lines.Length == 0 || lines[0] != Header) return default(LadderProfile);

                var p = default(LadderProfile);
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line)) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;                    // 坏行跳过（容错）
                    string key = line.Substring(0, eq);
                    string val = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "bestStreak": p.BestStreak = ParseInt(val); break;
                        case "totalScore": p.TotalScore = ParseLong(val); break;
                        case "attempts": p.Attempts = ParseInt(val); break;
                        case "lastStreak": p.LastStreak = ParseInt(val); break;
                        case "lastScore": p.LastScore = ParseLong(val); break;
                        case "hasActiveRun": p.HasActiveRun = ParseInt(val) != 0; break;
                        case "activeSeed": p.ActiveSeed = ParseInt(val); break;
                        case "activeLayer": p.ActiveLayer = ParseInt(val); break;
                    }
                }
                return p;
            }
            catch (IOException) { return default(LadderProfile); }
            catch (UnauthorizedAccessException) { return default(LadderProfile); }
        }

        public void Save(LadderProfile profile)
        {
            Directory.CreateDirectory(_directory);
            var sb = new StringBuilder(160);
            sb.Append(Header).Append('\n');
            sb.Append("bestStreak=").Append(profile.BestStreak.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("totalScore=").Append(profile.TotalScore.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("attempts=").Append(profile.Attempts.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("lastStreak=").Append(profile.LastStreak.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("lastScore=").Append(profile.LastScore.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("hasActiveRun=").Append(profile.HasActiveRun ? 1 : 0).Append('\n');
            sb.Append("activeSeed=").Append(profile.ActiveSeed.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("activeLayer=").Append(profile.ActiveLayer.ToString(CultureInfo.InvariantCulture)).Append('\n');
            // 原子写：同卷 .tmp → 替换/移动——崩溃/断电不留半截主档（半截 → Load 零值 → 后续零值覆盖 = 生涯纪录不可逆丢失）
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new System.Text.UTF8Encoding(false));   // 无 BOM：人类可读、首行无噪声
            if (File.Exists(FilePath))
                File.Replace(tmp, FilePath, null);   // 同卷原子替换（net48 可用）
            else
                File.Move(tmp, FilePath);            // 首次写：目标不存在走 Move
        }

        private static int ParseInt(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        private static long ParseLong(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0L;
    }
}
