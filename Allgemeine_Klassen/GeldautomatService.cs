using System;
using TaMi_Einzahlautomat.Coins;

namespace TaMi_Einzahlautomat
{
    /// <summary>
    /// Zentraler Service für Hardware-Objekte und ggf. weitere Automat-Logik.
    /// </summary>
    public class GeldautomatService : IDisposable
    {
        public NV200_SSP Ssp { get; }
        public ICoinValidator Coin { get; }
        private readonly string _iniPath = @"C:\ProgramData\SuE-Software\SuE-TaMi Client SQL\Einzahlautomat.ini";
        private bool _disposed;

        public GeldautomatService()
        {
            Ssp = new NV200_SSP();
            Coin = CoinValidatorFactory.CreateFromIni(_iniPath);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Coin?.Dispose();
                _disposed = true;
            }
        }
    }
}
