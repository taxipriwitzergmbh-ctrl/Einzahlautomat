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
        private const string UpdateUrl = "http://kassenautomat.priwitzer-dienstleistungsgmbh.de/Update_Automatenclient";

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

                var targetExe = Application.ExecutablePath;
                var appDir = Path.GetDirectoryName(targetExe) ?? Environment.CurrentDirectory;

                // Build updater batch in TEMP that copies from tmpPath to targetExe; elevate if required
                var batPath = Path.Combine(Path.GetTempPath(), "updater_" + Guid.NewGuid().ToString("N") + ".bat");
                var bat = BuildUpdateBatch(tmpPath, targetExe);
                File.WriteAllText(batPath, bat, Encoding.ASCII);

                var needsElevation = RequiresElevation(appDir);
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = batPath,
                        UseShellExecute = true,
                        Verb = needsElevation ? "runas" : null,
                        WorkingDirectory = Path.GetDirectoryName(batPath),
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi);
                }
                catch
                {
                    try { File.Delete(batPath); } catch { }
                    SafeDelete(tmpPath);
                    return;
                }

                try { Environment.Exit(0); } catch { Application.Exit(); }
            }
            catch
            {
            }
        }

        private static bool RequiresElevation(string appDir)
        {
            try
            {
                var t = Path.Combine(appDir, ".__updtest_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(t, "x");
                File.Delete(t);
                return false;
            }
            catch (UnauthorizedAccessException) { return true; }
            catch (System.Security.SecurityException) { return true; }
            catch { return false; }
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

        private static string BuildUpdateBatch(string srcExe, string targetExe)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal");
            sb.AppendLine("set SRC=\"" + srcExe + "\"");
            sb.AppendLine("set EXE=\"" + targetExe + "\"");
            sb.AppendLine(":repeat");
            sb.AppendLine("ping 127.0.0.1 -n 2 >nul");
            sb.AppendLine("copy /y %SRC% %EXE% >nul");
            sb.AppendLine("if errorlevel 1 goto repeat");
            sb.AppendLine("start \"\" %EXE%");
            sb.AppendLine("del /f /q %SRC% >nul 2>&1");
            sb.AppendLine("del \"%~f0\" >nul 2>&1");
            sb.AppendLine("endlocal");
            return sb.ToString();
        }
    }
}
