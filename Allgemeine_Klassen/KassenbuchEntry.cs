namespace Geldautomat
{
    public class KassenbuchEntry
    {
        public int PersId { get; set; }
        public int SchichtId { get; set; }
        public string Typ { get; set; }            // z. B. "Einzahlung"
        public string Buchungstext { get; set; }   // z. B. "Schichtabrechnung"

        // NEU: statt Koststelle
        public int Kost1 { get; set; }
        public int Kost2 { get; set; }
        public int Konto { get; set; }

        public decimal Betrag19 { get; set; }
        public decimal Betrag7 { get; set; }
        public decimal Betrag0 { get; set; }
        public decimal SaldoPersonalguthaben { get; set; }
        public int FirmenId { get; set; }
        public decimal Kassenbestand { get; set; }
        public decimal BetragGesamt { get; set; }

        public string AutomatenName { get; set; }

        // Neu: Fahrzeug-ID (FhzId) wird in TKassenbuch gespeichert, falls vorhanden
        public int FhzId { get; set; }
    }
}