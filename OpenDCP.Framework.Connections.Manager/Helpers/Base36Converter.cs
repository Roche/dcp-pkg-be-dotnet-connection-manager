namespace OpenDCP.Framework.Connections.Manager.Helpers
{
    internal class Base36Converter
    {
        private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"; // 36 chars

        public static string ToBase36String(int value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Only non-negative values supported");

            var chars = new char[5];
            for (int i = 4; i >= 0; i--)
            {
                chars[i] = Alphabet[value % 36];
                value /= 36;
            }

            if (value > 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Value too large for 5-digit base36");

            return new string(chars);
        }

        public static int FromBase36String(string s)
        {
            if (s == null || s.Length != 5)
                throw new ArgumentException("String must be exactly 5 characters long", nameof(s));

            int value = 0;
            foreach (var c in s)
            {
                int index = Alphabet.IndexOf(c);
                if (index < 0)
                    throw new ArgumentException($"Invalid character '{c}' in base36 string");

                value = value * 36 + index;
            }
            return value;
        }
    }
}