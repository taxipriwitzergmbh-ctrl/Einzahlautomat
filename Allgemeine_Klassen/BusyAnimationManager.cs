using System;
using System.Threading;
using System.Windows.Forms;

namespace TaMi_Einzahlautomat
{
    // Zentrale, threadsichere Verwaltung einer einzigen ProcessingAnimation
    internal static class BusyAnimationManager
    {
        private static readonly object _lock = new object();
        private static int _refCount = 0; // jetzt nur 0 oder 1 – keine Verschachtelung mehr
        private static ProcessingAnimation _anim;
        private static string _currentText;
        private static SynchronizationContext _uiCtx = SynchronizationContext.Current;
        private static DateTime _lastChangeUtc = DateTime.MinValue;
        private static System.Windows.Forms.Timer _pumpTimer; // NEU: periodischer Pump-Aufruf
        private static DateTime _lastBeginUtc = DateTime.MinValue; // NEU: für Timeout

        static BusyAnimationManager()
        {
            try
            {
                _pumpTimer = new System.Windows.Forms.Timer { Interval = 3000 }; // alle 3s prüfen
                _pumpTimer.Tick += (s, e) =>
                {
                    try { Pump(); } catch { }
                    try { SafetyTimeoutCheck(); } catch { }
                };
                _pumpTimer.Start();
            }
            catch { }
        }

        public static bool IsActive { get { lock (_lock) return _refCount > 0 && _anim != null; } }
        public static int CurrentRefCount { get { lock (_lock) return _refCount; } }

        public static IDisposable Scope(string text = null)
        {
            Begin(text);
            return new ScopeToken();
        }
        private sealed class ScopeToken : IDisposable { private bool _d; public void Dispose() { if (_d) return; _d = true; End("scope"); } }

        public static void Begin(string text = null)
        {
            try
            {
                bool create = false;
                lock (_lock)
                {
                    if (_refCount == 0)
                    {
                        _refCount = 1; // nur erster Begin zählt
                        create = true;
                        _lastChangeUtc = DateTime.UtcNow;
                        _lastBeginUtc = _lastChangeUtc;
                        _currentText = string.IsNullOrWhiteSpace(text) ? "Vorgang läuft" : text.Trim();
                    }
                    else
                    {
                        // Bereits aktiv – nur optional Text aktualisieren
                        if (!string.IsNullOrWhiteSpace(text) && text != _currentText)
                        {
                            _currentText = text.Trim();
                            _lastChangeUtc = DateTime.UtcNow;
                        }
                    }
                }
                if (!create) return;

                void ShowAnim()
                {
                    try
                    {
                        if (_anim != null) return;
                        Form owner = null;
                        try { owner = AbrechnungForm.FindOpenInstance(); } catch { }
                        if (owner == null) { try { owner = Program.BackgroundFormInstance; } catch { } }
                        _anim = ProcessingAnimation.Show(owner, _currentText);
                    }
                    catch { }
                }
                if (Application.MessageLoop) ShowAnim(); else _uiCtx?.Post(_ => ShowAnim(), null);
            }
            catch { }
        }

        public static void End(string reason = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(reason))
                {
                    var r = reason.ToLowerInvariant();
                    if (r.Contains("fehler") || r.Contains("error") || r.Contains("jam") || r.Contains("timeout") || r.Contains("abort"))
                    { EndForce(); return; }
                }
                ProcessingAnimation toDispose = null;
                lock (_lock)
                {
                    if (_refCount > 0) _refCount = 0; // sofort auf 0 – keine Verschachtelung mehr
                    if (_anim != null) { toDispose = _anim; _anim = null; _currentText = null; }
                    _lastChangeUtc = DateTime.UtcNow;
                }
                if (toDispose != null) DisposeOnUi(toDispose);
            }
            catch { }
        }

        public static void EndForce()
        {
            try
            {
                ProcessingAnimation toDispose = null;
                lock (_lock)
                {
                    if (_uiCtx == null) _uiCtx = SynchronizationContext.Current;
                    _refCount = 0;
                    if (_anim != null) { toDispose = _anim; _anim = null; _currentText = null; }
                    _lastChangeUtc = DateTime.UtcNow;
                }
                if (toDispose != null) DisposeOnUi(toDispose);
            }
            catch { }
        }

        // NEU: Sicherheits-Timeout falls Animation länger "festhängt" (Geräte-Fehler / verpasster End-Aufruf)
        private static void SafetyTimeoutCheck()
        {
            try
            {
                ProcessingAnimation stuckAnim = null;
                lock (_lock)
                {
                    if (_refCount > 0 && _anim != null)
                    {
                        var now = DateTime.UtcNow;
                        var activeFor = now - _lastBeginUtc;
                        // Nach 60s ohne Textänderung oder nach insgesamt 120s zwingend schließen
                        if ((activeFor > TimeSpan.FromSeconds(60) && (now - _lastChangeUtc) > TimeSpan.FromSeconds(55))
                            || activeFor > TimeSpan.FromSeconds(120))
                        {
                            stuckAnim = _anim;
                            _anim = null;
                            _refCount = 0;
                            _currentText = null;
                            _lastChangeUtc = now;
                        }
                    }
                }
                if (stuckAnim != null)
                {
                    DisposeOnUi(stuckAnim);
                    try { AppLogger.Log("BusyAnimationManager: Sicherheits-Timeout – Animation auto-geschlossen"); } catch { }
                }
            }
            catch { }
        }

        public static void Pump()
        {
            try
            {
                ProcessingAnimation toDispose = null;
                lock (_lock)
                {
                    if (_refCount == 0 && _anim != null) { toDispose = _anim; _anim = null; }
                    else if (_anim != null && (DateTime.UtcNow - _lastChangeUtc) > TimeSpan.FromMinutes(3)) { _refCount = 0; toDispose = _anim; _anim = null; }
                }
                if (toDispose != null) DisposeOnUi(toDispose);
            }
            catch { }
        }

        private static void DisposeOnUi(ProcessingAnimation anim)
        {
            if (anim == null) return;
            void Act() { try { anim.Dispose(); } catch { } }
            if (Application.MessageLoop)
            {
                // Wir sind auf einem UI-Thread
                Act();
                return;
            }
            // Versuche über SynchronizationContext
            if (_uiCtx != null)
            {
                try { _uiCtx.Post(_ => Act(), null); return; } catch { }
            }
            // Fallback: über ein offenes Formular marshallen
            try
            {
                Form owner = null;
                try { owner = AbrechnungForm.FindOpenInstance(); } catch { }
                if (owner == null) { try { owner = Program.BackgroundFormInstance; } catch { }
                }
                if (owner != null && owner.IsHandleCreated)
                {
                    owner.BeginInvoke((Action)(() => Act()));
                    return;
                }
            }
            catch { }
            // Letzter Versuch: direkt dispose (kann auf Background-Thread fehlschlagen, aber besser als hängen bleiben)
            try { Act(); } catch { }
        }

        public static void ForceReset() => EndForce();
    }
}
