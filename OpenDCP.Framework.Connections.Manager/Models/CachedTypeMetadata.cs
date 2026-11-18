namespace OpenDCP.Framework.Connections.Manager.Models
{
    internal class CachedTypeMetadata
    {
        public PropertyMetadata[] DecodeProperties { get; set; } = Array.Empty<PropertyMetadata>();
        public PropertyMetadata[] TransformProperties { get; set; } = Array.Empty<PropertyMetadata>();
        public PropertyMetadata[] MapProperties { get; set; } = Array.Empty<PropertyMetadata>();
        public PropertyMetadata[] AllProperties { get; set; } = Array.Empty<PropertyMetadata>();
    }
}
