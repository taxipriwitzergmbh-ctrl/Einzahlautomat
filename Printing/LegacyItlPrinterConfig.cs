using System;
using System.IO.Ports;

namespace TaMi_Einzahlautomat.Printing
{
    [Serializable]
    public class LegacyItlPrinterConfig
    {
        public bool Enabled;
        public string ComPort;
        public int BaudRate = 9600;
        public int DataBits = 8;
        public Parity Parity = Parity.None;
        public StopBits StopBits = StopBits.One;
        public int CharsPerLine = 40;
        public bool CutAfterPrint = true;

        public LegacyItlPrinterConfig Clone()
        {
            return (LegacyItlPrinterConfig)MemberwiseClone();
        }
    }
}
