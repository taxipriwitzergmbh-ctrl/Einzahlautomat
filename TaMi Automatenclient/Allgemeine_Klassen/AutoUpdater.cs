using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace TaMi_Automatenclient
{
    public static class AutoUpdater
    {
        // Base URL or direct file URL. If it's a folder, the current exe file name will be appended.
        private const string UpdateUrl = "http://kassenautomat.priwitzer-dienstleistungsgmbh.de/Update_Automatenclient";

        // Call this once during startup. Safe: exceptions are swallowed.
        public static void CheckAndPromptAtStartup()
        {
            try
            {
                var exeName = Path.GetFileName(Application.ExecutablePath) ?? string.Empty;
                if (string.IsNullOrEmpty(exeName)) return;

                var currentVersion = GetCurrentVersion();
                var remoteUrl = BuildRemoteUrl(UpdateUrl, exeName);

                string tmpPath = null;
                try
                {
                    tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe");
                    using (var wc = new WebClient())
                    {
                        wc.DownloadFile(remoteUrl, tmpPath);
                    }
                }
                catch
                {
                    // No update available or offline
                    SafeDelete(tmpPath);
                    return;
                }

                Version remoteVersion = GetFileVersion(tmpPath);
                if (remoteVersion == null || remoteVersion <= currentVersion)
                {
                    SafeDelete(tmpPath);
                    return;
                }

                var result = MessageBox.Show(
                    $"Es ist eine neue Version verfügbar (aktuell: {currentVersion}, neu: {remoteVersion}).\r\nJetzt aktualisieren?",
                    "Update verfügbar",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result != DialogResult.Yes)
                {
                    SafeDelete(tmpPath);
                    return;
                }

                // Move downloaded file next to the current EXE as .new
                var exePath = Application.ExecutablePath;
                var appDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
                var newPath = Path.Combine(appDir, exeName + ".new");
                try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
                File.Copy(tmpPath, newPath, true);
                SafeDelete(tmpPath);

                // Create a small batch file that waits for this process to exit, then swaps files and restarts
                var batPath = Path.Combine(appDir, "updater_" + Guid.NewGuid().ToString("N") + ".bat");
                var bat = BuildUpdateBatch(exeName);
                File.WriteAllText(batPath, bat, Encoding.ASCII);

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = batPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = appDir,
                    };
                    Process.Start(psi);
                }
                catch
                {
                    // If starting the updater failed, just leave the .new file in place
                }

                try { Environment.Exit(0); } catch { Application.Exit(); }
            }
            catch
            {
                // Never allow updater to crash app on startup
            }
        }

        private static Version GetCurrentVersion()
        {
            try { return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0); }
            catch { return new Version(0, 0, 0, 0); }
        }

        private static string BuildRemoteUrl(string baseOrFileUrl, string exeName)
        {
            if (string.IsNullOrWhiteSpace(baseOrFileUrl)) return string.Empty;
            var u = baseOrFileUrl.Trim();
            int lastSlash = u.LastIndexOf('/');
            int lastDot = u.LastIndexOf('.');
            bool looksLikeFile = (lastDot > lastSlash); // e.g. ends with .exe
            if (looksLikeFile) return u;
            if (!u.EndsWith("/")) u += "/";
            return u + exeName; // append current exe name
        }

        private static Version GetFileVersion(string path)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrEmpty(info.FileVersion))
                {
                    Version v; if (Version.TryParse(info.FileVersion, out v)) return v;
                }
            }
            catch { }
            return null;
        }

        private static void SafeDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string BuildUpdateBatch(string exeName)
        {
            // Uses a simple retry loop: try to copy .new over .exe until it succeeds, then start and delete self
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal");
            sb.AppendLine("set EXE=\"%~dp0" + exeName + "\"");
            sb.AppendLine("set NEW=\"%~dp0" + exeName + ".new\"");
            sb.AppendLine(":repeat");
            sb.AppendLine("ping 127.0.0.1 -n 2 >nul");
            sb.AppendLine("copy /y %NEW% %EXE% >nul");
            sb.AppendLine("if errorlevel 1 goto repeat");
            sb.AppendLine("del /f /q %NEW% >nul 2>&1");
            sb.AppendLine("start \"\" %EXE%");
            sb.AppendLine("del \"%~f0\" >nul 2>&1");
            sb.AppendLine("endlocal");
            return sb.ToString();
        }
    }
}
