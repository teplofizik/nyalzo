using System;

//
// teplofizik 2026
// No unsafe code
//

/*
 * LZO1X-1 compressor (port of linux/lib/lzo/lzo1x_compress.c, v4.8)
 * https://elixir.bootlin.com/linux/v4.8/source/lib/lzo/lzo1x_compress.c
 *
 * Based on the LZO real-time data compression library by
 * Markus Franz Xaver Johannes Oberhumer.
 */

namespace NyaLZO
{
    /// <summary>
    /// LZO1X-1 compressor. Produces a raw LZO1X stream compatible with
    /// <see cref="LZO1xDecompressor"/>.
    /// </summary>
    public static class LZO1xCompressor
    {
        private const int D_BITS = 14;
        private const uint D_MASK = (1u << D_BITS) - 1;
        private const int D_SIZE = 1 << D_BITS;

        private const uint M2_MAX_OFFSET = 0x0800;
        private const uint M3_MAX_OFFSET = 0x4000;
        private const uint M4_MAX_OFFSET = 0xbfff;

        private const uint M2_MAX_LEN = 8;
        private const uint M3_MAX_LEN = 33;
        private const uint M4_MAX_LEN = 9;

        private const byte M3_MARKER = 32;
        private const byte M4_MARKER = 16;

        public static byte[] Compress(byte[] Src)
        {
            if (Src == null)
                throw new ArgumentNullException(nameof(Src));

            if (Src.Length == 0)
                return new byte[] { M4_MARKER | 1, 0, 0 };

            return new CompressState(Src).Compress();
        }

        private sealed class CompressState
        {
            private readonly BytePointer InBase;
            private readonly ushort[] Dict = new ushort[D_SIZE];
            private readonly long InLen;

            private BytePointer Op;

            public CompressState(byte[] Src)
            {
                InBase = new BytePointer(Src);
                InLen = Src.Length;
                Op = new BytePointer();
            }

            public byte[] Compress()
            {
                long ip = 0;
                long l = InLen;
                long t = 0;

                while (l > 20)
                {
                    long ll = (l <= (long)M4_MAX_OFFSET + 1) ? l : ((long)M4_MAX_OFFSET + 1);

                    Array.Clear(Dict, 0, Dict.Length);

                    t = DoCompress(ip, ll, t);
                    ip += ll;
                    l -= ll;
                }

                t += l;

                if (t > 0)
                {
                    long ii = InLen - t;

                    if ((Op.CurrentOffset == 0) && (t <= 238))
                        Emit((byte)(17 + t));
                    else if (t <= 3)
                        Op[-2] = (byte)(Op[-2] | t);
                    else if (t <= 18)
                        Emit((byte)(t - 3));
                    else
                    {
                        long tt = t - 18;
                        Emit(0);
                        while (tt > 255) { tt -= 255; Emit(0); }
                        Emit((byte)tt);
                    }

                    if (t >= 16)
                    {
                        var src = InBase + ii;
                        do
                        {
                            CopyN(Op, src, 8);
                            CopyN(Op + 8, src + 8, 8);
                            Op.Advance(16);
                            src.Advance(16);
                            t -= 16;
                        } while (t >= 16);
                        ii = InLen - t;
                    }

                    if (t > 0)
                    {
                        var src = InBase + ii;
                        do { Emit(src[0]); src.Advance(1); } while (--t > 0);
                    }
                }

                Emit((byte)(M4_MARKER | 1));
                Emit(0);
                Emit(0);

                return Op.Result;
            }

            private long DoCompress(long inOff, long inLen, long ti)
            {
                var chunk = InBase + inOff;
                var inEnd = chunk + inLen;
                var ipEnd = chunk + inLen - 20;

                var ip = chunk + (ti < 4 ? 4 - ti : 0);
                var ii = chunk.Copy();

                var finished = false;
                while (!finished)
                {
                    // literal:
                    ip.Advance(1 + ((ip - ii) >> 5));

                    // next:
                    while (true)
                    {
                        if (!(ip < ipEnd)) { finished = true; break; }

                        uint dv = Le32(ip);
                        int hash = (int)(((dv * 0x1824429d) >> (32 - D_BITS)) & D_MASK);
                        var mPos = chunk + Dict[hash];
                        Dict[hash] = (ushort)(ip - chunk);

                        if (dv != Le32(mPos))
                            break; // -> literal

                        ii = ii - ti;
                        ti = 0;
                        long litLen = ip - ii;
                        if (litLen != 0)
                        {
                            if (litLen <= 3)
                            {
                                Op[-2] = (byte)(Op[-2] | litLen);
                                CopyN(Op, ii, 4);
                                Op.Advance(litLen);
                            }
                            else if (litLen <= 16)
                            {
                                Emit((byte)(litLen - 3));
                                CopyN(Op, ii, 8);
                                CopyN(Op + 8, ii + 8, 8);
                                Op.Advance(litLen);
                            }
                            else
                            {
                                if (litLen <= 18)
                                    Emit((byte)(litLen - 3));
                                else
                                {
                                    long tt = litLen - 18;
                                    Emit(0);
                                    while (tt > 255) { tt -= 255; Emit(0); }
                                    Emit((byte)tt);
                                }

                                do
                                {
                                    CopyN(Op, ii, 8);
                                    CopyN(Op + 8, ii + 8, 8);
                                    Op.Advance(16);
                                    ii.Advance(16);
                                    litLen -= 16;
                                } while (litLen >= 16);

                                if (litLen > 0)
                                    do { Emit(ii[0]); ii.Advance(1); } while (--litLen > 0);
                            }
                        }

                        long mLen = 4;
                        if (ip[(int)mLen] == mPos[(int)mLen])
                        {
                            do
                            {
                                mLen++;
                                if (!(ip + mLen < ipEnd)) break;
                                if (ip[(int)mLen] != mPos[(int)mLen]) break;
                            } while (true);
                        }

                        long mOff = ip - mPos;
                        ip.Advance(mLen);
                        ii = ip.Copy();

                        if ((mLen <= M2_MAX_LEN) && (mOff <= M2_MAX_OFFSET))
                        {
                            mOff -= 1;
                            Emit((byte)(((mLen - 1) << 5) | ((mOff & 7) << 2)));
                            Emit((byte)(mOff >> 3));
                        }
                        else if (mOff <= M3_MAX_OFFSET)
                        {
                            mOff -= 1;
                            if (mLen <= M3_MAX_LEN)
                                Emit((byte)(M3_MARKER | (mLen - 2)));
                            else
                            {
                                mLen -= M3_MAX_LEN;
                                Emit((byte)(M3_MARKER | 0));
                                while (mLen > 255) { mLen -= 255; Emit(0); }
                                Emit((byte)mLen);
                            }
                            Emit((byte)(mOff << 2));
                            Emit((byte)(mOff >> 6));
                        }
                        else
                        {
                            mOff -= 0x4000;
                            if (mLen <= M4_MAX_LEN)
                                Emit((byte)(M4_MARKER | ((mOff >> 11) & 8) | (mLen - 2)));
                            else
                            {
                                mLen -= M4_MAX_LEN;
                                Emit((byte)(M4_MARKER | ((mOff >> 11) & 8)));
                                while (mLen > 255) { mLen -= 255; Emit(0); }
                                Emit((byte)mLen);
                            }
                            Emit((byte)(mOff << 2));
                            Emit((byte)(mOff >> 6));
                        }
                        // -> next (continue inner loop)
                    }
                }

                return inEnd - (ii - ti);
            }

            private void Emit(byte Value)
            {
                Op.Value = Value;
                Op.Advance(1);
            }

            private static void CopyN(BytePointer Dst, BytePointer Src, int Count)
            {
                for (int i = 0; i < Count; i++)
                    Dst[i] = Src[i];
            }

            private static uint Le32(BytePointer P)
            {
                return (uint)(P[0] | (P[1] << 8) | (P[2] << 16) | (P[3] << 24));
            }
        }
    }
}

