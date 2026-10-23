using System;
using System.IO;
using System.Text;

namespace HideMe.Services
{
    /// <summary>
    /// 极简文件日志：%LOCALAPPDATA%\HideMe\logs\yyyyMMdd.log，保留最近 7 天。
    /// 日志写入失败绝不影响主流程。
    /// </summary>
    public static class LogService
    {
        private static readonly object SyncRoot = new object();
        private static readonly string LogDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HideMe", "logs");

        public static string Directory
        {
            get { return LogDirectoryPath; }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Warn(string message, Exception exception)
        {
            Write("WARN", message, exception);
        }

        public static void Error(string message)
        {
            Write("ERROR", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        /// <summary>删除 7 天前的日志文件。</summary>
        public static void CleanupOldLogs()
        {
            try
            {
                if (!System.IO.Directory.Exists(LogDirectoryPath)) return;
                var cutoff = DateTime.Now.Date.AddDays(-7);
                foreach (var file in System.IO.Directory.GetFiles(LogDirectoryPath, "*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                            File.Delete(file);
                    }
                    catch
                    {
                        // 单个文件删除失败不影响其它文件
                    }
                }
            }
            catch
            {
                // 清理失败不影响程序启动
            }
        }

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                lock (SyncRoot)
                {
                    System.IO.Directory.CreateDirectory(LogDirectoryPath);
                    var file = Path.Combine(LogDirectoryPath, DateTime.Now.ToString("yyyyMMdd") + ".log");
                    var line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level + "] " +
                               (message ?? "") +
                               (exception == null
                                   ? ""
                                   : Environment.NewLine + exception);
                    File.AppendAllText(file, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
                // 日志失败不能影响主流程
            }
        }
    }
}
