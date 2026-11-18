using OpenDCP.Framework.Configuration.Model.Constants.Datasource;
using OpenDCP.Framework.Configuration.Model.Interfaces;
using OpenDCP.Framework.Connections.Manager.Helpers;
using OpenDCP.Framework.Connections.Manager.Interfaces;
using OpenDCP.Framework.Connections.Manager.Models;
using OpenDCP.Framework.Connections.Manager.PluginManagement;
using OpenDCP.Framework.Connections.Manager.Strategies;

namespace OpenDCP.Framework.Connections.Manager.Pipeline
{
    public class GraphMergePipeline
    {
        private readonly DatasourceCache _cache;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConnectionStringLoader _connectionStringLoader;

        private List<ConnectionStringTransferDTO> _connectionStrings = new List<ConnectionStringTransferDTO>();
        public GraphMergePipeline(DatasourceCache datasourceCache, IServiceProvider sp, IConnectionStringLoader loader)
        {
            _cache = datasourceCache;
            _serviceProvider = sp;
            _connectionStringLoader = loader;
        }

        public delegate ITreeNode<T> TreeAdapterFactory<T, TInterface>(TInterface source);

        public async Task<TResponse> RunPipeline<TInterface, TNode, TResponse>(
           int siteId, DataType dataType, int mapArgIndex,
           TreeAdapterFactory<TNode, TInterface> adapterFactory,
           Func<TInterface, object[], CancellationToken, Task<TResponse>> pluginActionAsync,
           Func<ConnectionContext<TResponse>, TResponse>? transformFun = null,
           CancellationToken cancellationToken = default,
           params object[] args
        )
           where TInterface : class
        {
            var id = args[mapArgIndex] as string ?? throw new ArgumentNullException("Missing map argument");

            var ids = await FindMatchingIds(siteId, dataType);

            var pipeline = new PluginPipeline<object[], TResponse>(
                _cache,
                _serviceProvider,
                _connectionStringLoader
            )
            .WithDefaultReduceFunction();

            if (transformFun is null)
                pipeline = pipeline.WithDefaultTransformFunction();
            else
                pipeline = pipeline.WithCustomTransformFunction(transformFun);

            var groups = new List<ConnectionContext<object[]>>
            {
                new ConnectionContext<object[]>
                {
                    DatasourceId = WebIdEncoding.Decode(id).DatasourceId,
                    Request = args
                }
            };

            if (ids.Count > 1)
            {
                var briges = await _connectionStringLoader.LoadConnectionBridges(
                    siteId,
                    dataType,
                    dataType
                );

                var conString = await _connectionStringLoader.Load([WebIdEncoding.Decode(id).DatasourceId]!);
                var sPluginInstance = pipeline.GetPluginInstance<TInterface>(conString.First());

                if (sPluginInstance == null)
                    throw new InvalidOperationException($"Failed to create plugin instance of type {typeof(TInterface).Name}");

                foreach (var brig in briges)
                {
                    IFindMatchingNodeStrategy? strategy = null;

                    if (brig.Strategy == Constants.ConnectionBridgeStrategyType.IdLookUp)
                    {
                        strategy = new LookUpStrategy(brig.Definition);
                    }
                    else if (brig.Strategy == Constants.ConnectionBridgeStrategyType.Attribute)
                    {
                        strategy = new AttributeMatchingStrategy(brig.Definition);
                    }
                    else if (brig.Strategy == Constants.ConnectionBridgeStrategyType.Path)
                    {
                        strategy = new PathMatchingStrategy(brig.Definition);
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }

                    var sNode = adapterFactory(sPluginInstance);

                    var tPluginInstance = pipeline.GetPluginInstance<TInterface>(ids.Find(x => x.DatasourceId == brig.TConId)
                        ?? throw new InvalidOperationException($"Cannot resolve plugin instance because datasource '{brig.TConId}' does not exist"));

                    if (tPluginInstance == null)
                        throw new InvalidOperationException($"Failed to create plugin instance of type {typeof(TInterface).Name}");

                    var tNode = adapterFactory(tPluginInstance);

                    var tConElement = (await strategy.FindMatchingNode<TNode>(
                        id: WebIdEncoding.Decode(id.Split('/').First()).WebId,
                        sNode,
                        tNode
                     )).FirstOrDefault();

                    if (tConElement is not null)
                    {
                        var context = new ConnectionContext<object[]>
                        {
                            DatasourceId = brig.TConId,
                            Request = new object[] { tNode.GetLink(tConElement) }
                        };

                        groups.Add(context);
                    }
                }
            }

            var result = await pipeline.RunReduceAsync<TInterface>(
                (plugin, request, token) => pluginActionAsync(plugin, request, token),
                groups,
                cancellationToken
            );

            return result;
        }

        private async Task<List<ConnectionStringTransferDTO>> FindMatchingIds(int siteId, DataType dataType)
        {
            return await _connectionStringLoader.Load(siteId, dataType);
        }

        public bool GetValidationStatus()
        {
            return _connectionStrings.All(x => x.IsGxP);
        }
    }
}
