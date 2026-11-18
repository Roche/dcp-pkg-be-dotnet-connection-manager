using OpenDCP.Framework.Connections.Manager.KeyTemplateEngine.Operators;

namespace OpenDCP.Framework.Connections.Manager.KeyTemplateEngine
{
    internal sealed class TemplateEngine
    {
        // Registry of available operators
        private readonly Dictionary<string, IOperator> _operators;

        // Strict mode flag: enforces unambiguous templates and reversible parsing
        private readonly bool _strict;

        public TemplateEngine(bool strict = true)
        {
            _strict = strict;
            _operators = new Dictionary<string, IOperator>(StringComparer.OrdinalIgnoreCase);

            // Register built-in operators
            RegisterBuiltInOperators();
        }

        public void RegisterOperator(IOperator op)
        {
            _operators[op.Name] = op;
        }

        public CompiledTemplate Compile(string template)
        {
            return TemplateParser.Parse(template, _operators, _strict);
        }

        private void RegisterBuiltInOperators()
        {
            RegisterOperator(new ReplaceOperator());
        }
    }
}
