using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Gameplay.Ladder
{
    /// <summary>档案文件存储（Simulation 内纯 .NET IO——asmdef noEngineReferences 允许 System.IO）。
    /// Unity 侧传 Application.persistentDataPath 为目录；测试传临时目录。
    /// 格式：KV 行 + 版本头（零依赖、人类可读；v1 字段见各字段名）。
    /// 容错：文件缺失/版本不符/坏行 → 零值档案（HasActiveRun=false），不抛——存档损坏不应炸游戏。</summary>
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
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
        }

        private static int ParseInt(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        private static long ParseLong(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0L;
    }
}
