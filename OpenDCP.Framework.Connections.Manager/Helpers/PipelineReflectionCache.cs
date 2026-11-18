using OpenDCP.Framework.Configuration.Model.Attributes;
using OpenDCP.Framework.Configuration.Model.Constants.Datasource;
using OpenDCP.Framework.Connections.Manager.Models;
using System.Collections.Concurrent;
using System.Reflection;

namespace OpenDCP.Framework.Connections.Manager.Helpers
{
    public static class PipelineReflectionCache
    {
        private static readonly ConcurrentDictionary<Type, CachedTypeMetadata> ReflectionCache =
            new ConcurrentDictionary<Type, CachedTypeMetadata>();

        internal static CachedTypeMetadata GetTypeMetadata(Type type)
        {
            return ReflectionCache.GetOrAdd(type, t =>
            {
                var allProps = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead && p.CanWrite)
                    .Select(p => new PropertyMetadata
                    {
                        Property = p,
                        IsString = p.PropertyType == typeof(string),
                        IsStringEnumerable = typeof(IEnumerable<string>).IsAssignableFrom(p.PropertyType),
                        IsClass = p.PropertyType.IsClass && p.PropertyType != typeof(string),
                        IsEnumerable = typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string)
                    }).ToArray();

                // Filter out properties based on which action attributes are present
                var decodeProps = allProps
                    .Where(m => m.Property.GetCustomAttributes<ConnectionManagerAttribute>()
                    .Any(a => a.Action == RequestPipelineAction.Decode))
                    .ToArray();

                var transformProps = allProps
                    .Where(m => m.Property.GetCustomAttributes<ConnectionManagerAttribute>()
                    .Any(a => a.Action == RequestPipelineAction.Transform))
                    .ToArray();

                var mapProps = allProps
                    .Where(m => m.Property.GetCustomAttributes<ConnectionManagerAttribute>()
                    .Any(a => a.Action == RequestPipelineAction.Map))
                    .ToArray();

                return new CachedTypeMetadata
                {
                    AllProperties = allProps,
                    DecodeProperties = decodeProps,
                    TransformProperties = transformProps,
                    MapProperties = mapProps
                };
            });
        }
    }
}
