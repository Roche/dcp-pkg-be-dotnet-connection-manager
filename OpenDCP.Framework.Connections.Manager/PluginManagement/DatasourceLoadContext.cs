using NuGet.Versioning;
using OpenDCP.Framework.Exceptions;
using System.Reflection;
using System.Runtime.Loader;

public class DatasourceLoadContext : AssemblyLoadContext
{
    private readonly string _pluginFolder;
    private readonly AssemblyDependencyResolver _resolver;
    private readonly SemanticVersion _pluginVersion;
    private readonly HashSet<string> _hostContractAssemblies;

    private static readonly HashSet<string> SharedFrameworkAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "OpenDCP.Framework.Datasource.Base",
        "OpenDCP.Framework.Configuration.Model",
        "OpenDCP.Framework.FormBuilder",
        "Microsoft.Extensions.Configuration.Abstractions",
        "System.Text.Json"
    };

    public DatasourceLoadContext(
        string pluginFolder,
        string pluginMainDllPath,
        SemanticVersion pluginVersion,
        IEnumerable<string> hostContracts
    )
        : base(isCollectible: true)
    {
        _pluginFolder = pluginFolder;
        _pluginVersion = pluginVersion;
        _hostContractAssemblies = new HashSet<string>(hostContracts, StringComparer.OrdinalIgnoreCase);

        _resolver = new AssemblyDependencyResolver(pluginMainDllPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null)
            return null;

        // 1. Shared Framework Assemblies -> Delegate directly to Host (AssemblyLoadContext.Default)
        if (SharedFrameworkAssemblies.Contains(assemblyName.Name))
        {
            // Returning null tells .NET to fallback to AssemblyLoadContext.Default
            return null;
        }

        // 2. Host Contract Assemblies -> Delegate to Host if present/compatible
        if (_hostContractAssemblies.Contains(assemblyName.Name))
        {
            try
            {
                // Try loading from default context first to guarantee type identity
                var defaultAsm = AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
                if (defaultAsm != null && defaultAsm.GetName().Version?.Major == assemblyName.Version?.Major)
                {
                    return defaultAsm;
                }
            }
            catch
            {
                // If not in Default ALC, fall back to plugin folder
            }

            var pluginPath = Path.Combine(_pluginFolder, assemblyName.Name + ".dll");
            if (File.Exists(pluginPath))
            {
                return LoadFromAssemblyPath(pluginPath);
            }

            throw new DCPNotFoundException($"Contract assembly {assemblyName.Name} required by plugin {_pluginFolder} not found.");
        }

        // 3. Plugin-only assemblies -> Load from plugin folder using resolver
        var pluginResolved = _resolver.ResolveAssemblyToPath(assemblyName);
        if (pluginResolved != null)
        {
            return LoadFromAssemblyPath(pluginResolved);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolvedPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (resolvedPath != null)
            return LoadUnmanagedDllFromPath(resolvedPath);

        return IntPtr.Zero;
    }
}