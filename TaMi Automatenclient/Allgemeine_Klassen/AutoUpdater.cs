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
        // Einzahlautomat: MSI-Installer liegt unter folgendem Pfad
        private const string UpdateUrl = "http://kassenautomat.priwitzer-dienstleistungsgmbh.de/Update_Einzahlautomat/Einzahlautomat_Setup.msi";

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
                    var remoteExt = Path.GetExtension(remoteUrl);
                    if (string.IsNullOrEmpty(remoteExt)) remoteExt = ".msi";
                    tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + remoteExt);
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
                var ext = Path.GetExtension(path);
                if (!string.IsNullOrEmpty(ext) && ext.Equals(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    var mv = GetMsiProductVersion(path);
                    if (mv != null) return mv;
                }
                else
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrEmpty(info.FileVersion))
                    {
                        Version v; if (Version.TryParse(info.FileVersion, out v)) return v;
                    }
                }
            }
            catch { }
            return null;
        }

        private static void SafeDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string BuildUpdateBatch(string srcPath, string targetExe)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal");
            sb.AppendLine("set SRC=\"" + srcPath + "\"");
                sb.AppendLine("set EXE=\"" + targetExe + "\"");
                // Keep unquoted path for directory extraction
                sb.AppendLine("set EXEPATH=" + targetExe);

            var ext = Path.GetExtension(srcPath);
            if (!string.IsNullOrEmpty(ext) && ext.Equals(".msi", StringComparison.OrdinalIgnoreCase))
            {
                var exeName = Path.GetFileName(targetExe);
                if (string.IsNullOrEmpty(exeName)) exeName = "app.exe";
                sb.AppendLine(":waitclose");
                sb.AppendLine("ping 127.0.0.1 -n 2 >nul");
                sb.AppendLine("tasklist /fi \"imagename eq " + exeName + "\" | find /i \"" + exeName + "\" >nul && goto waitclose");
                sb.AppendLine("msiexec /i %SRC% /passive /norestart");
                // After installation, organize PdfSharp-related files into a subfolder to keep program folder clean
                // Determine application directory from %EXE%
                sb.AppendLine("set BASEDIR=%~dp0");
                sb.AppendLine("set APPDIR=%EXEPATH%");
                sb.AppendLine("for %%I in (\"%EXEPATH%\") do set APPDIR=%%~dpI");
                sb.AppendLine("if not exist \"%APPDIR%PDFSharp\" mkdir \"%APPDIR%PDFSharp\"");
                sb.AppendLine("pushd \"%APPDIR%\"");
                // Move common dlls (retry a few times in case MSI still holds locks)
                sb.AppendLine("set RETRIES=5");
                sb.AppendLine(":move_try");
                sb.AppendLine("for %%F in (PdfSharp*.dll MigraDoc*.dll SharpZipLib*.dll) do if exist \"%%F\" move /y \"%%F\" \"%APPDIR%PDFSharp\" >nul");
                sb.AppendLine("for %%F in (*.ttf fonts.json) do if exist \"%%F\" move /y \"%%F\" \"%APPDIR%PDFSharp\" >nul");
                sb.AppendLine("set /a RETRIES-=1");
                sb.AppendLine("if %RETRIES% gtr 0 (");
                sb.AppendLine("  ping 127.0.0.1 -n 2 >nul");
                sb.AppendLine("  goto move_try");
                sb.AppendLine(")");
                sb.AppendLine("popd");
                sb.AppendLine("start \"\" %EXE%");
                sb.AppendLine("del /f /q %SRC% >nul 2>&1");
                sb.AppendLine("del \"%~f0\" >nul 2>&1");
                sb.AppendLine("endlocal");
            }
            else
            {
                sb.AppendLine(":repeat");
                sb.AppendLine("ping 127.0.0.1 -n 2 >nul");
                sb.AppendLine("copy /y %SRC% %EXE% >nul");
                sb.AppendLine("if errorlevel 1 goto repeat");
                sb.AppendLine("start \"\" %EXE%");
                sb.AppendLine("del /f /q %SRC% >nul 2>&1");
                sb.AppendLine("del \"%~f0\" >nul 2>&1");
                sb.AppendLine("endlocal");
            }
            return sb.ToString();
        }

        private static Version GetMsiProductVersion(string msiPath)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WindowsInstaller.Installer");
                if (t == null) return null;
                var installer = Activator.CreateInstance(t);
                // OpenDatabase(msiPath, 0)
                var db = t.InvokeMember("OpenDatabase", BindingFlags.InvokeMethod, null, installer, new object[] { msiPath, 0 });
                if (db == null) return null;
                var dbType = db.GetType();
                var view = dbType.InvokeMember("OpenView", BindingFlags.InvokeMethod, null, db, new object[] { "SELECT Value FROM Property WHERE Property='ProductVersion'" });
                if (view == null) return null;
                var viewType = view.GetType();
                viewType.InvokeMember("Execute", BindingFlags.InvokeMethod, null, view, new object[] { null });
                var rec = viewType.InvokeMember("Fetch", BindingFlags.InvokeMethod, null, view, null);
                if (rec == null) return null;
                var recType = rec.GetType();
                var s = recType.InvokeMember("StringData", BindingFlags.GetProperty, null, rec, new object[] { 1 }) as string;
                Version v; if (!string.IsNullOrEmpty(s) && Version.TryParse(s, out v)) return v;
            }
            catch { }
            return null;
        }
    }
}
