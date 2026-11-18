using OpenDCP.Framework.Configuration.Model.Interfaces;
using OpenDCP.Framework.Connections.Manager.Interfaces;
using OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions;


namespace OpenDCP.Framework.Connections.Manager.Strategies
{
    internal class PathMatchingStrategy : IFindMatchingNodeStrategy
    {
        private readonly PathMatchingStrategyDefition _definition;

        public PathMatchingStrategy(StrategyDefintion defintion)
        {
            _definition = (PathMatchingStrategyDefition)defintion;
        }
        public async Task<List<TNode>> FindMatchingNode<TNode>(string id, ITreeNode<TNode> sCon, ITreeNode<TNode> tCon)
        {
            var result = new List<TNode>();

            var path = await sCon.GetPathAsync(id);
            var el = await tCon.FindByPathAsync(path);

            if (el is not null )
            {
                result.Add(el);
            }
           
            return result;
        }
    }
}
