using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuE.Tools
{
    public class MessagePacket
    {
        private const int DEFAULT_BUFFER_SIZE = 4096;

        private byte[]  mBuffer;
        private int     mWritePos;  //Aktuelle Schreibposition (put Funktionen)
        private int     mReadPos;   //Aktuelle Leseposition (get Funktionen)


        /// <summary>
        /// Erstellt ein neues leeres MessagePacket mit einer standart Puffergröße von 4096 byte
        /// </summary>
        public MessagePacket()
        {
            mWritePos = 0;
            mReadPos = 0;
            mBuffer = new byte[DEFAULT_BUFFER_SIZE];
        }

        /// <summary>
        /// Erstellt ein neues leeres MessagePacket der angegebenen Puffergröße
        /// </summary>
        public MessagePacket(int buffersize)
        {
            mWritePos = 0;
            mReadPos = 0;
            mBuffer = new byte[buffersize];
        }


        /// <summary>
        /// Erstellt ein neues MessagePacket aus den angegebenen byte array
        /// Die Escapezeichen werden dabei entfernt
        /// </summary>
        public MessagePacket(byte[] packet, int nOffset, int nLength)
        {
            if (packet == null) return;

            mBuffer = packet;
            mWritePos = nLength;
            mReadPos = 0;

            //STX, ETX und ESC wurden schon von MessageClient entfernt

            /*
            mBuffer = new byte[nLength];
             
            byte    b;
            int     n = 0;
            
            for (int i = 0; i < nLength; i++)
            {
                b = packet[nOffset + i];

                if ((b == MessageClient.STX) || (b == MessageClient.ETX) || (b == MessageClient.ESC))
                { 
                    i++;
                    b = packet[nOffset + i];
                }

                if (i >= nLength) break;
                mBuffer[n++] = b;
            }
     
            mWritePos = n;
            mReadPos = 0;
            */
        }



        public int WritePos
        {
            get { return mWritePos; }
        }

        public int ReadPos
        {
            get { return mReadPos; }
        }

        /// <summary>
        /// Gibt die Anzahl der noch verbleibenden zu lesenden Bytes zurück (WritePos - ReadPos)
        /// </summary>
        public int RemainingBytesToRead
        {
            get { return mWritePos - mReadPos; }
        }



#region "put Funktionen"

        //put Funktionen
        public void putByte(byte b) { cb(1); mBuffer[mWritePos] = b; mWritePos += 1; }
        public void putByte(short s) { cb(1); mBuffer[mWritePos] = (byte)(s & 0xFF); mWritePos += 1; }
        public void putByte(int i) { cb(1); mBuffer[mWritePos] = (byte)(i & 0xFF); mWritePos += 1; }

        public void putShort(short s) { cb(2); mBuffer[mWritePos] = (byte)(s & 0x00FF); mBuffer[mWritePos + 1] = (byte)((s & 0xFF00) >> 8); mWritePos += 2; }
        public void putShort(int i) { cb(2); mBuffer[mWritePos] = (byte)(i & 0x00FF); mBuffer[mWritePos + 1] = (byte)((i & 0xFF00) >> 8); mWritePos += 2; }

        public void putInt(int i) { cb(4); mBuffer[mWritePos] = (byte)(i & 0x000000FF); mBuffer[mWritePos + 1] = (byte)((i & 0x0000FF00) >> 8); mBuffer[mWritePos + 2] = (byte)((i & 0x00FF0000) >> 16); mBuffer[mWritePos + 3] = (byte)((i & 0xFF000000) >> 24); mWritePos += 4; }

        public void putFloat(float f) { byte[] ba = BitConverter.GetBytes(f); this.putBytes(ba); }

        public void putLong(ulong l) { cb(8); mBuffer[mWritePos] = (byte)(l & 0x00000000000000FFL); mBuffer[mWritePos + 1] = (byte)((l & 0x000000000000FF00L) >> 8); mBuffer[mWritePos + 2] = (byte)((l & 0x0000000000FF0000L) >> 16); mBuffer[mWritePos + 3] = (byte)((l & 0x00000000FF000000L) >> 24); mBuffer[mWritePos + 4] = (byte)((l & 0x000000FF00000000L) >> 32); mBuffer[mWritePos + 5] = (byte)((l & 0x0000FF0000000000L) >> 40); mBuffer[mWritePos + 6] = (byte)((l & 0x00FF000000000000L) >> 48); mBuffer[mWritePos + 7] = (byte)((l & 0xFF00000000000000L) >> 56); mWritePos += 8; }

        public void putDouble(double d) { byte[] ba = BitConverter.GetBytes(d); this.putBytes(ba); }

        public void putString(string s) { byte[] txt = System.Text.Encoding.Default.GetBytes(s); this.putBytes(txt); }

        public void putBytes(byte[] ba) { cb(ba.GetLength(0)); ba.CopyTo(mBuffer, mWritePos); mWritePos += ba.GetLength(0); }

#endregion

#region "get Funktionen"

        //get Funktionen
        public byte getByteB() { byte r = mBuffer[mReadPos]; mReadPos += 1; return r; }
        public short getByteS() { short r = (short)(mBuffer[mReadPos] & 0x00FF); mReadPos += 1; return r; }
        public int getByteI() { int r = (int)(mBuffer[mReadPos] & 0x000000FF); mReadPos += 1; return r; }

        public short getShortS() { short r = (short)((mBuffer[mReadPos + 1] & 0xFF) << 8 | (mBuffer[mReadPos] & 0xFF)); mReadPos += 2; return r; }
        public int getShortI() { int r = (mBuffer[mReadPos + 1] & 0xFF) << 8 | (mBuffer[mReadPos] & 0xFF); mReadPos += 2; return r; }

        public int getIntI() { int r = (0xFF & mBuffer[mReadPos + 3]) << 24 | (0xFF & mBuffer[mReadPos + 2]) << 16 | (0xFF & mBuffer[mReadPos + 1]) << 8 | (0xFF & mBuffer[mReadPos]); mReadPos += 4; return r; }
        public float getFloat() { float f = BitConverter.ToSingle(mBuffer, mReadPos); mReadPos += 4; return f; }

        public long getLong() { long r = (0xFF & mBuffer[mReadPos + 7]) << 56 | (0xFF & mBuffer[mReadPos + 6]) << 48 | (0xFF & mBuffer[mReadPos + 5]) << 40 | (0xFF & mBuffer[mReadPos + 4]) << 32 | (0xFF & mBuffer[mReadPos + 3]) << 24 | (0xFF & mBuffer[mReadPos + 2]) << 16 | (0xFF & mBuffer[mReadPos + 1]) << 8 | (0xFF & mBuffer[mReadPos]); mReadPos += 8; return r; }
        public double getDouble() { double d = BitConverter.ToDouble(mBuffer, mReadPos); mReadPos += 8; return d; }

        public string getString(int len) { string s = System.Text.Encoding.Default.GetString(mBuffer, mReadPos, len); mReadPos += len; return s; }

        public string getStringUTF8(int len) { string s = System.Text.Encoding.UTF8.GetString(mBuffer, mReadPos, len); mReadPos += len; return s; }

#endregion

        /// <summary> 
        /// Gibt das Paket als byte array inklusive STX-, ETX- und ESC-Zeichen zurück
        /// </summary>
        public byte[] getEscaped()
        {
            byte[] packetesc = new byte[(mWritePos * 2) + 2];
            int npacketescsize = 0;

            //STX-Zeichen
            packetesc[npacketescsize] = MessageClient.STX;
            npacketescsize++;

            //ESC-Zeichen einfügen wenn nötig
            for (int i = 0; i < mWritePos; i++)
            {
                if ((mBuffer[i] == MessageClient.STX) || (mBuffer[i] == MessageClient.ETX) || (mBuffer[i] == MessageClient.ESC))
                {
                    packetesc[npacketescsize] = MessageClient.ESC;
                    npacketescsize++;
                    packetesc[npacketescsize] = mBuffer[i];
                }
                else
                {
                    packetesc[npacketescsize] = mBuffer[i];
                }

                npacketescsize++;
            }

            //ETX-Zeichen
            packetesc[npacketescsize] = MessageClient.ETX;
            npacketescsize++;

            //Result
            byte[] ba = new byte[npacketescsize];
            Buffer.BlockCopy(packetesc, 0, ba, 0, npacketescsize);

            return ba;
        }


        /*
        /// <summary> 
        /// Gibt true zurück wenn byte b ein Escapechar ist.
        /// </summary>
        /// 
        private bool IsEscapeChar(byte b)
        {
            return ((b == ESC) || (b == STX) || (b == ETX));
        }
        */

        /// <summary>
        /// Prüfe ob der Puffer überlaufen würde wenn nAdditionalBytes angefügt würden
        /// und vergrößert diesen gegebenfalls.
        /// </summary>
        private void cb(int nAdditionalBytes)
        {
            if (mWritePos + nAdditionalBytes > mBuffer.GetLength(0))
            {
                // Debug.WriteLine("FleetPacket must increase buffer to " + (mBuffer.GetLength(0) + BUFFER_INITIAL_SIZE));
                byte[] ba = new byte[mBuffer.GetLength(0) + (nAdditionalBytes > DEFAULT_BUFFER_SIZE ? nAdditionalBytes : DEFAULT_BUFFER_SIZE) ];
                mBuffer.CopyTo(ba, 0);
                mBuffer = ba;
            }
        }


        /// <summary>
        /// Gibt einen HEX-Ascii String des Dateninhalts zurück.
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(mWritePos * 3);
            for (int i = 0; i < mWritePos; i++) { sb.AppendFormat("{0:X2} ", mBuffer[i]); }
            return sb.ToString();
        }


    }
}
