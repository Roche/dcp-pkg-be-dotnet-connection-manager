using System.Globalization;

namespace OpenDCP.Framework.Connections.Manager.KeyTemplateEngine.Operators
{
    public sealed class ReplaceOperator : IOperator
    {
        public string Name => "replace";

        // This operator is reversible if both search and replace are non-empty
        public bool IsReversible => true;

        public string Forward(string value, string[] args, CultureInfo culture)
        {
            var search = args.Length > 0 ? args[0] : string.Empty;
            var replace = args.Length > 1 ? args[1] : string.Empty;
            return value.Replace(search, replace);
        }

        public bool TryReverse(string value, string[] args, CultureInfo culture, out string reversed)
        {
            reversed = value;

            // Cannot safely reverse if replace is empty
            if (args.Length < 2 || string.IsNullOrEmpty(args[1]))
                return false;

            var search = args[0];
            var replace = args[1];

            // Reverse by swapping replace back to search
            reversed = value.Replace(replace, search);
            return true;
        }
    }
}
