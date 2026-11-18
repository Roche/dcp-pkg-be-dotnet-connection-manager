using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.FormBuilder.Models;

namespace OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions
{
    public abstract class StrategyDefintion
    {
        public ConnectionBridgeStrategyType Type { get; set; }
        public virtual List<InputField> Form()
        {
            return new List<InputField>();
        }
        public abstract void LoadFromForm(List<FormValue> values);
    }
}
