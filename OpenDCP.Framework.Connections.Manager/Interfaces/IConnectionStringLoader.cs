using OpenDCP.Framework.Configuration.Model.Constants.Datasource;
using OpenDCP.Framework.Connections.Manager.Models;

namespace OpenDCP.Framework.Connections.Manager.Interfaces
{
    public interface IConnectionStringLoader
    {
        public Task<List<ConnectionStringTransferDTO>> Load(List<int> datasourceIds);
        public Task<List<ConnectionStringTransferDTO>> Load(int siteId, DataType dataType);
        public Task<List<ConnectionBridgeDTO>> LoadConnectionBridges(int siteId, DataType sConDataType, DataType tConDataType);
    }
}
