using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions;

namespace OpenDCP.Framework.Connections.Manager.Models
{
    internal class BridgeContextDTO
    {
        public string Id { get; set; } = default!;
        public StrategyDefintion? Definition { get; set; }
        public int? SDatasourceId { get; set; }
        public int? TDatasourceId { get; set; }
        public ConnectionBridgeStrategyType? Strategy { get; set; }
    }
}
