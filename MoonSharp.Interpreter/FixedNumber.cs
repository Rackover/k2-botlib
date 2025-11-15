namespace MoonSharp.Interpreter
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public readonly struct FixedNumber
    {
        private const byte PRECISION = 10;

        private readonly long internalValue;

        public static readonly FixedNumber MaxValue = long.MaxValue;

        public static readonly FixedNumber PI = new FixedNumber(3216); // Pi << 10

        public static readonly FixedNumber E = new FixedNumber(2783); // E << 10
        

        public static implicit operator FixedNumber(int num)
        {
            return new FixedNumber(num << PRECISION);
        }

        public static implicit operator FixedNumber(long num)
        {
            return new FixedNumber(num << PRECISION);
        }

        public static implicit operator FixedNumber(double num)
        {
            return new FixedNumber((long)Math.Floor(num * (1 << PRECISION)));
        }

        public static implicit operator double(FixedNumber fixedNumber)
        {
            return fixedNumber.internalValue / ((double)(1 << PRECISION));
        }

        public static implicit operator long(FixedNumber fixedNumber)
        {
            return fixedNumber.GetRoundedValue();
        }

        public static implicit operator int(FixedNumber fixedNumber)
        {
            return checked((int)(fixedNumber.GetRoundedValue()));
        }

        public static bool operator !=(FixedNumber a, FixedNumber b)
        {
            return a.internalValue != b.internalValue;
        }

        public static bool operator >(FixedNumber a, FixedNumber b)
        {
            return a.internalValue > b.internalValue;
        }

        public static bool operator <(FixedNumber a, FixedNumber b)
        {
            return a.internalValue < b.internalValue;
        }

        public static bool operator ==(FixedNumber a, FixedNumber b)
        {
            return a.internalValue == b.internalValue;
        }

        public static FixedNumber operator +(FixedNumber a, FixedNumber b)
        {
            return new FixedNumber(a.internalValue + b.internalValue);
        }

        public static FixedNumber operator -(FixedNumber a)
        {
            return new FixedNumber(-a.internalValue);
        }

        public static FixedNumber operator -(FixedNumber a, FixedNumber b)
        {
            return new FixedNumber(a.internalValue - b.internalValue);
        }

        public static FixedNumber operator *(FixedNumber a, FixedNumber b)
        {
            return new FixedNumber((a.internalValue * b.internalValue) >> PRECISION);
        }

        public static FixedNumber operator / (FixedNumber a, FixedNumber b){
            return a.internalValue / b.internalValue;
        }

        public int CompareTo(FixedNumber other)
        {
            return internalValue.CompareTo(other.internalValue);
        }

        private FixedNumber(long internalValue)
        {
            this.internalValue = internalValue;
        }

        private FixedNumber(int internalValue)
        {
            this.internalValue = internalValue;
        }

        private long GetRoundedValue()
        {
            return internalValue >> PRECISION;
        }

        public static FixedNumber Log(FixedNumber a, FixedNumber b)
        {
            return new FixedNumber((long)Math.Log(a.internalValue, (long)b));
        }

        public static FixedNumber Pow(FixedNumber x, FixedNumber y)
        {
            return (long)Math.Pow(x.internalValue, y.internalValue);
        }

        public static FixedNumber Floor(FixedNumber a)
        {
            return new FixedNumber(a.GetRoundedValue() << PRECISION);
        }

        public static FixedNumber Round(FixedNumber a)
        {
            return Floor(new FixedNumber(((1 << PRECISION) / 2) + a.internalValue));
        }

        public static FixedNumber Ceil(FixedNumber a)
        {
            return Floor(new FixedNumber(((1 << PRECISION) - 1) + a.internalValue));
        }

        public static FixedNumber IEEERemainder(FixedNumber x, FixedNumber y)
        {
            if (y == 0) {
                return 0;
            }

            var q = x / y;

            return x - (y * q);
        }

        public static bool TryParse(string s, NumberStyles style, IFormatProvider provider, out FixedNumber result)
        {
            if (long.TryParse(s, style, provider, out long longResult)) {
                result = longResult;
                return true;
            }

            result = default;
            return false;
        }

        public override bool Equals(object obj)
        {
            return obj is FixedNumber number &&
                   internalValue == number.internalValue;
        }

        public override int GetHashCode()
        {
            return 1989885529 + internalValue.GetHashCode();
        }

        public string ToString(CultureInfo culture)
        {
            return GetRoundedValue().ToString(culture);
        }

        public override string ToString()
        {
            return $"fx:{((double)(internalValue / (double)(1 << PRECISION)))}";
        }
    }
}
