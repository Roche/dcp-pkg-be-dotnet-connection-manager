namespace OpenDCP.Framework.Connections.Manager.Models
{
    public class ConnectionTypeContext<T>
    {
        public required string ConnectionType { get; set; }
        public required T Request { get; set; }
    }
}
