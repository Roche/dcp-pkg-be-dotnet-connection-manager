using OpenDCP.Framework.Configuration.Model.Interfaces;
using OpenDCP.Framework.Connections.Manager.KeyTemplateEngine;
using OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions;

namespace OpenDCP.Framework.Connections.Manager.Strategies
{
    public class QueryLookUpStrategy
    {
        private readonly QueryLookUpStrategyDefiniton _definition;
        private readonly TemplateEngine _engine = new();

        public QueryLookUpStrategy(StrategyDefintion definition)
        {
            _definition = (QueryLookUpStrategyDefiniton)definition;
        }

        public async Task<Dictionary<string, List<string>>> FindMatchingByQuery(string id, IRelationDataSource con)
        {
            var parameters = new Dictionary<string, string?>
            {
                { nameof(id), id ?? string.Empty }
            };

            var compiledQuery = _engine.Compile(_definition.Query).Render(parameters);

            var result = await con.FindByQueryAsync(compiledQuery);

            return result ?? new Dictionary<string, List<string>>();
        }
    }
}
