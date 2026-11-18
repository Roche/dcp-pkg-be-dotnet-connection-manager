using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;


namespace OpenDCP.Framework.Connections.Manager.KeyTemplateEngine
{
    internal sealed class CompiledTemplate
    {
        private readonly List<ISegment> _segments;
        private readonly bool _strict = false;

        internal CompiledTemplate(List<ISegment> segments)
        {
            _segments = segments;
        }

        public string Render(
            IDictionary<string, string?> values,
            CultureInfo? culture = null)
        {
            culture ??= CultureInfo.InvariantCulture;

            var sb = new StringBuilder();

            foreach (var segment in _segments)
                sb.Append(segment.Render(values, culture));

            return sb.ToString();
        }

        public bool TryParse(
            string input,
            out Dictionary<string, string> values,
            CultureInfo? culture = null)
        {
            culture ??= CultureInfo.InvariantCulture;
            values = new Dictionary<string, string>();

            var regex = BuildRegex();
            var match = regex.Match(input);

            if (!match.Success)
                return false;

            foreach (var groupName in regex.GetGroupNames())
            {
                if (int.TryParse(groupName, out _))
                    continue;

                values[groupName] = match.Groups[groupName].Value;
            }

            // reverse operators
            foreach (var segment in _segments)
            {
                if (segment is PlaceholderSegment ph)
                {
                    if (!values.TryGetValue(ph.Key, out var value))
                        continue;

                    if (!ph.TryReverse(value, culture, out var reversed, out var error))
                    {
                        // Optionally handle strict mode failure
                        // You can throw, log, or return false
                        return false;
                    }

                    values[ph.Key] = reversed;
                }
            }

            return true;
        }

        private Regex BuildRegex()
        {
            var sb = new StringBuilder();
            sb.Append("^");

            bool lastWasPlaceholder = false;

            foreach (var segment in _segments)
            {
                if (segment is TextSegment text)
                {
                    // Escape all regex special characters in literals
                    sb.Append(Regex.Escape(text.Value));
                    lastWasPlaceholder = false;
                }
                else if (segment is PlaceholderSegment ph)
                {
                    if (_strict && lastWasPlaceholder)
                    {
                        throw new InvalidOperationException(
                            $"Ambiguous template: adjacent placeholders '{ph.Key}' without literal separator.");
                    }

                    // Lazy match everything until next literal or placeholder
                    sb.Append($"(?<{ph.Key}>.+?)");

                    lastWasPlaceholder = true;
                }
            }

            sb.Append("$");
            return new Regex(sb.ToString(), RegexOptions.Compiled);
        }
    }
}
