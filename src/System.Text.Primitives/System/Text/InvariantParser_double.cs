// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Text.Utf8;

namespace System.Text
{
    public static partial class InvariantParser
    {
        // https://github.com/dotnet/corert/blob/4376c55dd018d8b7b9fed82449728711231ba266/src/System.Private.CoreLib/src/System/Globalization/FormatProvider.FormatAndParse.cs
        #region static arrays
        private static readonly ulong[] s_rgval64Power10 =
            {
                // powers of 10
                /*1*/ 0xa000000000000000,
                /*2*/ 0xc800000000000000,
                /*3*/ 0xfa00000000000000,
                /*4*/ 0x9c40000000000000,
                /*5*/ 0xc350000000000000,
                /*6*/ 0xf424000000000000,
                /*7*/ 0x9896800000000000,
                /*8*/ 0xbebc200000000000,
                /*9*/ 0xee6b280000000000,
                /*10*/ 0x9502f90000000000,
                /*11*/ 0xba43b74000000000,
                /*12*/ 0xe8d4a51000000000,
                /*13*/ 0x9184e72a00000000,
                /*14*/ 0xb5e620f480000000,
                /*15*/ 0xe35fa931a0000000,

                // powers of 0.1
                /*1*/ 0xcccccccccccccccd,
                /*2*/ 0xa3d70a3d70a3d70b,
                /*3*/ 0x83126e978d4fdf3c,
                /*4*/ 0xd1b71758e219652e,
                /*5*/ 0xa7c5ac471b478425,
                /*6*/ 0x8637bd05af6c69b7,
                /*7*/ 0xd6bf94d5e57a42be,
                /*8*/ 0xabcc77118461ceff,
                /*9*/ 0x89705f4136b4a599,
                /*10*/ 0xdbe6fecebdedd5c2,
                /*11*/ 0xafebff0bcb24ab02,
                /*12*/ 0x8cbccc096f5088cf,
                /*13*/ 0xe12e13424bb40e18,
                /*14*/ 0xb424dc35095cd813,
                /*15*/ 0x901d7cf73ab0acdc,
            };
        private static readonly sbyte[] s_rgexp64Power10 =
           {
                // exponents for both powers of 10 and 0.1
                /*1*/ 4,
                /*2*/ 7,
                /*3*/ 10,
                /*4*/ 14,
                /*5*/ 17,
                /*6*/ 20,
                /*7*/ 24,
                /*8*/ 27,
                /*9*/ 30,
                /*10*/ 34,
                /*11*/ 37,
                /*12*/ 40,
                /*13*/ 44,
                /*14*/ 47,
                /*15*/ 50,
            };
        private static readonly short[] s_rgexp64Power10By16 =
             {
                // exponents for both powers of 10^16 and 0.1^16
                /*1*/ 54,
                /*2*/ 107,
                /*3*/ 160,
                /*4*/ 213,
                /*5*/ 266,
                /*6*/ 319,
                /*7*/ 373,
                /*8*/ 426,
                /*9*/ 479,
                /*10*/ 532,
                /*11*/ 585,
                /*12*/ 638,
                /*13*/ 691,
                /*14*/ 745,
                /*15*/ 798,
                /*16*/ 851,
                /*17*/ 904,
                /*18*/ 957,
                /*19*/ 1010,
                /*20*/ 1064,
                /*21*/ 1117,
            };
        private static readonly ulong[] s_rgval64Power10By16 =
            {
                // powers of 10^16
                /*1*/ 0x8e1bc9bf04000000,
                /*2*/ 0x9dc5ada82b70b59e,
                /*3*/ 0xaf298d050e4395d6,
                /*4*/ 0xc2781f49ffcfa6d4,
                /*5*/ 0xd7e77a8f87daf7fa,
                /*6*/ 0xefb3ab16c59b14a0,
                /*7*/ 0x850fadc09923329c,
                /*8*/ 0x93ba47c980e98cde,
                /*9*/ 0xa402b9c5a8d3a6e6,
                /*10*/ 0xb616a12b7fe617a8,
                /*11*/ 0xca28a291859bbf90,
                /*12*/ 0xe070f78d39275566,
                /*13*/ 0xf92e0c3537826140,
                /*14*/ 0x8a5296ffe33cc92c,
                /*15*/ 0x9991a6f3d6bf1762,
                /*16*/ 0xaa7eebfb9df9de8a,
                /*17*/ 0xbd49d14aa79dbc7e,
                /*18*/ 0xd226fc195c6a2f88,
                /*19*/ 0xe950df20247c83f8,
                /*20*/ 0x81842f29f2cce373,
                /*21*/ 0x8fcac257558ee4e2,

                // powers of 0.1^16
                /*1*/ 0xe69594bec44de160,
                /*2*/ 0xcfb11ead453994c3,
                /*3*/ 0xbb127c53b17ec165,
                /*4*/ 0xa87fea27a539e9b3,
                /*5*/ 0x97c560ba6b0919b5,
                /*6*/ 0x88b402f7fd7553ab,
                /*7*/ 0xf64335bcf065d3a0,
                /*8*/ 0xddd0467c64bce4c4,
                /*9*/ 0xc7caba6e7c5382ed,
                /*10*/ 0xb3f4e093db73a0b7,
                /*11*/ 0xa21727db38cb0053,
                /*12*/ 0x91ff83775423cc29,
                /*13*/ 0x8380dea93da4bc82,
                /*14*/ 0xece53cec4a314f00,
                /*15*/ 0xd5605fcdcf32e217,
                /*16*/ 0xc0314325637a1978,
                /*17*/ 0xad1c8eab5ee43ba2,
                /*18*/ 0x9becce62836ac5b0,
                /*19*/ 0x8c71dcd9ba0b495c,
                /*20*/ 0xfd00b89747823938,
                /*21*/ 0xe3e27a444d8d991a,
            };
        #endregion

        private static int BufferToDouble(byte[] buffer, int index, out double value)
        {
            int scale = 0;
            bool negative = false;
            bool eNeg = false;
            bool nonZero = false;
            int maxParseDigits = 32;
            int bytesConsumed = 0;

            int digStart = 0;
            int digEnd = 0;
            bool decimalPlace = false;

            if (buffer[index] == '-')
            {
                negative = true;
                bytesConsumed++;
                index++;
            }
            else if (buffer[index] == '+')
            {
                bytesConsumed++;
                index++;
            }

            byte nextByte = buffer[index];
            byte nextByteVal = (byte)(nextByte - '0');

            if (nextByteVal > 9)
            {
                value = 0;
                return 0;
            }

            while (nextByteVal == 0) // Exhaust any initial zeroes
            {
                nextByte = buffer[++index];
                nextByteVal = (byte)(nextByte - '0');
                bytesConsumed++;
            }

            digStart = index;
            while (nextByteVal <= 9 || nextByte == '.')
            {
                if (nextByte == '.')
                {
                    decimalPlace = true;
                }
                else if (!decimalPlace)
                {
                    scale++;
                }
                nextByte = buffer[++index];
                nextByteVal = (byte)(nextByte - '0');
                bytesConsumed++;
            }
            digEnd = index - 1;

            if (nextByte == 'e' || nextByte == 'E')
            {
                nextByte = buffer[++index];
                bytesConsumed++;

                if (nextByte == '-')
                {

                }
            }
        }
        private static uint DigitsToInt(byte[] p, int index, int count)
        {
            int end = index + count;
            uint res = (uint)p[index] - '0';
            for (index = index + 1; index < end; index++)
                res = 10 * res + (uint)(p[index]) - '0';
            return res;
        }
        private static int Min(int first, int second)
        {
            if (first < second)
                return first;
            else
                return second;
        }
        private static ulong Mul32x32To64(uint a, uint b)
        {
            return (ulong)a * (ulong)b;
        }
        private static ulong Mul64Lossy(ulong a, ulong b, ref int pexp)
        {
            // it's ok to lose some precision here - Mul64 will be called
            // at most twice during the conversion, so the error won't propagate
            // to any of the 53 significant bits of the result
            ulong val = Mul32x32To64((uint)(a >> 32), (uint)(b >> 32)) +
                (Mul32x32To64((uint)(a >> 32), (uint)(b)) >> 32) +
                (Mul32x32To64((uint)(a), (uint)(b >> 32)) >> 32);

            // normalize
            if ((val & 0x8000000000000000) == 0)
            {
                val <<= 1;
                pexp -= 1;
            }

            return val;
        }
        private static int abs(int a)
        {
            return a > -a ? a : -a;
        }

        public static bool TryParse(byte[] utf8Text, int index, out double value, out int bytesConsumed)
        {
            // Precondition replacement
            if (utf8Text.Length < 1 || index < 0 || index >= utf8Text.Length)
            {
                value = 0;
                bytesConsumed = 0;
                return false;
            }

            value = 0.0;
            bytesConsumed = BufferToDouble(utf8Text, index, out value);
            return (bytesConsumed > 0);

        }

        public unsafe static bool TryParse(byte* utf8Text, int index, int length, out double value, out int bytesConsumed)
        {
            // Precondition replacement
            if (length < 1 || index < 0)
            {
                value = 0;
                bytesConsumed = 0;
                return false;
            }

            value = 0.0;
            bytesConsumed = 0;
            string doubleString = "";
            bool decimalPlace = false, e = false, signed = false, digitLast = false, eLast = false;

            if ((length) >= 3 && utf8Text[index] == 'N' && utf8Text[index + 1] == 'a' && utf8Text[index + 2] == 'N')
            {
                value = double.NaN;
                bytesConsumed = 3;
                return true;
            }
            if (utf8Text[index] == '-' || utf8Text[index] == '+')
            {
                signed = true;
                doubleString += (char)utf8Text[index];
                index++;
                bytesConsumed++;
            }
            if ((length - index) >= 8 && utf8Text[index] == 'I' && utf8Text[index + 1] == 'n' &&
                utf8Text[index + 2] == 'f' && utf8Text[index + 3] == 'i' && utf8Text[index + 4] == 'n' &&
                utf8Text[index + 5] == 'i' && utf8Text[index + 6] == 't' && utf8Text[index + 7] == 'y')
            {
                if (signed && utf8Text[index - 1] == '-')
                {
                    value = double.NegativeInfinity;
                }
                else
                {
                    value = double.PositiveInfinity;
                }
                bytesConsumed += 8;
                return true;
            }

            for (int byteIndex = index; byteIndex < length; byteIndex++)
            {
                byte nextByte = utf8Text[byteIndex];
                byte nextByteVal = (byte)(nextByte - '0');

                if (nextByteVal > 9)
                {
                    if (!decimalPlace && nextByte == '.')
                    {
                        if (digitLast)
                        {
                            digitLast = false;
                        }
                        if (eLast)
                        {
                            eLast = false;
                        }
                        bytesConsumed++;
                        decimalPlace = true;
                        doubleString += (char)nextByte;
                    }
                    else if (!e && nextByte == 'e' || nextByte == 'E')
                    {
                        e = true;
                        eLast = true;
                        bytesConsumed++;
                        doubleString += (char)nextByte;
                    }
                    else if (eLast && nextByte == '+' || nextByte == '-')
                    {
                        eLast = false;
                        bytesConsumed++;
                        doubleString += (char)nextByte;
                    }
                    else if ((decimalPlace && signed && bytesConsumed == 2) || ((signed || decimalPlace) && bytesConsumed == 1))
                    {
                        value = 0;
                        bytesConsumed = 0;
                        return false;
                    }
                    else
                    {
                        if (double.TryParse(doubleString, out value))
                        {
                            return true;
                        }
                        else
                        {
                            bytesConsumed = 0;
                            return false;
                        }
                    }
                }
                else
                {
                    if (eLast)
                        eLast = false;
                    if (!digitLast)
                        digitLast = true;
                    bytesConsumed++;
                    doubleString += (char)nextByte;
                }
            }

            if ((decimalPlace && signed && bytesConsumed == 2) || ((signed || decimalPlace) && bytesConsumed == 1))
            {
                value = 0;
                bytesConsumed = 0;
                return false;
            }
            else
            {
                if (double.TryParse(doubleString, out value))
                {
                    return true;
                }
                else
                {
                    bytesConsumed = 0;
                    return false;
                }
            }
        }
    }
}
