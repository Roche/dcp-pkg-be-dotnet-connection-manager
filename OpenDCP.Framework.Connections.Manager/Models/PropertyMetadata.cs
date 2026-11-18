using System.Reflection;

namespace OpenDCP.Framework.Connections.Manager.Models
{
    internal class PropertyMetadata
    {
        public PropertyInfo Property { get; set; } = null!;
        public bool IsString { get; set; }
        public bool IsStringEnumerable { get; set; }
        public bool IsClass { get; set; }
        public bool IsEnumerable { get; set; }
    }
}