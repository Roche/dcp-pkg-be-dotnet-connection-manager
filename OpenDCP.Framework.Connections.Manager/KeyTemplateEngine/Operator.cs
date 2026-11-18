using System.Globalization;


namespace OpenDCP.Framework.Connections.Manager.KeyTemplateEngine
{
    internal interface IOperator
    {
        string Name { get; }

        string Forward(string value, string[] args, CultureInfo culture);

        bool IsReversible { get; }

        bool TryReverse(
            string value,
            string[] args,
            CultureInfo culture,
            out string reversed);
    }
}
