using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace TaMi_Einzahlautomat
{
    public static class AnyDeskHelper
    {
        // Download-URL bereitgestellt vom Nutzer
        private const string AnyDeskUrl = "https://my.anydesk.com/download/IVsJVVTj/SuE_Geldautomat.exe";
        // Installations-Ziel (lokal)
        private static readonly string LocalInstallerPath = Path.Combine(Path.GetTempPath(), "SuE_Geldautomat.exe");
        // Optionaler Zielordner für portable Installation
        private static readonly string LocalAppPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SuE-Software", "AnyDesk");
        private static readonly string LocalExePath = Path.Combine(LocalAppPath, "AnyDesk.exe");

        // Haupt-API: sicherstellen, dass AnyDesk installiert ist und starten
        public static void EnsureInstalledAndOpen(IWin32Window owner)
        {
            try
            {
                if (!IsInstalled())
                {
                    DownloadInstaller(owner);
                    RunInstaller(owner);
                }
                OpenAnyDesk(owner);
            }
            catch (Exception ex)
            {
                try { AppLogger.Log("[AnyDesk] Fehler: " + ex.Message); } catch { }
                throw;
            }
        }

        private static bool IsInstalled()
        {
            try
            {
                // einfache Heuristik: existiert eine AnyDesk.exe im bekannten Pfad oder in Standardpfaden
                if (File.Exists(LocalExePath)) return true;
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AnyDesk", "AnyDesk.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "AnyDesk", "AnyDesk.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnyDesk", "AnyDesk.exe"),
                };
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) return true;
                }
                return false;
            }
            catch { return false; }
        }

        private static void DownloadInstaller(IWin32Window owner)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LocalInstallerPath));
                using (var wc = new WebClient())
                {
                    wc.DownloadProgressChanged += (s, e) => { /* optional: Fortschritt anzeigen */ };
                    wc.DownloadFile(AnyDeskUrl, LocalInstallerPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Download fehlgeschlagen:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw;
            }
        }

        private static void RunInstaller(IWin32Window owner)
        {
            try
            {
                if (!File.Exists(LocalInstallerPath)) throw new FileNotFoundException("Installer nicht gefunden", LocalInstallerPath);
                var psi = new ProcessStartInfo
                {
                    FileName = LocalInstallerPath,
                    UseShellExecute = true,
                    Verb = "runas" // Admin-Rechte anfordern für Installation
                };
                var p = Process.Start(psi);
                p?.WaitForExit(120000); // bis zu 2 Minuten warten
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Installation fehlgeschlagen:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw;
            }
        }

        private static void OpenAnyDesk(IWin32Window owner)
        {
            try
            {
                // Versuch 1: lokaler Pfad
                if (File.Exists(LocalExePath))
                {
                    Process.Start(LocalExePath);
                    return;
                }
                // Versuch 2: Standardpfade
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AnyDesk", "AnyDesk.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "AnyDesk", "AnyDesk.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnyDesk", "AnyDesk.exe"),
                };
                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        Process.Start(c);
                        return;
                    }
                }
                // Fallback: Installer direkt starten (portable Nutzung)
                if (File.Exists(LocalInstallerPath))
                {
                    Process.Start(LocalInstallerPath);
                    return;
                }
                MessageBox.Show(owner, "AnyDesk konnte nicht gefunden oder gestartet werden.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Start fehlgeschlagen:\r\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw;
            }
        }
    }
}
