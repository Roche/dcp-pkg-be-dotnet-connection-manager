using System.Globalization;


namespace OpenDCP.Framework.Connections.Manager.KeyTemplateEngine
{
    internal interface ISegment
    {
        string Render(IDictionary<string, string?> values, CultureInfo culture);
    }

    internal sealed class TextSegment : ISegment
    {
        // The literal text this segment represents
        public string Value { get; }

        public TextSegment(string text)
        {
            Value = text ?? throw new ArgumentNullException(nameof(text));
        }

        // Forward rendering: just return the literal
        public string Render(IDictionary<string, string?> _, CultureInfo __)
        {
            return Value;
        }
    }

    internal sealed class PlaceholderSegment : ISegment
    {
        public string Key { get; }

        private readonly List<(IOperator Op, string[] Args)> _pipeline;

        private readonly bool _strict;

        public PlaceholderSegment(
            string key,
            List<(IOperator Op, string[] Args)> pipeline,
            bool strict)
        {
            Key = key;
            _pipeline = pipeline;
            _strict = strict;
        }

        public string Render(
            IDictionary<string, string?> values,
            CultureInfo culture)
        {
            values.TryGetValue(Key, out var value);
            value ??= string.Empty;

            foreach (var (op, args) in _pipeline)
            {
                value = op.Forward(value, args, culture);
            }

            return value;
        }

        public bool TryReverse(
            string input,
            CultureInfo culture,
            out string reversed,
            out string? error)
        {
            reversed = input;
            error = null;

            // Reverse pipeline order
            for (int i = _pipeline.Count - 1; i >= 0; i--)
            {
                var (op, args) = _pipeline[i];

                if (!op.IsReversible)
                {
                    if (_strict)
                    {
                        error = $"Operator '{op.Name}' is not reversible.";
                        return false;
                    }

                    // lenient mode → skip
                    continue;
                }

                if (!op.TryReverse(reversed, args, culture, out reversed))
                {
                    error = $"Operator '{op.Name}' failed to reverse.";
                    return false;
                }
            }

            return true;
        }
    }
}
