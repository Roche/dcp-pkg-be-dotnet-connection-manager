using OpenDCP.Framework.Configuration.Model.Attributes;

namespace OpenDCP.Framework.Connections.Manager.Models
{
    public class RequestContainer
    {
        public object[]? Args { get; set; }
        [ConnectionManagerAttribute(Configuration.Model.Constants.Datasource.RequestPipelineAction.Map)]
        public object? MapArgs { get; set; }
    }
}
