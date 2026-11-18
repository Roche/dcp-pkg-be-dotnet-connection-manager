using System.Text;

namespace OpenDCP.Framework.Connections.Manager.KeyTemplateEngine
{
    internal static class TemplateParser
    {
        public static CompiledTemplate Parse(
            string template,
            Dictionary<string, IOperator> operatorRegistry,
            bool strict)
        {
            var segments = new List<ISegment>();
            var buffer = new StringBuilder();

            for (int i = 0; i < template.Length; i++)
            {
                if (template[i] == '\\')
                {
                    if (i + 1 < template.Length)
                    {
                        buffer.Append(template[i + 1]);
                        i++;
                    }
                    continue;
                }

                if (template[i] == '{')
                {
                    if (buffer.Length > 0)
                    {
                        segments.Add(new TextSegment(buffer.ToString()));
                        buffer.Clear();
                    }

                    int end = template.IndexOf('}', i);
                    if (end < 0)
                        throw new FormatException("Missing closing brace.");

                    var content = template.Substring(i + 1, end - i - 1);
                    segments.Add(ParsePlaceholder(content, operatorRegistry, strict));

                    i = end;
                }
                else
                {
                    buffer.Append(template[i]);
                }
            }

            if (buffer.Length > 0)
                segments.Add(new TextSegment(buffer.ToString()));

            return new CompiledTemplate(segments);
        }

        private static PlaceholderSegment ParsePlaceholder(
            string content,
            Dictionary<string, IOperator> operatorRegistry,
            bool strict)
        {
            var parts = SplitEscaped(content, '|');
            var key = parts[0];

            var pipeline = new List<(IOperator Op, string[] Args)>();

            for (int i = 1; i < parts.Length; i++)
            {
                var opParts = SplitEscaped(parts[i], ':');
                var opName = opParts[0];

                if (!operatorRegistry.TryGetValue(opName, out var op))
                    throw new InvalidOperationException($"Unknown operator '{opName}' in template.");

                var args = opParts.Length > 1 ? opParts[1..] : Array.Empty<string>();
                pipeline.Add((op, args));
            }

            return new PlaceholderSegment(key, pipeline, strict);
        }

        private static string[] SplitEscaped(string input, char separator)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool escaped = false;

            foreach (var ch in input)
            {
                if (escaped)
                {
                    sb.Append(ch);
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == separator)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(ch);
                }
            }

            result.Add(sb.ToString());
            return result.ToArray();
        }
    }
}
