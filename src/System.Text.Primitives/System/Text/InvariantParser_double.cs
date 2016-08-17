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
        #endregion

        private static unsafe bool TryBufferToDouble(byte[] number, int byteIndex, out double value)
        {
            ulong val;
            int exp;
            int remaining;
            int length;
            int count;
            int scale;
            int absscale;
            int index;

            length = number.Length;
            remaining = length;

            // skip the leading zeros
            while (number[byteIndex] == '0')
            {
                remaining--;
                byteIndex++;
            }

            if (remaining == 0)
            {
                value = 0;
                return true;
            }

            count = Min(remaining, 9);
            remaining -= count;
            val = DigitsToInt(number, byteIndex, count);

            if (remaining > 0)
            {
                count = Min(remaining, 9);
                remaining -= count;

                // get the denormalized power of 10
                uint mult = (uint)(s_rgval64Power10[count - 1] >> (64 - s_rgexp64Power10[count - 1]));
                val = Mul32x32To64((uint)val, mult) + DigitsToInt(number, byteIndex + 9, count);
            }

            scale = number.scale - (length - remaining);
            absscale = abs(scale);
            if (absscale >= 22 * 16)
            {
                // overflow / underflow
                ulong result = (scale > 0) ? 0x7FF0000000000000 : 0ul;
                if (number.sign)
                    result |= 0x8000000000000000;
                return *(double*)&result;
            }

            exp = 64;

            // normalize the mantissa
            if ((val & 0xFFFFFFFF00000000) == 0) { val <<= 32; exp -= 32; }
            if ((val & 0xFFFF000000000000) == 0) { val <<= 16; exp -= 16; }
            if ((val & 0xFF00000000000000) == 0) { val <<= 8; exp -= 8; }
            if ((val & 0xF000000000000000) == 0) { val <<= 4; exp -= 4; }
            if ((val & 0xC000000000000000) == 0) { val <<= 2; exp -= 2; }
            if ((val & 0x8000000000000000) == 0) { val <<= 1; exp -= 1; }

            index = absscale & 15;
            if (index != 0)
            {
                int multexp = s_rgexp64Power10[index - 1];
                // the exponents are shared between the inverted and regular table
                exp += (scale < 0) ? (-multexp + 1) : multexp;

                ulong multval = s_rgval64Power10[index + ((scale < 0) ? 15 : 0) - 1];
                val = Mul64Lossy(val, multval, ref exp);
            }

            index = absscale >> 4;
            if (index != 0)
            {
                int multexp = s_rgexp64Power10By16[index - 1];
                // the exponents are shared between the inverted and regular table
                exp += (scale < 0) ? (-multexp + 1) : multexp;

                ulong multval = s_rgval64Power10By16[index + ((scale < 0) ? 21 : 0) - 1];
                val = Mul64Lossy(val, multval, ref exp);
            }


            // round & scale down
            if (((int)val & (1 << 10)) != 0)
            {
                // IEEE round to even
                ulong tmp = val + ((1 << 10) - 1) + (ulong)(((int)val >> 11) & 1);
                if (tmp < val)
                {
                    // overflow
                    tmp = (tmp >> 1) | 0x8000000000000000;
                    exp += 1;
                }
                val = tmp;
            }

            // return the exponent to a biased state
            exp += 0x3FE;

            // handle overflow, underflow, "Epsilon - 1/2 Epsilon", denormalized, and the normal case
            if (exp <= 0)
            {
                if (exp == -52 && (val >= 0x8000000000000058))
                {
                    // round X where {Epsilon > X >= 2.470328229206232730000000E-324} up to Epsilon (instead of down to zero)
                    val = 0x0000000000000001;
                }
                else if (exp <= -52)
                {
                    // underflow
                    val = 0;
                }
                else
                {
                    // denormalized
                    val >>= (-exp + 11 + 1);
                }
            }
            else if (exp >= 0x7FF)
            {
                // overflow
                val = 0x7FF0000000000000;
            }
            else
            {
                // normal postive exponent case
                val = ((ulong)exp << 52) + ((val >> 11) & 0x000FFFFFFFFFFFFF);
            }

            if (number.sign)
                val |= 0x8000000000000000;

            value =  *(double*)&val;
            return true;
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
            bytesConsumed = 0;
            return TryBufferToDouble(utf8Text, index, out value);
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
