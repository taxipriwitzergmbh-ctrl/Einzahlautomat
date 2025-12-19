using System;
using System.Diagnostics;
using System.IO;
using System.Management;

namespace Geldautomat
{
    public static class PnpDeviceUtil
    {
        // Neustart des PnP-Geräts, das den angegebenen COM‑Port bereitstellt (z. B. "COM5").
        // Erfordert i. d. R. Administratorrechte.
        public static bool RestartComPort(string comPort, out string log)
        {
            log = "";
            if (string.IsNullOrWhiteSpace(comPort))
            {
                log = "COM-Port fehlt.";
                return false;
            }

            string port = comPort.Trim().ToUpperInvariant();
            if (!port.StartsWith("COM")) port = "COM" + port;

            try
            {
                // 1) PNPDeviceID zum COM-Port ermitteln
                string instanceId = TryResolvePnpInstanceId(port);
                if (string.IsNullOrWhiteSpace(instanceId))
                {
                    log = $"PNPDeviceID zu {port} nicht gefunden.";
                    return false;
                }

                string diag = $"InstanceId: {instanceId}";

                // 2) Versuche 'pnputil /restart-device'
                if (TryPnputilRestart(instanceId, out var log1))
                {
                    log = $"{diag}{Environment.NewLine}{log1}".Trim();
                    return true;
                }

                // 3) Fallback: PowerShell 'Restart-PnpDevice' nur wenn vorhanden
                if (TryPowerShellRestart(instanceId, out var log2))
                {
                    log = $"{diag}{Environment.NewLine}{log2}".Trim();
                    return true;
                }

                // 4) Fallback: pnputil disable/enable
                if (TryPnputilDisableEnable(instanceId, out var log3))
                {
                    log = $"{diag}{Environment.NewLine}{log3}".Trim();
                    return true;
                }

                log = $"{diag}{Environment.NewLine}{log1}{Environment.NewLine}{log2}{Environment.NewLine}{log3}".Trim();
                return false;
            }
            catch (Exception ex)
            {
                log = ex.ToString();
                return false;
            }
        }

        private static string TryResolvePnpInstanceId(string comPortUpper)
        {
            // Primär: Win32_SerialPort (zuverlässig für echte COM-Ports)
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                           "ROOT\\CIMV2",
                           $"SELECT PNPDeviceID, DeviceID FROM Win32_SerialPort WHERE DeviceID = '{comPortUpper}'"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                        return mo["PNPDeviceID"] as string;
                }
            }
            catch { }

            // Fallback: Win32_PnPEntity – sucht nach Gerätenamen, der "(COMx)" enthält
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                           "ROOT\\CIMV2",
                           "SELECT PNPDeviceID, Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%)'"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var name = (mo["Name"] as string) ?? "";
                        if (name.ToUpperInvariant().Contains("(" + comPortUpper + ")"))
                            return mo["PNPDeviceID"] as string;
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool TryPnputilRestart(string instanceId, out string output)
        {
            string exe = GetSystemExe("pnputil.exe");
            return RunProcess(exe, $"/restart-device \"{instanceId}\"", out output);
        }

        private static bool TryPnputilDisableEnable(string instanceId, out string output)
        {
            string exe = GetSystemExe("pnputil.exe");
            output = "";

            // Disable (wenn das scheitert, trotzdem Enable probieren)
            RunProcess(exe, $"/disable-device \"{instanceId}\"", out var o1);

            // Enable
            bool okEnable = RunProcess(exe, $"/enable-device \"{instanceId}\"", out var o2);

            output = (o1 + Environment.NewLine + o2).Trim();
            return okEnable;
        }

        private static bool TryPowerShellRestart(string instanceId, out string output)
        {
            // Prüfe zuerst, ob das Cmdlet überhaupt vorhanden ist
            string ps = GetSystemExe("WindowsPowerShell\\v1.0\\powershell.exe");
            string check = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \""
                         + "if (Get-Command -Name Restart-PnpDevice -ErrorAction SilentlyContinue) { exit 0 } else { exit 2 }\"";

            if (!RunProcess(ps, check, out var checkOut) || LastExitCode != 0 /* wird gleich gesetzt */)
            {
                // ExitCode 2 -> Cmdlet nicht vorhanden, kein Fehlertext anzeigen
                output = "PowerShell-Cmdlet Restart-PnpDevice nicht verfügbar – übersprungen.";
                return false;
            }

            // Cmdlet ist vorhanden -> Neustart versuchen
            string args = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \""
                        + $"Try {{ Restart-PnpDevice -InstanceId '{EscapePs(instanceId)}' -Confirm:$false -ErrorAction Stop; exit 0 }} "
                        + "Catch { $_ | Out-String; exit 1 }\"";

            return RunProcess(ps, args, out output);
        }

        private static string EscapePs(string s) => s?.Replace("'", "''") ?? "";

        // Pfad zu System32-Exe (berücksichtigt WOW64-Redirection)
        private static string GetSystemExe(string relative)
        {
            // relative kann "pnputil.exe" oder "WindowsPowerShell\\v1.0\\powershell.exe" sein
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string pathSystem32 = Path.Combine(win, "System32", relative);
            if (File.Exists(pathSystem32))
                return pathSystem32;

            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            {
                // 32‑Bit Prozess auf 64‑Bit OS -> Sysnative für 64‑Bit Exe
                string pathSysnative = Path.Combine(win, "Sysnative", relative);
                if (File.Exists(pathSysnative))
                    return pathSysnative;
            }

            // Fallback: nur Dateiname (PATH)
            return relative;
        }

        // Letzten ExitCode mitschneiden (nur für Kontrolle im PS‑Check verwendet)
        private static int LastExitCode;

        private static bool RunProcess(string fileName, string args, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    var stdOut = p.StandardOutput.ReadToEnd();
                    var stdErr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    LastExitCode = p.ExitCode;
                    output = (stdOut + Environment.NewLine + stdErr).Trim();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                LastExitCode = -1;
                return false;
            }
        }
    }
}