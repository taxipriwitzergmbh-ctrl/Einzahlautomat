using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace TaMi_Einzahlautomat
{
    public enum MwStType
    {
        Mwst0 = 0,
        Mwst7 = 7,
        Mwst19 = 19
    }

    public class PaymentFieldSetting
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Bezeichnung { get; set; }

        public MwStType MwSt { get; set; } = MwStType.Mwst19;

        public string Kost1 { get; set; }

        public string Kost2 { get; set; }

        public string Konto { get; set; }

        public string Buchungstext { get; set; }

        public byte Typ { get; set; } = 2; // Standard: Einzahlung

        public decimal MaxBetrag { get; set; } = 1000m;
    }

    public class PaymentSettingsData
    {
        public bool ZahlungenErstellenAktiv { get; set; }

        public List<PaymentFieldSetting> Felder { get; set; } = new List<PaymentFieldSetting>();
    }

    public static class PaymentSettingsStore
    {
        private static readonly string BaseDir = @"C:\\ProgramData\\SuE-Software\\SuE-TaMi Client SQL";
        private static readonly string FilePath = Path.Combine(BaseDir, "PaymentSettings.xml");
        private static PaymentSettingsData _cache;

        public static PaymentSettingsData Load()
        {
            try
            {
                if (_cache != null) return _cache;
                if (!File.Exists(FilePath))
                {
                    _cache = new PaymentSettingsData();
                    return _cache;
                }
                using (var fs = File.OpenRead(FilePath))
                {
                    var ser = new XmlSerializer(typeof(PaymentSettingsData));
                    _cache = (PaymentSettingsData)ser.Deserialize(fs);
                    if (_cache == null) _cache = new PaymentSettingsData();
                    return _cache;
                }
            }
            catch
            {
                _cache = new PaymentSettingsData();
                return _cache;
            }
        }

        public static void Save(PaymentSettingsData data)
        {
            if (data == null) data = new PaymentSettingsData();
            _cache = data;
            try
            {
                if (!Directory.Exists(BaseDir)) Directory.CreateDirectory(BaseDir);
                using (var fs = File.Create(FilePath))
                {
                    var ser = new XmlSerializer(typeof(PaymentSettingsData));
                    ser.Serialize(fs, data);
                }
            }
            catch { }
        }

        public static BindingList<PaymentFieldSetting> GetBindingList()
        {
            var data = Load();
            return new BindingList<PaymentFieldSetting>(data.Felder);
        }

        public static void UpdateFromBindingList(BindingList<PaymentFieldSetting> list)
        {
            var data = Load();
            data.Felder = list?.ToList() ?? new List<PaymentFieldSetting>();
            Save(data);
        }

        public static bool IsEnabled()
        {
            return Load().ZahlungenErstellenAktiv;
        }

        public static void SetEnabled(bool enabled)
        {
            var data = Load();
            data.ZahlungenErstellenAktiv = enabled;
            Save(data);
        }
    }
}
