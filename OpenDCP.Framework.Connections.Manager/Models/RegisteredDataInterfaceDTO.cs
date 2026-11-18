namespace OpenDCP.Framework.Connections.Manager.Models
{
    public class RegisteredDataInterfaceDTO
    {
        public required string Name { get; set; }
        public required string Version { get; set; }
        public List<RegisteredDatasourceDTO> Datasources { get; set; } = new List<RegisteredDatasourceDTO>();
    }
}
