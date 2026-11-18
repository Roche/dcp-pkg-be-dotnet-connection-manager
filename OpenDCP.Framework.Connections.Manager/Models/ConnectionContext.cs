namespace OpenDCP.Framework.Connections.Manager.Models
{
    public class ConnectionContext<T>
    {
        public int DatasourceId { get; set; }
        public required T Request { get; set; }
    }
}
