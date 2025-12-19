using System;

namespace Geldautomat
{
    // Globales Flag, um Ein-/Auszahlungen zu blockieren, solange die Admin-Übersicht geöffnet ist
    internal static class AdminMode
    {
        private static bool _isOpen;
        public static bool IsOpen
        {
            get { return _isOpen; }
        }

        public static void Enter()
        {
            _isOpen = true;
        }

        public static void Exit()
        {
            _isOpen = false;
        }
    }
}
