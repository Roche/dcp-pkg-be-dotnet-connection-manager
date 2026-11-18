using System.Text.Json.Nodes;

namespace OpenDCP.Framework.Connections.Manager.Models
{
    public class ConnectionStringTransferDTO
    {
        public required JsonObject ConnectionString { get; set; }
        public int DatasourceId { get; set; }
        public required string ConnectionType { get; set; }
        public bool IsGxP { get; set; }
    }
}
