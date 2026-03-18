using System;

namespace TaMi_Einzahlautomat
{
    public sealed class NotizenDbDto
    {
        public int NotizId;
        public string Text;
        public int RelId;
        public int Flags;
        public DateTime? GueltigBis;
    }
}
