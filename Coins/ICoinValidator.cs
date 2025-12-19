using System;

namespace Geldautomat.Coins
{
    public interface ICoinValidator : IDisposable
    {
        string ComPort { get; set; }
        int SspAddress { get; set; }
        bool Connected { get; }

        void Connect();
        void Disconnect();
        void Enable(bool enable);

        event Action<int> CoinAccepted; // Wert in Cent
        event Action<string> EventLog;

        // Optional: Münzlevels / Auszahlung (SmartCoin / RM5)
        // Implementierer ohne Unterstützung können NotImplementedException werfen.
        int[] GetCoinAvailability();
        void RequestCoinLevels();
        void PayoutCoins(int[] countsByIndex); // Index {1,2,5,10,20,50,100,200}
    }
}