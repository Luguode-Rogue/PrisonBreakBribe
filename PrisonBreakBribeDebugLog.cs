using System;
using System.IO;

namespace New_ZZZF.PrisonBreakBribe
{
    /// <summary>
    /// 劫狱贿赂（贿赂狱卒降低冷却）功能诊断日志。
    /// 仿照 EquipmentAffixSystem/Debug/AffixLifecycleDebugLog 实现，
    /// 排查或维护完成后可随本功能一起删除。
    /// </summary>
    internal static class PrisonBreakBribeDebugLog
    {
        private static readonly object Sync = new object();

        private static string FilePath
        {
            get
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                return Path.GetFullPath(Path.Combine(
                    baseDirectory,
                    "..", "..", "Modules", "New_ZZZF",
                    "prison_break_bribe_debug.log"));
            }
        }

        internal static void Info(string message) => Write("INFO", message);
        internal static void Warn(string message) => Write("WARN", message);
        internal static void Error(string message) => Write("ERROR", message);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    string path = FilePath;
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    File.AppendAllText(
                        path,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [PrisonBreakBribe] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // 诊断日志绝不能影响游戏逻辑。
            }
        }
    }
}
