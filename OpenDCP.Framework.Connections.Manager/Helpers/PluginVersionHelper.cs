using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;


public static class PluginVersionHelper
{
    public static Version? GetPluginContractVersion(string pluginDllPath, string contractAssemblyName)
    {
        if (string.IsNullOrWhiteSpace(pluginDllPath) || !File.Exists(pluginDllPath))
            return null;

        try
        {
            using var stream = File.OpenRead(pluginDllPath);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
                return null;

            var mdReader = peReader.GetMetadataReader();

            foreach (var handle in mdReader.AssemblyReferences)
            {
                var reference = mdReader.GetAssemblyReference(handle);
                string name = mdReader.GetString(reference.Name);

                if (string.Equals(name, contractAssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    return reference.Version;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
