using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions;

namespace OpenDCP.Framework.Connections.Manager.Factories
{
    public static class ConnectionBridgeStrategyFactory
    {
        public static StrategyDefintion LoadByType(ConnectionBridgeStrategyType type)
        {
            switch (type)
            {
                case ConnectionBridgeStrategyType.Attribute:
                    return new AttributeMatchingStrategyDefiniton();
                case ConnectionBridgeStrategyType.Path:
                    return new PathMatchingStrategyDefition();
                case ConnectionBridgeStrategyType.IdLookUp:
                    return new IdLookUpStrategyDefiniton();
                case ConnectionBridgeStrategyType.QueryLookUp:
                    return new QueryLookUpStrategyDefiniton();
                default:
                    throw new ArgumentException($"The {nameof(type)} is not known as a connection bridge strategy");
            }
        }
    }
}
