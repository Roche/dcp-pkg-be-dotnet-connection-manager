using OpenDCP.Framework.Configuration.Model.Interfaces;
using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.Connections.Manager.Interfaces;
using OpenDCP.Framework.Connections.Manager.KeyTemplateEngine;
using OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions;


namespace OpenDCP.Framework.Connections.Manager.Strategies
{
    internal class AttributeMatchingStrategy : IFindMatchingNodeStrategy
    {
        private readonly AttributeMatchingStrategyDefiniton _definition;

        public AttributeMatchingStrategy(StrategyDefintion defintion)
        {
            _definition = (AttributeMatchingStrategyDefiniton)defintion;
        }
        public async Task<List<TNode>> FindMatchingNode<TNode>(string id, ITreeNode<TNode> sCon, ITreeNode<TNode> tCon)
        {
            var result = new List<TNode>();

            var attributes = await sCon.GetAttribtuesAsync(id);

            var el = await tCon.FindByAttributesAsync(attributes);

            if (el is not null)
            {
                result.AddRange(el);
            }

            return result;
        }


        public Dictionary<string,Dictionary<string,object>> TranslateForward(Dictionary<string, Dictionary<string, object>> source, TranslationDirection direction = TranslationDirection.Forward)
        {
            var engine = new TemplateEngine();

            return source
                .Select(s => new
                {
                    SId = s.Key,
                    SearchStrings = _definition.Keys.Select(k => new {
                        TKey = k.tKey,
                        SearchString = engine.Compile(k.sKey).Render(s.Value.ToDictionary(d => d.Key, d => (string)d.Value))
                    })
                })
                .ToDictionary(s => s.SId, s =>  s.SearchStrings
                        .Select(x => engine.Compile(x.TKey).TryParse(x.SearchString, out Dictionary<string,string> n ) ? n : null )
                                    .SelectMany(dict => dict)
                                    .ToDictionary(kv => kv.Key, kv => (object)kv.Value)
                );
            
        }     
    }
}
