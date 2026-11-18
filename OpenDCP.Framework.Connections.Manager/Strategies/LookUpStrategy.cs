using OpenDCP.Framework.Configuration.Model.Interfaces;
using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.Connections.Manager.Helpers;
using OpenDCP.Framework.Connections.Manager.Interfaces;
using OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions;

namespace OpenDCP.Framework.Connections.Manager.Strategies
{
    public class LookUpStrategy : IFindMatchingNodeStrategy
    {
        private readonly IdLookUpStrategyDefiniton _definition;

        public LookUpStrategy(StrategyDefintion defintion)
        {
            _definition = (IdLookUpStrategyDefiniton)defintion;
        }

        public async Task<List<TNode>> FindMatchingNode<TNode>(string id, ITreeNode<TNode> sCon, ITreeNode<TNode> tCon)
        {
            var result = new List<TNode>();

            if (_definition.IdMap.ContainsKey(id))
            {
                var el = await tCon.FindByIdAsync(_definition.IdMap[id]);
                result.Add(el);
            }

            return result;
        }

        internal string TranslateForward(string source, int? tConId, TranslationDirection direction = TranslationDirection.Forward)
        {
            _definition.IdMap.TryGetValue(WebIdEncoding.Decode(source).WebId, out var result);
            var id =  result ?? source;
            return WebIdEncoding.Encode(webId: id, datasourceId: tConId.Value, format: WebIdType.DCP);
        }
    }
}
