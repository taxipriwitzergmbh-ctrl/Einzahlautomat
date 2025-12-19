using System.Data;

namespace Geldautomat
{
    public class AuswahlItem
    {
        public string AnzeigeText { get; set; }
        public ShiftDetails Schicht { get; set; }
        public DataRow Auszahlung { get; set; }
    }
}