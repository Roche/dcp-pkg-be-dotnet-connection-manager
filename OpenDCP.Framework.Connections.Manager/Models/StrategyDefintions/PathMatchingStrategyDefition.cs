using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.FormBuilder.Models;

namespace OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions
{
    public class PathMatchingStrategyDefition : StrategyDefintion
    {
        public PathMatchingStrategyDefition()
        {
            Type = ConnectionBridgeStrategyType.Path;
        }

        public override List<InputField> Form()
        {
            return new List<InputField>();
        }

        public override void LoadFromForm(List<FormValue> values)
        {
            
        }
    }
}
