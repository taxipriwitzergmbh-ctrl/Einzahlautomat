using System;
using System.Collections.Concurrent; // NEU Cache
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using SuE.TaMi;

namespace Geldautomat
{
    // ...bestehende Klassen FahrzeugInfo, PersonalStatus, LoginContext...
    public class FahrzeugInfo { public int FID { get; set; } public string Kennzeichen { get; set; } }
    
    public class PersonalStatus 
    { 
        public bool Gesperrt { get; set; } 
        public DateTime? EintrittAm { get; set; } 
        public DateTime? AustrittAm { get; set; }  

        public int AppRights1 { get; set; }
        public int AppRights2 { get; set; }

        public bool IsAutomatAdmin 
        { 
            get { return (this.AppRights2 & (int)SuE.TaMi.AppRights2.AUTOMAT_ADMIN) == (int)SuE.TaMi.AppRights2.AUTOMAT_ADMIN; } 
        }
    }

    public class LoginContext
    { 
        public PersonalInfo Personal { get; set; } 
        public ShiftDetails Shift { get; set; } 
        public decimal Guthaben { get; set; } 
    }

    // Info aus TKassenbuchDevice
    public class KassenbuchDeviceInfo
    {
        public int DeviceID { get; set; }
        public string AutomatenName { get; set; }
        public string Standort { get; set; }
        public bool Gesperrt { get; set; }
        public int? AllowedManID { get; set; }
    }


    public class DatabaseHelper : IDisposable
    {
        private readonly SqlConnection _connection;
        private int CurrentDeviceId => AppSettings.DeviceId;

        // Cache für PersonalStatus + Fahrercode (TTL kurz, um DB zu entlasten bei wiederholtem Login-Versuch)
        private struct PersonalCacheEntry { public PersonalStatus Status; public string Code; public DateTime ExpiryUtc; }
        private static readonly ConcurrentDictionary<int, PersonalCacheEntry> _personalCache = new ConcurrentDictionary<int, PersonalCacheEntry>();
        private static TimeSpan _personalCacheTtl = TimeSpan.FromSeconds(30);
        public static void SetPersonalCacheTtlSeconds(int s) { if (s > 0 && s <= 600) _personalCacheTtl = TimeSpan.FromSeconds(s); }

        public DatabaseHelper() { _connection = new SqlConnection(GetConnectionString()); }

        public static string GetConnectionString()
        {
            string iniPath = @"C:\\ProgramData\\SuE-Software\\SuE-TaMi Client SQL\\TaMi Client.ini";
            string server = "localhost,1433";
            if (File.Exists(iniPath))
            {
                foreach (var line in File.ReadAllLines(iniPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Server=") && !trimmed.StartsWith("#")) { server = trimmed.Substring("Server=".Length); break; }
                }
            }
            string user = "TaMiCli"; string password = "tami"; string dbName = "SuE-TaMi";
            return $"Data Source={server};Initial Catalog={dbName};User ID={user};Password={password};Network Library=DBMSSOCN;";
        }

        // TKassenbuchDevice anhand der current DeviceID laden
        public async Task<KassenbuchDeviceInfo> GetKassenbuchDeviceInfoAsync(int deviceId)
        {
            if (deviceId <= 0) return null;
            await EnsureOpenAsync().ConfigureAwait(false);
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT TOP 1 DeviceID, AutomatenName, Standort, Gesperrt, AllowedManID FROM TKassenbuchDevice WITH (NOLOCK) WHERE DeviceID=@DID";
                cmd.Parameters.AddWithValue("@DID", deviceId);
                using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow).ConfigureAwait(false))
                {
                    if (!await rdr.ReadAsync().ConfigureAwait(false)) return null;
                    var info = new KassenbuchDeviceInfo
                    {
                        DeviceID = rdr.IsDBNull(rdr.GetOrdinal("DeviceID")) ? deviceId : Convert.ToInt32(rdr["DeviceID"]),
                        AutomatenName = rdr.IsDBNull(rdr.GetOrdinal("AutomatenName")) ? null : Convert.ToString(rdr["AutomatenName"]),
                        Standort = rdr.IsDBNull(rdr.GetOrdinal("Standort")) ? null : Convert.ToString(rdr["Standort"]),
                        Gesperrt = !rdr.IsDBNull(rdr.GetOrdinal("Gesperrt")) && Convert.ToBoolean(rdr["Gesperrt"]),
                        AllowedManID = rdr.IsDBNull(rdr.GetOrdinal("AllowedManID")) ? (int?)null : Convert.ToInt32(rdr["AllowedManID"]) 
                    };
                    return info;
                }
            }
        }

        private async Task EnsureOpenAsync()
        { if (_connection.State != ConnectionState.Open) await _connection.OpenAsync().ConfigureAwait(false); }

        public async Task<bool> PersIdExistsAsync(int persId)
        {
            await EnsureOpenAsync().ConfigureAwait(false);
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT TOP 1 1 FROM TSchichten WITH (NOLOCK) WHERE PersId = @PersId";
                cmd.Parameters.AddWithValue("@PersId", persId);
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return result != null && result != DBNull.Value;
            }
        }
        public async Task<bool> PersonalIdExistsAsync(int pid)
        { await EnsureOpenAsync().ConfigureAwait(false); using (var cmd = _connection.CreateCommand()) { cmd.CommandText = "SELECT TOP 1 1 FROM TPersonal WITH (NOLOCK) WHERE PID = @PID"; cmd.Parameters.AddWithValue("@PID", pid); var r = await cmd.ExecuteScalarAsync().ConfigureAwait(false); return r != null && r != DBNull.Value; } }

        // Kombinierte Meta-Abfrage für Login (Existenz + Status + Fahrercode) – reduziert Roundtrips
        public async Task<(bool exists, PersonalStatus status, string fahrercode)> GetPersonalLoginMetaAsync(int pid)
        {
            if (pid <= 0) return (false, null, null);
            // Cache-Hit?
            if (_personalCache.TryGetValue(pid, out var entry) && DateTime.UtcNow < entry.ExpiryUtc)
                return (true, entry.Status, entry.Code);

            await EnsureOpenAsync().ConfigureAwait(false);
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT PID, Gesperrt, EintrittAm, AustrittAm, AppRechte, Fahrercode FROM TPersonal WITH (NOLOCK) WHERE PID=@PID";
                cmd.Parameters.AddWithValue("@PID", pid);

                using (var rdr = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow).ConfigureAwait(false))
                {
                    if (!await rdr.ReadAsync().ConfigureAwait(false)) 
                        return (false, null, null);

                    string appRechte = rdr["AppRechte"].ToString();
                    ParseAppRechteToUints(appRechte, out uint appRights1, out uint appRigths2);

                    var status = new PersonalStatus
                    {
                        Gesperrt = !rdr.IsDBNull(rdr.GetOrdinal("Gesperrt")) && Convert.ToBoolean(rdr["Gesperrt"]),
                        EintrittAm = rdr.IsDBNull(rdr.GetOrdinal("EintrittAm")) ? (DateTime?)null : Convert.ToDateTime(rdr["EintrittAm"]),
                        AustrittAm = rdr.IsDBNull(rdr.GetOrdinal("AustrittAm")) ? (DateTime?)null : Convert.ToDateTime(rdr["AustrittAm"]),
                        AppRights1 = (int)appRights1,
                        AppRights2 = (int)appRigths2,
                    };

                    string code = rdr.IsDBNull(rdr.GetOrdinal("Fahrercode")) ? null : rdr["Fahrercode"].ToString();
                    _personalCache[pid] = new PersonalCacheEntry { Status = status, Code = code, ExpiryUtc = DateTime.UtcNow + _personalCacheTtl };
                    
                    return (true, status, code);
                }
            }
        }

        public async Task<PersonalStatus> GetPersonalStatusAsync(int pid)
        {
            var meta = await GetPersonalLoginMetaAsync(pid).ConfigureAwait(false); return meta.exists ? meta.status : null;
        }

        private static void ParseAppRechteToUints(string appRechte, out uint u1, out uint u2)
        {
            u1 = 0u; u2 = 0u;
            if (string.IsNullOrEmpty(appRechte))
                return;

            // Nur die ersten 8 Bytes verwenden
            byte[] bytes = Encoding.ASCII.GetBytes(appRechte);
            int len = Math.Min(8, bytes.Length);

            // 8-Byte Puffer anlegen: truncate oder pad mit 0
            var b = new byte[8];
            for (int i = 0; i < len; i++) b[i] = bytes[i];
            // Rest bleibt 0

            // Little-Endian in zwei uints umwandeln (Bytes 0..3 und 4..7)
            u1 = (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
            u2 = (uint)(b[4] | (b[5] << 8) | (b[6] << 16) | (b[7] << 24));
        }



        public async Task<DataTable> GetOpenShiftsListAsync(int persId)
        {
            await EnsureOpenAsync().ConfigureAwait(false);
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"SELECT s.SchichtId,s.PersId,s.PersName,s.FhzId,s.StartZeit,ISNULL(s.EinnahmenBar1,0)-ISNULL(s.EinzahlungFahrer1,0) AS Betrag19,ISNULL(s.EinnahmenBar2,0)-ISNULL(s.EinzahlungFahrer2,0) AS Betrag7,ISNULL(s.EinnahmenBar3,0)-ISNULL(s.EinzahlungFahrer3,0) AS Betrag0,CAST(ISNULL(s.EinnahmenBar1,0)+ISNULL(s.EinnahmenBar2,0)+ISNULL(s.EinnahmenBar3,0)-ISNULL(s.EinzahlungFahrer,0) AS money) AS OffenerBetrag FROM TSchichten s WITH (NOLOCK) WHERE (s.Flags & 1)=0 AND (s.Flags & 4)=0 AND s.PersId=@PersId ORDER BY s.StartZeit DESC;";
                cmd.Parameters.AddWithValue("@PersId", persId);
                using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess).ConfigureAwait(false)) { var dt = new DataTable(); dt.Load(reader); return dt; }
            }
        }

        public async Task<ShiftDetails> GetShiftDetailsAsync(int schichtId)
        {
            await EnsureOpenAsync().ConfigureAwait(false);
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"SELECT SchichtId,PersId,PersName,FhzId,ManID,StartZeit,ISNULL(EinnahmenBar1,0)-ISNULL(EinzahlungFahrer1,0) AS Betrag19,ISNULL(EinnahmenBar2,0)-ISNULL(EinzahlungFahrer2,0) AS Betrag7,ISNULL(EinnahmenBar3,0)-ISNULL(EinzahlungFahrer3,0) AS Betrag0,ISNULL(EinzahlungFahrer,0) AS EinzahlungBisher FROM TSchichten WITH (NOLOCK) WHERE SchichtId=@SchichtId;";
                cmd.Parameters.AddWithValue("@SchichtId", schichtId);
                using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow).ConfigureAwait(false))
                {
                    if (!reader.Read()) return null;
                    return new ShiftDetails
                    {
                        SchichtId = reader.GetInt32(reader.GetOrdinal("SchichtId")),
                        PersId = reader.GetInt32(reader.GetOrdinal("PersId")),
                        PersName = reader.GetString(reader.GetOrdinal("PersName")),
                        FhzId = reader.GetInt32(reader.GetOrdinal("FhzId")),
                        ManId = Convert.ToInt32(reader["ManID"]),
                        StartZeit = reader.GetDateTime(reader.GetOrdinal("StartZeit")),
                        Betrag19 = reader.GetDecimal(reader.GetOrdinal("Betrag19")),
                        Betrag7 = reader.GetDecimal(reader.GetOrdinal("Betrag7")),
                        Betrag0 = reader.GetDecimal(reader.GetOrdinal("Betrag0")),
                        EinzahlungBisher = reader.GetDecimal(reader.GetOrdinal("EinzahlungBisher"))
                    };
                }
            }
        }

        // TKassenbuch: Eintrag erzeugen inkl. Kassenbestand-Fortschreibung (pro FirmenId+DeviceID)
        public async Task<int> InsertKassenbuchAsync(KassenbuchEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (CurrentDeviceId <= 0) throw new InvalidOperationException("DeviceID ungültig (0) – Abbruch.");
            await EnsureOpenAsync().ConfigureAwait(false);
            if (entry.FhzId == 0 && entry.SchichtId > 0)
            {
                try
                { using (var cmdFhz = _connection.CreateCommand()) { cmdFhz.CommandText = "SELECT FhzId FROM TSchichten WITH (NOLOCK) WHERE SchichtId=@SID"; cmdFhz.Parameters.AddWithValue("@SID", entry.SchichtId); var o = await cmdFhz.ExecuteScalarAsync().ConfigureAwait(false); if (o != null && o != DBNull.Value && int.TryParse(o.ToString(), out var fhz)) entry.FhzId = fhz; } }
                catch { }
            }
            var deviceId = CurrentDeviceId;
            var typCode = MapTypStringToCode(entry.Typ); bool isPersonalguthaben = typCode == 5; decimal previousPgSaldo = 0m;
            if (isPersonalguthaben && entry.PersId > 0)
            {
                try { using (var cmdPrev = _connection.CreateCommand()) { cmdPrev.CommandText = "SELECT TOP 1 SaldoPersonalguthaben FROM TKassenbuch WITH (NOLOCK) WHERE PersId=@pid AND Typ=5 AND DeviceID=@DID AND ISNULL(RevIsOld,0)=0 ORDER BY ErfasstAm DESC, Belegnummer DESC"; cmdPrev.Parameters.AddWithValue("@pid", entry.PersId); cmdPrev.Parameters.AddWithValue("@DID", deviceId); var o = await cmdPrev.ExecuteScalarAsync().ConfigureAwait(false); if (o != null && o != DBNull.Value) previousPgSaldo = Convert.ToDecimal(o); } } catch { }
            }
            decimal letzterKassenbestand = 0m;
            using (var cmdLast = _connection.CreateCommand())
            {
                cmdLast.CommandText = "SELECT TOP 1 Kassenbestand FROM TKassenbuch WITH (HOLDLOCK,UPDLOCK) WHERE FirmenId=@FID AND DeviceID=@DID AND ISNULL(RevIsOld,0)=0 ORDER BY ErfasstAm DESC, Belegnummer DESC";
                cmdLast.Parameters.AddWithValue("@FID", entry.FirmenId); cmdLast.Parameters.AddWithValue("@DID", deviceId);
                var res = await cmdLast.ExecuteScalarAsync().ConfigureAwait(false); if (res != null && res != DBNull.Value) letzterKassenbestand = Convert.ToDecimal(res);
            }
            var delta = entry.BetragGesamt != 0m ? entry.BetragGesamt : (entry.Betrag19 + entry.Betrag7 + entry.Betrag0); var neuerKassenbestand = letzterKassenbestand + delta; if (neuerKassenbestand < 0m) throw new InvalidOperationException("NEGATIVE_KASSENBESTAND");
            int? kassenBelegnummer = null;
            try
            { using (var cmdHas = _connection.CreateCommand()) { cmdHas.CommandText = "SELECT 1 FROM sys.columns WHERE Name='KassenBelegnummer' AND Object_ID=OBJECT_ID('dbo.TKassenbuch')"; var has = await cmdHas.ExecuteScalarAsync().ConfigureAwait(false); if (has != null && has != DBNull.Value) { using (var cmdLastNo = _connection.CreateCommand()) { cmdLastNo.CommandText = "SELECT TOP 1 ISNULL(KassenBelegnummer,0) FROM TKassenbuch WITH (NOLOCK) WHERE FirmenId=@FID AND DeviceID=@DID AND ISNULL(RevIsOld,0)=0 ORDER BY ErfasstAm DESC, Belegnummer DESC"; cmdLastNo.Parameters.AddWithValue("@FID", entry.FirmenId); cmdLastNo.Parameters.AddWithValue("@DID", deviceId); var o = await cmdLastNo.ExecuteScalarAsync().ConfigureAwait(false); int last = (o != null && o != DBNull.Value) ? Convert.ToInt32(o) : 0; kassenBelegnummer = last + 1; } } } }
            catch { }
            using (var cmd = _connection.CreateCommand())
            {
                if (kassenBelegnummer.HasValue) cmd.CommandText = "INSERT INTO TKassenbuch (Typ,Buchungstext,Betrag19,Betrag7,Betrag0,Kassenbestand,Kost1,Kost2,Konto,PersId,SchichtId,ErfasstAm,SaldoPersonalguthaben,FirmenId,DeviceID,FhzId,KassenBelegnummer) VALUES (@Typ,@Buchungstext,@Betrag19,@Betrag7,@Betrag0,@Kassenbestand,@Kost1,@Kost2,@Konto,@PersId,@SchichtId,SYSDATETIME(),@SaldoPG,@FirmenID,@DeviceID,@FhzId,@KassenBelegnummer); SELECT SCOPE_IDENTITY();"; else cmd.CommandText = "INSERT INTO TKassenbuch (Typ,Buchungstext,Betrag19,Betrag7,Betrag0,Kassenbestand,Kost1,Kost2,Konto,PersId,SchichtId,ErfasstAm,SaldoPersonalguthaben,FirmenId,DeviceID,FhzId) VALUES (@Typ,@Buchungstext,@Betrag19,@Betrag7,@Betrag0,@Kassenbestand,@Kost1,@Kost2,@Konto,@PersId,@SchichtId,SYSDATETIME(),@SaldoPG,@FirmenID,@DeviceID,@FhzId); SELECT SCOPE_IDENTITY();";
                cmd.Parameters.Add("@Typ", SqlDbType.TinyInt).Value = typCode; cmd.Parameters.AddWithValue("@Buchungstext", (object)entry.Buchungstext ?? DBNull.Value); cmd.Parameters.AddWithValue("@Betrag19", entry.Betrag19); cmd.Parameters.AddWithValue("@Betrag7", entry.Betrag7); cmd.Parameters.AddWithValue("@Betrag0", entry.Betrag0); cmd.Parameters.AddWithValue("@Kassenbestand", neuerKassenbestand); cmd.Parameters.AddWithValue("@Kost1", entry.Kost1); cmd.Parameters.AddWithValue("@Kost2", entry.Kost2); cmd.Parameters.AddWithValue("@Konto", entry.Konto); cmd.Parameters.AddWithValue("@PersId", entry.PersId); cmd.Parameters.AddWithValue("@SchichtId", entry.SchichtId); cmd.Parameters.AddWithValue("@SaldoPG", entry.SaldoPersonalguthaben); cmd.Parameters.AddWithValue("@FirmenID", entry.FirmenId); cmd.Parameters.AddWithValue("@DeviceID", deviceId); cmd.Parameters.AddWithValue("@FhzId", entry.FhzId); if (kassenBelegnummer.HasValue) cmd.Parameters.AddWithValue("@KassenBelegnummer", kassenBelegnummer.Value);
                var newIdObj = await cmd.ExecuteScalarAsync().ConfigureAwait(false); int newId = Convert.ToInt32(Convert.ToDecimal(newIdObj));
                if (isPersonalguthaben)
                { try { decimal betragDelta = entry.Betrag19 + entry.Betrag7 + entry.Betrag0; decimal newSaldo = (entry.SaldoPersonalguthaben != 0m) ? entry.SaldoPersonalguthaben : previousPgSaldo + betragDelta; decimal diff = newSaldo - previousPgSaldo; AppLogger.Log($"Personalguthaben Änderung PID={entry.PersId}: {diff:+0.00;-0.00;0.00} € -> Saldo neu {newSaldo:0.00} €"); } catch { } }
                return newId;
            }
        }

        public async Task<int> UpdateSchichtEinzahlungAsync(int schichtId, int persId, decimal sessionEingezahlt, decimal betrag19, decimal betrag7, decimal betrag0)
        { await EnsureOpenAsync().ConfigureAwait(false); using (var cmd = _connection.CreateCommand()) { cmd.CommandText = "UPDATE TSchichten SET EinzahlungFahrer1=ISNULL(EinzahlungFahrer1,0)+@Betrag19,EinzahlungFahrer2=ISNULL(EinzahlungFahrer2,0)+@Betrag7,EinzahlungFahrer3=ISNULL(EinzahlungFahrer3,0)+@Betrag0,EinzahlungFahrer=ISNULL(EinzahlungFahrer1,0)+@Betrag19+ISNULL(EinzahlungFahrer2,0)+@Betrag7+ISNULL(EinzahlungFahrer3,0)+@Betrag0,Flags=Flags|1,AusgebuchtZeit=SYSDATETIME(),AusgebuchtPersId=@PersId WHERE SchichtId=@SchichtId"; cmd.Parameters.AddWithValue("@SessionEingezahlt", sessionEingezahlt); cmd.Parameters.AddWithValue("@Betrag19", betrag19); cmd.Parameters.AddWithValue("@Betrag7", betrag7); cmd.Parameters.AddWithValue("@Betrag0", betrag0); cmd.Parameters.AddWithValue("@PersId", persId); cmd.Parameters.AddWithValue("@SchichtId", schichtId); return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false); } }



        public async Task<decimal> GetLastPersonalGuthabenSaldoAsync(int persId)
        { await EnsureOpenAsync().ConfigureAwait(false); using (var cmd = _connection.CreateCommand()) { cmd.CommandText = "SELECT TOP 1 SaldoPersonalguthaben FROM TKassenbuch WITH (NOLOCK) WHERE PersId=@PersId AND Typ=5 AND DeviceID=@DID ORDER BY ErfasstAm DESC"; cmd.Parameters.AddWithValue("@PersId", persId); cmd.Parameters.AddWithValue("@DID", CurrentDeviceId); var o = await cmd.ExecuteScalarAsync().ConfigureAwait(false); return (o == null || o == DBNull.Value) ? 0m : Convert.ToDecimal(o); } }

        public async Task<FahrzeugInfo> GetFahrzeugInfoAsync(int fid)
        { await EnsureOpenAsync().ConfigureAwait(false); using (var cmd = _connection.CreateCommand()) { cmd.CommandText = "SELECT FID,Kennzeichen FROM TFahrzeuge WHERE FID=@FID"; cmd.Parameters.AddWithValue("@FID", fid); using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false)) { if (await reader.ReadAsync().ConfigureAwait(false)) return new FahrzeugInfo { FID = reader.GetInt32(reader.GetOrdinal("FID")), Kennzeichen = reader["Kennzeichen"] as string ?? "" }; } } return null; }

        public async Task<DataTable> GetShiftSelectionTableAsync(int persId)
        { await EnsureOpenAsync().ConfigureAwait(false); using (var cmd = _connection.CreateCommand()) { cmd.CommandText = "SELECT s.SchichtId,s.StartZeit,s.FhzId,f.Kennzeichen,CAST(ISNULL(s.EinnahmenBar1,0)+ISNULL(s.EinnahmenBar2,0)+ISNULL(s.EinnahmenBar3,0) AS money) AS EinnahmenBar FROM TSchichten s LEFT JOIN TFahrzeuge f ON s.FhzId=f.FID WHERE s.Flags=0 AND s.PersId=@PersId ORDER BY s.StartZeit DESC"; cmd.Parameters.AddWithValue("@PersId", persId); using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false)) { var dt = new DataTable(); dt.Load(reader); return dt; } } }

        public async Task<DataTable> GetLatestKassenbestaendeAsync(string _ignored)
        { await EnsureOpenAsync().ConfigureAwait(false); using (var cmd = _connection.CreateCommand()) { cmd.CommandText = @";WITH x AS (SELECT k.FirmenId,k.Kassenbestand,k.ErfasstAm,ROW_NUMBER() OVER (PARTITION BY k.FirmenId ORDER BY k.ErfasstAm DESC,k.Belegnummer DESC) rn FROM TKassenbuch k WITH (NOLOCK) WHERE k.DeviceID=@DID AND ISNULL(k.RevIsOld,0)=0) SELECT x.FirmenId,CASE WHEN x.FirmenId=-1 THEN 'Personalguthaben' WHEN x.FirmenId IS NULL THEN 'Unbekannt' ELSE ISNULL(m.ManName,'ID '+CAST(x.FirmenId AS varchar(10))) END AS ManName,x.Kassenbestand,x.ErfasstAm,m.Kto1IBAN FROM x LEFT JOIN TMandanten m ON (x.FirmenId>=0 AND m.ManID=x.FirmenId) WHERE x.rn=1 ORDER BY ManName;"; cmd.Parameters.AddWithValue("@DID", CurrentDeviceId); var dt = new DataTable(); using (var rdr = await cmd.ExecuteReaderAsync().ConfigureAwait(false)) dt.Load(rdr); if (dt.Columns.Contains("FirmenId")) { try { dt.PrimaryKey = new[] { dt.Columns["FirmenId"] }; } catch { } } return dt; } }

        public async Task<DataTable> GetOffeneAuszahlungenAsync(int persId)
        { await EnsureOpenAsync().ConfigureAwait(false); using (var cmd = _connection.CreateCommand()) { cmd.CommandText = "SELECT Belegnummer,CASE CAST(Typ AS int) WHEN 1 THEN 'Anfangsbestand' WHEN 2 THEN 'Einzahlung' WHEN 3 THEN 'Auszahlung' WHEN 4 THEN 'Schichtabrechnung' WHEN 5 THEN 'Personalguthaben' WHEN 6 THEN 'Trinkgeld' ELSE '' END AS Typ,Buchungstext,Betrag19,Betrag7,Betrag0,CASE WHEN COLUMNPROPERTY(OBJECT_ID('dbo.TKassenbuchZahlungen'),'BetragGesamt','ColumnId') IS NOT NULL AND BetragGesamt IS NOT NULL THEN BetragGesamt ELSE ISNULL(Betrag19,0)+ISNULL(Betrag7,0)+ISNULL(Betrag0,0) END AS BetragGesamt,PersId,ErfasstAm,FirmenID,Kost1,Kost2,Konto,DeviceID,Verbucht FROM TKassenbuchZahlungen WITH (NOLOCK) WHERE PersId=@pid AND (Verbucht=0 OR Verbucht IS NULL)"; cmd.Parameters.AddWithValue("@pid", persId); cmd.Parameters.AddWithValue("@DID", CurrentDeviceId); var dt = new DataTable(); using (var rdr = await cmd.ExecuteReaderAsync().ConfigureAwait(false)) dt.Load(rdr); return dt; } }

        public async Task<DataTable> GetMandantenAsync(bool onlyForPayments = false)
        {
            await EnsureOpenAsync().ConfigureAwait(false);
            string filterColumn = null;
            if (onlyForPayments)
            {
                try
                { var candidates = new[] { "GeldautomatAktiv","KassenClientAktiv","KassenclientAktiv","ZahlungAktiv","ZahlungenAktiv","EnableZahlung","IsActive","Aktiv" }; using (var chk = _connection.CreateCommand()) { chk.CommandText = "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.TMandanten')"; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); using (var rdr = await chk.ExecuteReaderAsync().ConfigureAwait(false)) { while (await rdr.ReadAsync().ConfigureAwait(false)) { string n = rdr[0] as string; if (!string.IsNullOrEmpty(n)) names.Add(n); } } foreach (var c in candidates) if (names.Contains(c)) { filterColumn = c; break; } } }
                catch { filterColumn = null; }
            }
            using (var cmd = _connection.CreateCommand())
            {
                if (onlyForPayments)
                { var where = "WHERE (ISNULL(Flags,0) & 512)=0"; if (!string.IsNullOrEmpty(filterColumn)) where += $" AND ISNULL([{filterColumn}],0)<>0"; cmd.CommandText = $"SELECT ManID,ManName FROM TMandanten WITH (NOLOCK) {where} ORDER BY ManName"; }
                else cmd.CommandText = "SELECT ManID,ManName FROM TMandanten WITH (NOLOCK) ORDER BY ManName";
                using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false)) { var dt = new DataTable(); dt.Load(reader); return dt; }
            }
        }

        public async Task<int> InsertKassenbuchZahlungAsync(byte typ,string buchungstext,decimal betrag19,decimal betrag7,decimal betrag0,int persId,int firmenId,int? kost1,int? kost2,int? konto,decimal betragGesamt)
        { await EnsureOpenAsync().ConfigureAwait(false); bool hasUserAnlage=false; try { using (var chk=_connection.CreateCommand()) { chk.CommandText="SELECT 1 FROM sys.columns WHERE Name='UserAnlage' AND Object_ID=OBJECT_ID('dbo.TKassenbuchZahlungen')"; var o= await chk.ExecuteScalarAsync().ConfigureAwait(false); hasUserAnlage=o!=null && o!=DBNull.Value; } } catch { hasUserAnlage=false; } using (var cmd=_connection.CreateCommand()) { cmd.CommandText = hasUserAnlage? "INSERT INTO TKassenbuchZahlungen (Typ,Buchungstext,Betrag19,Betrag7,Betrag0,PersId,FirmenID,DeviceID,ErfasstAm,Kost1,Kost2,Konto,BetragGesamt,Verbucht,UserAnlage) VALUES (@Typ,@Buchungstext,@Betrag19,@Betrag7,@Betrag0,@PersId,@FirmenID,@DeviceID,SYSDATETIME(),@Kost1,@Kost2,@Konto,@BetragGesamt,0,@UserAnlage); SELECT SCOPE_IDENTITY();" : "INSERT INTO TKassenbuchZahlungen (Typ,Buchungstext,Betrag19,Betrag7,Betrag0,PersId,FirmenID,DeviceID,ErfasstAm,Kost1,Kost2,Konto,BetragGesamt,Verbucht) VALUES (@Typ,@Buchungstext,@Betrag19,@Betrag7,@Betrag0,@PersId,@FirmenID,@DeviceID,SYSDATETIME(),@Kost1,@Kost2,@Konto,@BetragGesamt,0); SELECT SCOPE_IDENTITY();"; cmd.Parameters.AddWithValue("@Typ", typ); cmd.Parameters.AddWithValue("@Buchungstext", (object)buchungstext ?? DBNull.Value); cmd.Parameters.AddWithValue("@Betrag19", betrag19); cmd.Parameters.AddWithValue("@Betrag7", betrag7); cmd.Parameters.AddWithValue("@Betrag0", betrag0); cmd.Parameters.AddWithValue("@PersId", persId); cmd.Parameters.AddWithValue("@FirmenID", firmenId); cmd.Parameters.AddWithValue("@DeviceID", CurrentDeviceId); cmd.Parameters.AddWithValue("@Kost1", (object)kost1 ?? DBNull.Value); cmd.Parameters.AddWithValue("@Kost2", (object)kost2 ?? DBNull.Value); cmd.Parameters.AddWithValue("@Konto", (object)konto ?? DBNull.Value); cmd.Parameters.AddWithValue("@BetragGesamt", betragGesamt); if (hasUserAnlage) cmd.Parameters.AddWithValue("@UserAnlage", persId); var o= await cmd.ExecuteScalarAsync().ConfigureAwait(false); return Convert.ToInt32(Convert.ToDecimal(o)); } }

        public async Task<DataTable> GetActivePersonalAsync()
        { await EnsureOpenAsync().ConfigureAwait(false); using (var cmd=_connection.CreateCommand()) { cmd.CommandText = "SELECT PID,LTRIM(RTRIM(COALESCE(Name,''))) + CASE WHEN NULLIF(Vorname,'') IS NULL THEN '' ELSE ', ' + Vorname END AS Name,Name AS Nachname,Vorname FROM TPersonal WITH (NOLOCK) WHERE (Gesperrt=0 OR Gesperrt IS NULL) AND (AustrittAm IS NULL OR AustrittAm>SYSDATETIME()) ORDER BY Name,Vorname"; using (var reader= await cmd.ExecuteReaderAsync().ConfigureAwait(false)) { var dt=new DataTable(); dt.Load(reader); return dt; } } }

        public async Task MarkZahlungAlsVerbuchtAsync(int belegnummer)
        { await EnsureOpenAsync().ConfigureAwait(false); using (var cmd=_connection.CreateCommand()) { cmd.CommandText="UPDATE TKassenbuchZahlungen SET Verbucht=1, DeviceID=@DID WHERE Belegnummer=@bnr"; cmd.Parameters.AddWithValue("@DID", CurrentDeviceId); cmd.Parameters.AddWithValue("@bnr", belegnummer); await cmd.ExecuteNonQueryAsync().ConfigureAwait(false); } }

        public async Task<string> GetFahrercodeAsync(int pid)
        { var meta = await GetPersonalLoginMetaAsync(pid).ConfigureAwait(false); return meta.exists ? meta.fahrercode : null; }

        public async Task<int> SetFahrercodeAsync(int pid,string code)
        { await EnsureOpenAsync().ConfigureAwait(false); using (var cmd=_connection.CreateCommand()) { cmd.CommandText="UPDATE TPersonal SET Fahrercode=@Code WHERE PID=@PID"; cmd.Parameters.AddWithValue("@Code", (object)code ?? DBNull.Value); cmd.Parameters.AddWithValue("@PID", pid); int rows= await cmd.ExecuteNonQueryAsync().ConfigureAwait(false); if (_personalCache.TryGetValue(pid,out var entry)) { entry.Code=code; entry.ExpiryUtc=DateTime.UtcNow+_personalCacheTtl; _personalCache[pid]=entry; } return rows; } }


        public async Task<PersonalInfo> GetPersonalInfoAsync(int pid)
        {
            await EnsureOpenAsync().ConfigureAwait(false);
            using (var cmd = _connection.CreateCommand())
            {
                //cmd.CommandText = "SELECT PID,Name,Vorname,NFCTagUID,Fahrercode,Gesperrt,EintrittAm,AustrittAm,AppRechte,EMail FROM TPersonal WITH (NOLOCK) WHERE PID=@PID";
                cmd.CommandText = "SELECT " + PersonalInfo.DB_FIELDS +" FROM TPersonal WITH (NOLOCK) WHERE PID=@PID";
                cmd.Parameters.AddWithValue("@PID", pid);

                using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow).ConfigureAwait(false))
                {
                    if (!reader.Read())
                        return null;

                    string appRechte = reader["AppRechte"].ToString();
                    ParseAppRechteToUints(appRechte, out uint appRights1, out uint appRigths2);

                    return new PersonalInfo
                    {
                        PID = reader.GetInt32(reader.GetOrdinal("PID")),
                        Name = reader.GetString(reader.GetOrdinal("Name")),
                        Vorname = reader.GetString(reader.GetOrdinal("Vorname")),
                        NFC = reader.IsDBNull(reader.GetOrdinal("NFCTagUID")) ? null : reader["NFCTagUID"].ToString(),
                        Fahrercode = reader.IsDBNull(reader.GetOrdinal("Fahrercode")) ? null : reader["Fahrercode"].ToString(),
                        EMail = reader.IsDBNull(reader.GetOrdinal("EMail")) ? null : reader["EMail"].ToString(),
                        Gesperrt = !reader.IsDBNull(reader.GetOrdinal("Gesperrt")) && Convert.ToBoolean(reader["Gesperrt"]),
                        Eintrittsdatum = reader.IsDBNull(reader.GetOrdinal("EintrittAm")) ? (DateTime?)null : Convert.ToDateTime(reader["EintrittAm"]),
                        Austrittsdatum = reader.IsDBNull(reader.GetOrdinal("AustrittAm")) ? (DateTime?)null : Convert.ToDateTime(reader["AustrittAm"]),
                        AppRights1 = (int)appRights1,
                        AppRights2 = (int)appRigths2,
                    };
                }
            }
        }

        public async Task<PersonalInfo> GetPersonalByNfcAsync(string nfcToken)
        { 
            if (string.IsNullOrWhiteSpace(nfcToken)) 
                return null;

            await EnsureOpenAsync().ConfigureAwait(false);

            using (var cmd=_connection.CreateCommand()) 
            { 
                cmd.CommandText= "SELECT TOP 1 PID,Name,Vorname,NFCTagUID,Fahrercode,Gesperrt,EintrittAm,AustrittAm,AppRechte,EMail FROM TPersonal WITH (NOLOCK) WHERE NFCTagUID=@nfc"; 
                cmd.Parameters.AddWithValue("@nfc", nfcToken); 
                
                using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow).ConfigureAwait(false)) 
                { 
                    if (!reader.Read()) 
                        return null;

                    string appRechte = reader["AppRechte"].ToString();
                    ParseAppRechteToUints(appRechte, out uint appRights1, out uint appRigths2);

                    int pid = reader.GetInt32(reader.GetOrdinal("PID")); 
                    
                    var status = new PersonalStatus 
                    { 
                        Gesperrt   = !reader.IsDBNull(reader.GetOrdinal("Gesperrt")) && Convert.ToBoolean(reader["Gesperrt"]), 
                        EintrittAm = reader.IsDBNull(reader.GetOrdinal("EintrittAm")) ? (DateTime?)null : Convert.ToDateTime(reader["EintrittAm"]), 
                        AustrittAm = reader.IsDBNull(reader.GetOrdinal("AustrittAm")) ? (DateTime?)null : Convert.ToDateTime(reader["AustrittAm"]),
                        AppRights1 = (int)appRights1,
                        AppRights2 = (int)appRigths2,
                    }; 

                    string code = reader.IsDBNull(reader.GetOrdinal("Fahrercode")) ? null : reader["Fahrercode"].ToString();
                    _personalCache[pid] = new PersonalCacheEntry { Status=status, Code=code, ExpiryUtc=DateTime.UtcNow+_personalCacheTtl }; 
                    
                    return new PersonalInfo 
                    { 
                        PID = pid, 
                        Name = reader.GetString(reader.GetOrdinal("Name")), 
                        Vorname = reader.GetString(reader.GetOrdinal("Vorname")), 
                        NFC = reader.IsDBNull(reader.GetOrdinal("NFCTagUID")) ? null : reader["NFCTagUID"].ToString(), 
                        Fahrercode = code, 
                        EMail = reader.IsDBNull(reader.GetOrdinal("EMail")) ? null : reader["EMail"].ToString(), 
                        Gesperrt = status.Gesperrt, 
                        Eintrittsdatum = status.EintrittAm, 
                        Austrittsdatum = status.AustrittAm,
                        AppRights1 = (int)appRights1,
                        AppRights2 = (int)appRigths2,
                    };
                } 
            }
        }

        public async Task<int> SetNfcAsync(int pid,string nfc)
        { await EnsureOpenAsync().ConfigureAwait(false); using (var cmd=_connection.CreateCommand()) { cmd.CommandText="UPDATE TPersonal SET NFCTagUID=@NFC WHERE PID=@PID"; cmd.Parameters.AddWithValue("@NFC", (object)nfc ?? DBNull.Value); cmd.Parameters.AddWithValue("@PID", pid); int rows= await cmd.ExecuteNonQueryAsync().ConfigureAwait(false); if (_personalCache.TryGetValue(pid,out var entry)) { entry.ExpiryUtc=DateTime.UtcNow+_personalCacheTtl; _personalCache[pid]=entry; } return rows; } }

        private static byte MapTypStringToCode(string typ)
        { if (string.IsNullOrWhiteSpace(typ)) return 0; switch (typ.Trim().ToLowerInvariant()) { case "anfangsbestand": return 1; case "einzahlung": return 2; case "auszahlung": return 3; case "schichtabrechnung": return 4; case "personalguthaben": return 5; case "trinkgeld": case "trinkgeld auszahlung": return 6; default: return 0; } }
        private static string MapTypCodeToString(object dbVal)
        { if (dbVal==null || dbVal==DBNull.Value) return string.Empty; int v; try { v=Convert.ToInt32(dbVal);} catch { return string.Empty;} switch(v){ case 1: return "Anfangsbestand"; case 2: return "Einzahlung"; case 3: return "Auszahlung"; case 4: return "Schichtabrechnung"; case 5: return "Personalguthaben"; case 6: return "Trinkgeld"; default: return string.Empty; } }

        public void Dispose() { _connection?.Dispose(); }

        public async Task<LoginContext> GetLoginContextAsync(int persId)
        {
            if (persId <= 0) return null; 
            
            await EnsureOpenAsync().ConfigureAwait(false); 
            var ctx = new LoginContext();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @";WITH lastPg AS (SELECT TOP 1 SaldoPersonalguthaben FROM TKassenbuch WITH (NOLOCK) WHERE PersId=@PID AND Typ=5 AND DeviceID=@DID ORDER BY ErfasstAm DESC), lastShift AS (SELECT TOP 1 s.SchichtId,s.PersId,s.PersName,s.FhzId,s.ManID,s.StartZeit,ISNULL(s.EinnahmenBar1,0)-ISNULL(s.EinzahlungFahrer1,0) AS Betrag19,ISNULL(s.EinnahmenBar2,0)-ISNULL(s.EinzahlungFahrer2,0) AS Betrag7,ISNULL(s.EinnahmenBar3,0)-ISNULL(s.EinzahlungFahrer3,0) AS Betrag0,ISNULL(s.EinzahlungFahrer,0) AS EinzahlungBisher FROM TSchichten s WITH (NOLOCK) WHERE (s.Flags & 1)=0 AND (s.Flags & 4)=0 AND s.PersId=@PID ORDER BY s.StartZeit DESC) SELECT p.PID,p.Name,p.Vorname,p.NFCTagUID,p.Fahrercode,p.Gesperrt,p.EintrittAm,p.AustrittAm,p.AppRechte,p.EMail,(SELECT SaldoPersonalguthaben FROM lastPg) AS Guthaben,ls.SchichtId,ls.PersId AS ShiftPersId,ls.PersName,ls.FhzId,ls.ManID,ls.StartZeit,ls.Betrag19,ls.Betrag7,ls.Betrag0,ls.EinzahlungBisher FROM TPersonal p WITH (NOLOCK) LEFT JOIN lastShift ls ON ls.PersId=p.PID WHERE p.PID=@PID;";
                cmd.Parameters.AddWithValue("@PID", persId); 
                cmd.Parameters.AddWithValue("@DID", CurrentDeviceId);

                using (var rdr = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    if (await rdr.ReadAsync().ConfigureAwait(false))
                    {
                        string appRechte = rdr["AppRechte"].ToString();
                        ParseAppRechteToUints(appRechte, out uint appRights1, out uint appRigths2);

                        ctx.Personal = new PersonalInfo
                        {
                            PID = rdr.GetInt32(rdr.GetOrdinal("PID")),
                            Name = rdr.IsDBNull(rdr.GetOrdinal("Name")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("Name")),
                            Vorname = rdr.IsDBNull(rdr.GetOrdinal("Vorname")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("Vorname")),
                            NFC = rdr.IsDBNull(rdr.GetOrdinal("NFCTagUID")) ? null : rdr["NFCTagUID"].ToString(),
                            Fahrercode = rdr.IsDBNull(rdr.GetOrdinal("Fahrercode")) ? null : rdr["Fahrercode"].ToString(),
                            EMail = rdr.IsDBNull(rdr.GetOrdinal("EMail")) ? null : rdr["EMail"].ToString(),
                            Gesperrt = !rdr.IsDBNull(rdr.GetOrdinal("Gesperrt")) && Convert.ToBoolean(rdr["Gesperrt"]),
                            Eintrittsdatum = rdr.IsDBNull(rdr.GetOrdinal("EintrittAm")) ? (DateTime?)null : Convert.ToDateTime(rdr["EintrittAm"]),
                            Austrittsdatum = rdr.IsDBNull(rdr.GetOrdinal("AustrittAm")) ? (DateTime?)null : Convert.ToDateTime(rdr["AustrittAm"]),
                            AppRights1 = (int)appRights1,
                            AppRights2 = (int)appRigths2,
                        };

                        ctx.Guthaben = rdr.IsDBNull(rdr.GetOrdinal("Guthaben")) ? 0m : Convert.ToDecimal(rdr["Guthaben"]);
                        int ordSchichtId = rdr.GetOrdinal("SchichtId");
                        
                        if (!rdr.IsDBNull(ordSchichtId))
                        {
                            ctx.Shift = new ShiftDetails
                            {
                                SchichtId = rdr.GetInt32(ordSchichtId),
                                PersId = rdr.GetInt32(rdr.GetOrdinal("ShiftPersId")),
                                PersName = rdr.IsDBNull(rdr.GetOrdinal("PersName")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("PersName")),
                                FhzId = rdr.IsDBNull(rdr.GetOrdinal("FhzId")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("FhzId")),
                                ManId = rdr.IsDBNull(rdr.GetOrdinal("ManID")) ? 0 : Convert.ToInt32(rdr["ManID"]),
                                StartZeit = rdr.IsDBNull(rdr.GetOrdinal("StartZeit")) ? DateTime.Now : rdr.GetDateTime(rdr.GetOrdinal("StartZeit")),
                                Betrag19 = rdr.IsDBNull(rdr.GetOrdinal("Betrag19")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("Betrag19")),
                                Betrag7 = rdr.IsDBNull(rdr.GetOrdinal("Betrag7")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("Betrag7")),
                                Betrag0 = rdr.IsDBNull(rdr.GetOrdinal("Betrag0")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("Betrag0")),
                                EinzahlungBisher = rdr.IsDBNull(rdr.GetOrdinal("EinzahlungBisher")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("EinzahlungBisher"))
                            };
                        }
                    }
                }
            }
            return ctx.Personal == null ? null : ctx;
        }
    }
}