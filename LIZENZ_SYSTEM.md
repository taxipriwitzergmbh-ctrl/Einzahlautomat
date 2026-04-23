# Lizenzsystem Integration - TaMi Einzahlautomat

## Überblick

Das Lizenzsystem prüft automatisch alle **3 Stunden** die Gültigkeit der Lizenz über den **LAUS-Server** (Lizenz- und Update-Service). 

### Lizenzvalidierung

Die Lizenz ist nur gültig wenn:
1. Server antwortet mit `msg=OK`
2. Entschlüsselte Lizenzinfo enthält **`ezatm=1`**

**Ohne gültige Lizenz ist kein Login möglich** (Ausnahme: Admin-Backdoor "mpr")

### Logging-Modi

**Debug-Modus** (`[App] Debug=1` in INI):
```
LicenseManager: Lizenzinfo entschlüsselt: EZATM-...::fhz=0,ezatm=1::17044::Taxi-Priwitzer...
LicenseManager: Lizenzprüfung erfolgreich (OK)
LicenseManager: Initialisiert – Prüfung alle 3 Stunden
```

**Produktiv-Modus** (`[App] Debug=0` oder nicht gesetzt):
```
LicenseManager: Lizenzprüfung erfolgreich (OK) Kunde:Taxi-Priwitzer, Knd:17044 Einzahlautomat
LicenseManager: Initialisiert – Lizenz Prüfung alle 3 Stunden nächste Prüfung 20:39 Uhr
```

## Technische Details

### Komponenten

1. **LicenseManager.cs** (`Allgemeine_Klassen\LicenseManager.cs`)
   - Zentrale Klasse für Lizenzverwaltung
   - Prüft Lizenz gegen LAUS-Server
   - Automatische Prüfung alle 3 Stunden
   - Thread-sichere Implementierung

2. **Integration in Program.cs**
   - Initialisierung beim Start: `LicenseManager.Initialize()`
   - Shutdown beim Beenden: `LicenseManager.Shutdown()`

3. **Integration in LoginForm.cs**
   - Lizenzprüfung vor jedem Login (Tastatur & NFC)
   - Benutzerfreundliche Fehlermeldungen
   - Admin-Backdoor bleibt funktionsfähig

### LAUS-Server Kommunikation

**URL:** `https://laus.sue-software.de/laus/`

**Request-Parameter:**
- `v=101` - Protokollversion
- `sid={encrypted}` - Verschlüsselte System-ID
- `can={encrypted}` - Verschlüsselter Software-Name (EZATM)
- `cav={encrypted}` - Verschlüsselte Version (1.0.0)

**System-ID Format:**
```
VVVVVVVVXMMMMMMMMMMMXPPPPPPPP

V = Volume Serial Number (8 Hex-Zeichen)
M = MAC-Adresse (12 Hex-Zeichen ohne Trenner)
P = Pfad-CRC32 (8 Hex-Zeichen)

Beispiel: A41C03D7X005056A0CE43XFA46CEAA
```

**Server-Antworten:**
- `OK` - Lizenz gültig
- `EXPIRED` - Lizenz abgelaufen
- `INVALID` - Lizenz ungültig
- `NOTFOUND` - Lizenz nicht gefunden
- Andere - Fehlermeldung

### Verschlüsselung

Die Parameter werden mit dem **tinyEncrypt-Algorithmus** verschlüsselt (kompatibel mit VB6-Version):
- XOR-basierte Verschlüsselung mit 32-Byte-Key
- Hex-Encoding mit speziellem Zeichensatz
- Bidirektionale Ver- und Entschlüsselung

## Verwendung

### API

```csharp
// Status abfragen
bool isValid = LicenseManager.IsLicenseValid;
string lastError = LicenseManager.LastError;
DateTime lastCheck = LicenseManager.LastCheckTime;

// Manuelle Prüfung
bool result = await LicenseManager.CheckLicenseAsync();
```

### Prüfintervall

- **Erste Prüfung:** Beim Programmstart (max. 15 Sekunden Timeout)
- **Folgeprüfungen:** Alle 3 Stunden automatisch
- **Manuelle Prüfung:** Vor jedem Login-Versuch

### Fehlerbehandlung

Bei Netzwerkfehlern oder Server-Ausfall:
- Fehler wird geloggt
- Lizenz wird als ungültig markiert
- Login wird verweigert
- Benutzer erhält Hinweis

## Konfiguration

### INI-Einstellungen

**Debug-Modus:**
```ini
[App]
Debug=1  ; Aktiviert detailliertes Logging (Standard: 0)
```

**Lizenz-Parameter** (hardcoded, keine INI-Einstellung):
- Software-ID: `EZATM-03D77D5CX005056A0CE43XFA46CEAA`
- Software-Name: `EZATM`
- Version: `1.0.0`
- Erforderliche Lizenz: `ezatm=1`

## Logging

### Debug-Modus aktiviert (`Debug=1`)

Detailliertes Logging mit vollständiger Lizenzinfo:

```
2026-04-23 17:34:30.375 | LicenseManager: Initialisierung gestartet
2026-04-23 17:34:30.452 | LicenseManager: Prüfe Lizenz... (System-ID: EZATM-98618CCAX50814...)
2026-04-23 17:34:30.756 | LicenseManager: Lizenzinfo entschlüsselt: EZATM-98618CCAX50814028EEC0X1204A956::0::20260722103000::fhz=0,ezatm=1::17044::Taxi-Priwitzer::::c1e44c6fff7374b28025d1fe7d4ae9ef
2026-04-23 17:34:30.756 | LicenseManager: Lizenzprüfung erfolgreich (OK)
2026-04-23 17:34:30.767 | LicenseManager: Initialisiert – Prüfung alle 3 Stunden
```

### Produktiv-Modus (`Debug=0` oder nicht gesetzt)

Vereinfachtes Logging für Endanwender:

```
2026-04-23 17:36:33.626 | LicenseManager: Lizenzprüfung erfolgreich (OK) Kunde:Taxi-Priwitzer, Knd:17044 Einzahlautomat
2026-04-23 17:36:33.638 | LicenseManager: Initialisiert – Lizenz Prüfung alle 3 Stunden nächste Prüfung 20:39 Uhr
```

### Bei Fehlern

```
2024-01-15 10:00:02.456 | LicenseManager: Lizenz ungültig – Fehler: Keine gültige EZATM-Lizenz
2024-01-15 10:05:00.123 | Login verweigert – Lizenzfehler: Keine gültige EZATM-Lizenz
```

### Sicherheit

### Maßnahmen

1. **Thread-sichere Implementierung** mit `lock(_lock)`
2. **Timeout** von 10 Sekunden für Server-Anfragen
3. **Verschlüsselte Übertragung** der Parameter
4. **Hardware-Bindung** (Volume-ID, MAC, Pfad)
5. **Lizenz-Validierung**: `ezatm=1` muss in Lizenzinfo vorhanden sein
6. **Admin-Backdoor "mpr"** funktioniert auch ohne gültige Lizenz

### Hinweise

- MAC-Adresse wird von der ersten physischen Ethernet-Karte ausgelesen
- Bei virtuellen Maschinen (VMware, Hyper-V) wird vmbus-Adapter akzeptiert
- CRC32 wird über den Installationspfad berechnet
- **Login ist nur mit gültiger Lizenz (`ezatm=1`) möglich**
- Ausnahme: Admin-Backdoor "mpr" (Text-basiert) oder "2602" (numerisch)

## Fehlerbehebung

### Häufige Probleme

**Problem:** Lizenz wird nicht geprüft
- **Lösung:** Log prüfen, ob `LicenseManager: Initialisiert` erscheint
- **Prüfung:** `LicenseManager.LastCheckTime` sollte gesetzt sein

**Problem:** System-ID kann nicht ermittelt werden
- **Lösung:** 
  - Festplatte prüfen (Volume Serial Number)
  - Netzwerkkarte prüfen (physischer Ethernet-Adapter)
  - Installationspfad prüfen

**Problem:** Server nicht erreichbar
- **Lösung:**
  - Internetverbindung prüfen
  - Firewall-Regeln prüfen
  - HTTPS-Zugriff auf `laus.sue-software.de` testen

**Problem:** Lizenz abgelaufen
- **Lösung:** Support kontaktieren mit System-ID aus Log

### Debug-Informationen sammeln

```csharp
// In LoginForm oder AdminForm:
var isValid = LicenseManager.IsLicenseValid;
var lastError = LicenseManager.LastError;
var lastCheck = LicenseManager.LastCheckTime;

MessageBox.Show($"Lizenz: {(isValid ? "Gültig" : "Ungültig")}\n" +
                $"Fehler: {lastError}\n" +
                $"Letzte Prüfung: {lastCheck}");
```

## Migration / Rollback

### Deaktivierung

Falls das Lizenzsystem temporär deaktiviert werden muss:

1. **Option A - Kommentieren:**
   ```csharp
   // In LoginForm.cs btnLogin_Click und HandleNfcAsync:
   /*
   if (!LicenseManager.IsLicenseValid)
   {
       // ... Lizenzprüfung ...
       return;
   }
   */
   ```

2. **Option B - Mock:**
   ```csharp
   // In LicenseManager.cs CheckLicenseAsync:
   // Erste Zeile hinzufügen:
   lock (_lock) { _isLicenseValid = true; return true; }
   ```

### Vollständige Entfernung

1. `LicenseManager.cs` löschen
2. Aufrufe in `Program.cs` entfernen (Initialize/Shutdown)
3. Prüfungen in `LoginForm.cs` entfernen (btnLogin_Click/HandleNfcAsync)

## Erweiterungen

Mögliche zukünftige Erweiterungen:

1. **Offline-Gnadenfrist:** 7 Tage ohne Server-Verbindung erlauben
2. **Lizenz-Cache:** Letzte gültige Prüfung speichern
3. **Benachrichtigungen:** Warnung 30 Tage vor Ablauf
4. **Dashboard:** Lizenzstatus in Admin-Übersicht anzeigen
5. **Mehr Geräte-Informationen:** CPU-ID, BIOS-Seriennummer

## Support

Bei Fragen zum Lizenzsystem:
- **E-Mail:** support@sue-software.de
- **Log-Datei:** `Ereignisse\{Datum}.log`
- **System-ID:** Siehe Log nach `LicenseManager: Prüfe Lizenz...`
