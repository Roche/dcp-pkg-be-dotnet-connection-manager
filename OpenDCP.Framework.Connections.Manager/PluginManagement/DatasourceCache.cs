using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using OpenDCP.Framework.Connections.Manager.Helpers;
using OpenDCP.Framework.Connections.Manager.Models;
using OpenDCP.Framework.Exceptions;
using System.Diagnostics;
using System.Reflection;

namespace OpenDCP.Framework.Connections.Manager.PluginManagement
{
    public class DatasourceCache
    {
        private readonly List<DatasourceLoadContext> _contexts = new();
        private readonly Dictionary<Type, List<Type>> _pluginTypesByInterface = new();
        private readonly Dictionary<string, string> _connectionTypes = new();

        public List<RegisteredDataInterfaceDTO> RegisteredInfo = new List<RegisteredDataInterfaceDTO>();

        public string InstanceId { get; } = Guid.NewGuid().ToString().Substring(0, 8);

        private static readonly string LogFilePath = Path.Combine(Path.GetTempPath(), "cache.log");
        private static readonly object FileLock = new object();
        private readonly bool _debug;

        public DatasourceCache(string? pluginsRoot, bool debug = false, params Type[] contractInterfaces)
        {
            _debug = debug;

            // Host-side contract assemblies for version/identity checks
            var hostContracts = contractInterfaces.Select(t => t.Assembly.GetName().Name!).ToList();

            // Host-side contract versions
            var hostVersions = contractInterfaces
                .ToDictionary(
                    t => t.Name,
                    t =>
                    {
                        var attr = t.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                        var versionString = attr?.InformationalVersion ?? t.Assembly.GetName().Version?.ToString() ?? "0.0.0";
                        return SemanticVersion.Parse(versionString);
                    },
                    StringComparer.OrdinalIgnoreCase);

            if (pluginsRoot is null)
            {
                return;
            }

            foreach (var pluginDir in Directory.GetDirectories(pluginsRoot))
            {
                var pluginName = Path.GetFileName(pluginDir);

                try
                {
                    //Get all version folders, ordered by newest deployment date
                    var availableVersions = Directory.GetDirectories(pluginDir)
                        .Select(vdir => new
                        {
                            Path = vdir,
                            Version = TryParseVersion(Path.GetFileName(vdir)),
                            DeployDate = new DirectoryInfo(vdir).LastWriteTime
                        })
                        .Where(x => x.Version != null)
                        .OrderByDescending(x => x.DeployDate)
                        .ToList();

                    if (!availableVersions.Any())
                        continue;


                    var allReferencedContractNames = availableVersions
                        .Select(v =>
                        {
                            string rawPath = Path.Combine(v.Path, $"OpenDCP.Framework.Datasource.{pluginName}.dll");
                            return File.Exists(rawPath) ? Path.GetFullPath(rawPath) : null;
                        })
                        .Where(path => !string.IsNullOrEmpty(path))
                        .AsParallel()
                        .Where(File.Exists)
                        .SelectMany(dllPath =>
                        {
                            var matchedContracts = new List<string>();
                            foreach (var contractName in hostContracts)
                            {
                                var ver = PluginVersionHelper.GetPluginContractVersion(dllPath, contractName);
                                if (ver != null) matchedContracts.Add(contractName);
                            }
                            return matchedContracts;
                        })
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var targetInterfacesForThisPlugin = contractInterfaces
                        .Where(iface => allReferencedContractNames.Contains(iface.Assembly.GetName().Name!))
                        .ToList();

                    if (!targetInterfacesForThisPlugin.Any())
                        continue;

                    //Track which interfaces have found their single match for this plugin
                    var registeredInterfacesForThisPlugin = new HashSet<Type>();

                    //Loop through versions first and find the best match, then handle interfaces
                    foreach (var v in availableVersions)
                    {
                        if (registeredInterfacesForThisPlugin.Count == targetInterfacesForThisPlugin.Count)
                            break;

                        var mainDllPath = Path.Combine(v.Path, $"OpenDCP.Framework.Datasource.{pluginName}.dll");
                        if (!File.Exists(mainDllPath))
                            continue;

                        var currentPluginVersion = TryParseVersion(Path.GetFileName(v.Path))!;

                        DatasourceLoadContext? context = null;
                        List<Type>? allAvailableTypes = null;
                        bool contextWasSaved = false;

                        foreach (var iface in targetInterfacesForThisPlugin)
                        {
                            if (registeredInterfacesForThisPlugin.Contains(iface))
                                continue;

                            var hostVersion = hostVersions[iface.Name];
                            var contractAssemblyName = iface.Assembly.GetName().Name!;

                            var referencedVersion = PluginVersionHelper.GetPluginContractVersion(mainDllPath, contractAssemblyName);
                            if (referencedVersion == null)
                                continue;

                            var pluginSemVer = VersionHelper.ParseFlexible(referencedVersion.ToString());
                            if (!IsCompatible(pluginSemVer, hostVersion))
                                continue;

                            if (allAvailableTypes == null)
                            {
                                try
                                {
                                    context = new DatasourceLoadContext(pluginDir, mainDllPath, currentPluginVersion, hostContracts);
                                    var assembly = context.LoadFromAssemblyPath(mainDllPath);
                                    allAvailableTypes = GetLoadableTypes(assembly);
                                }
                                catch (Exception ex)
                                {
                                    break; //skip to next version folder
                                }
                            }

                            // Filter out the implementing types from pre-loaded list
                            var impls = allAvailableTypes
                                .Where(t => iface.IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                                .ToList();

                            if (impls.Count == 0)
                                continue;

                            var interfaceInfo = GetOrCreateInterfaceInfo(iface, referencedVersion);

                            foreach (var type in impls)
                            {
                                var prop = type.GetProperty("ConnectionType", BindingFlags.Public | BindingFlags.Static);
                                if (prop != null && prop.PropertyType == typeof(string))
                                {
                                    var value = (string)prop.GetValue(null)!;
                                    _connectionTypes[value] = type.Name;
                                }

                                interfaceInfo.Datasources.Add(new RegisteredDatasourceDTO
                                {
                                    Name = type.Name,
                                    Version = currentPluginVersion.ToFullString(),
                                });
                            }

                            if (!_pluginTypesByInterface.TryGetValue(iface, out var list))
                            {
                                list = new List<Type>();
                                _pluginTypesByInterface[iface] = list;
                            }

                            list.AddRange(impls);
                            registeredInterfacesForThisPlugin.Add(iface);
                            contextWasSaved = true;
                        }

                        if (context != null)
                        {
                            if (contextWasSaved)
                            {
                                _contexts.Add(context);
                            }
                            else
                            {
                                context.Unload();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[DatasourceCache ERROR] Error processing plugin directory '{pluginName}'. Exception: {ex}");
                }
            }
        }

        // <summary>
        /// Registers a plugin type statically for Native AOT environments where dynamic assembly scanning is disabled.
        /// </summary>
        public void RegisterStaticPlugin<TInterface>(Type implementationType, string connectionType, string version = "1.0.0")
        {
            var iface = typeof(TInterface);

            if (!iface.IsAssignableFrom(implementationType))
            {
                throw new ArgumentException($"Type '{implementationType.FullName}' does not implement contract '{iface.FullName}'.");
            }

            // 1. Map type by interface
            if (!_pluginTypesByInterface.TryGetValue(iface, out var list))
            {
                list = new List<Type>();
                _pluginTypesByInterface[iface] = list;
            }

            if (!list.Contains(implementationType))
            {
                list.Add(implementationType);
            }

            // 2. Map ConnectionType string (e.g., "Mock") -> Plugin Implementation Class Name
            _connectionTypes[connectionType] = implementationType.Name;

            // 3. Populate RegisteredInfo DTO for diagnostic listings
            var dto = GetOrCreateInterfaceInfo(iface, new Version(1, 0, 0, 0));
            if (!dto.Datasources.Any(d => d.Name == implementationType.Name))
            {
                dto.Datasources.Add(new RegisteredDatasourceDTO
                {
                    Name = implementationType.Name,
                    Version = version
                });
            }
        }

        private static List<Type> GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            try
            {
                var types = assembly.GetTypes().ToList();
                return types;
            }
            catch (ReflectionTypeLoadException e)
            {
                var validTypes = e.Types.Where(t => t != null).ToList()!;
                return validTypes;
            }
        }

        public IEnumerable<Type> GetPluginTypes<T>()
        {
            return _pluginTypesByInterface.TryGetValue(typeof(T), out var list)
                ? list
                : Enumerable.Empty<Type>();
        }

        public IEnumerable<string> GetPluginNames<T>() =>
            GetPluginTypes<T>().Select(t => t.Name).Distinct().OrderBy(n => n);

        public IEnumerable<string> GetPluginConnectionTypes()
        {
            return _connectionTypes.Keys;
        }

        public IEnumerable<string> GetPluginConnectionTypes<T>()
        {
            return _connectionTypes.Where(kvp => GetPluginNames<T>().Contains(kvp.Value)).Select(kvp => kvp.Key);
        }

        public T CreatePluginByName<T>(string name, IServiceProvider sp, params object[] manualParameters)
        {
            var type = GetPluginTypes<T>().FirstOrDefault(t => t.Name == name)
                       ?? throw new DCPNotFoundException($"Plugin '{name}' not found for interface {typeof(T).Name}");

            manualParameters ??= Array.Empty<object>();

            var config = sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>();

            LogToFile($"Creating plugin instance: {name}, Type: {type.FullName}, {config} ManualParameters: {string.Join(", ", manualParameters.Select(p => p?.ToString() ?? "null"))}");
            LogToFile($"Types: {string.Join(", ", manualParameters.Select(p => p?.GetType().Name ?? "null"))}");

            try
            {
                return (T)ActivatorUtilities.CreateInstance(sp, type, manualParameters);
            }
            catch (Exception ex)
            {
                LogToFile($"Error creating plugin instance: {ex.InnerException ?? ex}");

                throw;
            }
        }

        public T CreatePluginByConnectionType<T>(string connectionType, IServiceProvider sp, params object[] manualParameters)
        {
            if (_connectionTypes.ContainsKey(connectionType))
            {
                return CreatePluginByName<T>(_connectionTypes[connectionType], sp, manualParameters);
            }
            else
            {
                throw new DCPInternalServerErrorException($"Not registered connection type {connectionType}, registered connection types: {string.Join(',', _connectionTypes.Keys)}");
            }
        }

        private static SemanticVersion? TryParseVersion(string folderName)
        {
            var name = folderName.TrimStart('v');
            return SemanticVersion.TryParse(name, out var v) ? v : null;
        }

        private static bool IsCompatible(SemanticVersion pluginVersion, SemanticVersion hostVersion) =>
            pluginVersion.Major == hostVersion.Major && pluginVersion.Minor <= hostVersion.Minor;

        private RegisteredDataInterfaceDTO GetOrCreateInterfaceInfo(Type type, Version interfaceVersion)
        {
            var existing = RegisteredInfo.FirstOrDefault(x =>
                x.Name.Equals(type.Name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            var dto = new RegisteredDataInterfaceDTO
            {
                Name = type.Name,
                Version = interfaceVersion.ToString()
            };

            RegisteredInfo.Add(dto);
            return dto;
        }

        private void LogToFile(string message)
        {
            if (!_debug)
                return;

            try
            {
                lock (FileLock)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    File.AppendAllText(LogFilePath, $"[{timestamp}] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }

    }
}
