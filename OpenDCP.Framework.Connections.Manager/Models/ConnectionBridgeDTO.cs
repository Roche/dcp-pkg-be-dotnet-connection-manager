using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions;

namespace OpenDCP.Framework.Connections.Manager.Models
{
    public class ConnectionBridgeDTO
    {
        public int SConId { get; set; }
        public int TConId { get; set; }
        public ConnectionBridgeStrategyType Strategy { get; set; }
        public required StrategyDefintion Definition { get; set; }
        public bool IsGxP { get; set; }
    }
}
