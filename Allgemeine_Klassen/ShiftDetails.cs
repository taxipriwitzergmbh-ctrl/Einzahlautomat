using System;

namespace Geldautomat
{
    public class ShiftDetails
    {
        public int SchichtId { get; set; }
        public int PersId { get; set; }
        public string PersName { get; set; }
        public int FhzId { get; set; }
        public int ManId { get; set; }
        public DateTime StartZeit { get; set; }

        public decimal Betrag19 { get; set; }
        public decimal Betrag7 { get; set; }
        public decimal Betrag0 { get; set; }

        public decimal EinzahlungBisher { get; set; }

        public decimal SummeZuZahlen => Betrag19 + Betrag7 + Betrag0;
    }
}