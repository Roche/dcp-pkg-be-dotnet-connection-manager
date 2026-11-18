using OpenDCP.Framework.Configuration.Model.Interfaces;

namespace OpenDCP.Framework.Connections.Manager.Interfaces
{
    public interface IFindMatchingNodeStrategy
    {
        Task<List<TNode>> FindMatchingNode<TNode>(string id, ITreeNode<TNode> sCon, ITreeNode<TNode> tCon);
    }
}
