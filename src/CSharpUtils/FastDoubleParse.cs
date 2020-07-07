namespace FastDoubleParse
{
    using Aardvark.Base;
    using System;
    using System.Linq;
    using System.Collections.Generic;
    

    public static class FastDoubleParse
    {
        public static bool IgnoreChar(char c)
        {
            return c < 33;
        }
        public static bool TryParseDoubleFastStream(char[] s, int begin, int end, out double result)
        {
            result = 0;
            char c = s[begin];
            int sign = 0;
            int start = begin;

            if (c == '-')
            {
                sign = -1;
                start = begin + 1;
            }
            else if (c > 57 || c < 48)
            {
                if (IgnoreChar(c))
                {
                    do
                    {
                        ++start;
                    }
                    while (start < end && IgnoreChar(c = s[start]));

                    if (start >= end)
                    {
                        return false;
                    }

                    if (c == '-')
                    {
                        sign = -1;
                        ++start;
                    }
                    else
                    {
                        sign = 1;
                    }
                }
                else
                {
                    result = 0;
                    return false;
                }
            }
            else
            {
                start = begin + 1;
                result = 10 * result + (c - 48);
                sign = 1;
            }

            int i = start;

            for (; i < end; ++i)
            {
                c = s[i];
                if (c > 57 || c < 48)
                {
                    if (c == '.')
                    {
                        ++i;
                        goto DecimalPoint;
                    }
                    else
                    {
                        result = 0;
                        return false;
                    }
                }

                result = 10 * result + (c - 48);
            }

            result *= sign;
            return true;

            DecimalPoint:

            long temp = 0;
            int length = i;
            double exponent = 0;

            for (; i < end; ++i)
            {
                c = s[i];
                if (c > 57 || c < 48)
                {
                    if (!IgnoreChar(c))
                    {
                        if (c == 'e' || c == 'E')
                        {
                            length = i - length;
                            goto ProcessExponent;
                        }

                        result = 0;
                        return false;
                    }
                    else
                    {
                        length = i - length;
                        goto ProcessFraction;
                    }
                }
                temp = 10 * temp + (c - 48);
            }
            length = i - length;

            ProcessFraction:

            double fraction = (double)temp;

            if (length < _powLookup.Length)
            {
                fraction = fraction / _powLookup[length];
            }
            else
            {
                fraction = fraction / _powLookup[_powLookup.Length - 1];
            }

            result += fraction;

            result *= sign;

            if (exponent > 0)
            {
                result *= exponent;
            }
            else if (exponent < 0)
            {
                result /= -exponent;
            }

            return true;

            ProcessExponent:

            int expSign = 1;
            int exp = 0;

            for (++i; i < end; ++i)
            {
                c = s[i];
                if (c > 57 || c < 48)
                {
                    if (c == '-')
                    {
                        expSign = -1;
                        continue;
                    }
                }

                exp = 10 * exp + (c - 48);
            }

            exponent = _doubleExpLookup[exp] * expSign;

            goto ProcessFraction;
        }

       

        private static readonly long[] _powLookup = new[]
        {
  1, // 10^0
  10, // 10^1
  100, // 10^2
  1000, // 10^3
  10000, // 10^4
  100000, // 10^5
  1000000, // 10^6
  10000000, // 10^7
  100000000, // 10^8
  1000000000, // 10^9,
  10000000000, // 10^10,
  100000000000, // 10^11,
  1000000000000, // 10^12,
  10000000000000, // 10^13,
  100000000000000, // 10^14,
  1000000000000000, // 10^15,
  10000000000000000, // 10^16,
  100000000000000000, // 10^17,
};

        private static readonly double[] _doubleExpLookup = GetDoubleExponents();

        private static double[] GetDoubleExponents()
        {
            var max = 309;

            var exps = new double[max];

            for (var i = 0; i < max; i++)
            {
                exps[i] = Math.Pow(10, i);
            }

            return exps;
        }
    }
}