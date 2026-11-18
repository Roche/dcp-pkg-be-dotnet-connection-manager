using OpenDCP.Framework.Configuration.Model.Attributes;
using OpenDCP.Framework.Configuration.Model.Constants.Datasource;
using OpenDCP.Framework.Configuration.Model.Interfaces;
using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.Connections.Manager.Helpers;
using OpenDCP.Framework.Connections.Manager.Interfaces;
using OpenDCP.Framework.Connections.Manager.Models;
using OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions;
using OpenDCP.Framework.Connections.Manager.PluginManagement;
using OpenDCP.Framework.Connections.Manager.Strategies;
using OpenDCP.Framework.Exceptions;
using System.Reflection;


namespace OpenDCP.Framework.Connections.Manager.Pipeline
{
    public class EventTranslationPipeline
    {
        private readonly DatasourceCache _cache;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConnectionStringLoader _connectionStringLoader;

        private List<ConnectionBridgeDTO> _bridges = new List<ConnectionBridgeDTO>();
        public EventTranslationPipeline(DatasourceCache datasourceCache, IServiceProvider sp, IConnectionStringLoader loader)
        {
            _cache = datasourceCache;
            _serviceProvider = sp;
            _connectionStringLoader = loader;
        }

        public async Task<(TRequest, Dictionary<string, List<string>>)> TranslateForward<TRequest>(
           int siteId, DataType sDataType, DataType tDataType,
           TRequest model
        )
        {
            _bridges = await _connectionStringLoader.LoadConnectionBridges(siteId, sDataType, tDataType);

            if (_bridges.Count == 0)
                return (model, new Dictionary<string, List<string>>());

            var properties = GetTranslatableProperties<TRequest>();
            List<string> ids = ExtractIds(model, properties);

            var datasourceIds = ids.Select(x => WebIdEncoding.Decode(x).DatasourceId).Distinct().ToList();

            _bridges = _bridges.Where(x => datasourceIds.Contains(x.SConId)).ToList();

            if (_bridges.Count == 0)
                return (model, new Dictionary<string, List<string>>());

            var groupedBridges = GroupBridgesByStrategy(ids, _bridges);

            var translations = await ExecuteStrategies(groupedBridges, ids);

            //Return model and dict for reuse
            ApplyTranslations(model, properties, translations);

            var reverseDict = BuildReverseDictionary(translations, ids);

            return (model, reverseDict);
        }


        public TResponse TranslateBackward<TResponse>(Dictionary<string, List<string>> dict, TResponse model)
        {
            if (model == null) return model;

            var dictWithQueue = dict.ToDictionary(
                kvp => kvp.Key,
                kvp => new Queue<string>(kvp.Value)
            );

            TranslateObject(model, dictWithQueue, new HashSet<object>());

            return model;
        }

        private void TranslateObject(object? obj, Dictionary<string, Queue<string>> dict, HashSet<object> visited)
        {
            if (obj == null || !visited.Add(obj)) return;

            var meta = PipelineReflectionCache.GetTypeMetadata(obj.GetType());

            foreach (var propMeta in meta.TransformProperties)
            {
                object? value;
                try
                {
                    value = propMeta.Property.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                if (value == null) continue;

                if (propMeta.IsString)
                {
                    var s = (string)value;
                    if (!string.IsNullOrEmpty(s))
                    {
                        if (dict.TryGetValue(s, out var queue))
                        {
                            if (queue.TryDequeue(out var originalEncodedId))
                            {
                                propMeta.Property.SetValue(obj, originalEncodedId);
                            }
                            else
                            {
                                throw new IndexOutOfRangeException(
                                    $"Translation mismatch on property '{propMeta.Property.Name}': The mapping pool for key '{s}' has run out of items.");
                            }
                        }
                    }
                }
                else if (propMeta.IsStringEnumerable)
                {
                    var translatedValues = new List<string>();
                    foreach (var s in (IEnumerable<string>)value)
                    {
                        if (!string.IsNullOrEmpty(s))
                        {
                            if (dict.TryGetValue(s, out var queue))
                            {
                                if (queue.TryDequeue(out var originalEncodedId))
                                {
                                    translatedValues.Add(originalEncodedId);
                                }
                                else
                                {
                                    throw new IndexOutOfRangeException(
                                        $"Translation mismatch inside collection '{propMeta.Property.Name}': The mapping pool for key '{s}' has run out of items.");
                                }
                            }
                            else
                            {
                                throw new KeyNotFoundException(
                                    $"Translation missing: The raw internal key '{s}' (decoded from '{s}') was not found in the translation dictionary mapping table.");
                            }
                        }
                        else
                        {
                            throw new DCPBadRequestException(
                                $"Translation error in collection '{propMeta.Property.Name}': Encountered a null or empty string element which cannot be translated.");
                        }
                    }

                    if (propMeta.Property.PropertyType.IsArray)
                    {
                        propMeta.Property.SetValue(obj, translatedValues.ToArray());
                    }
                    else
                    {
                        propMeta.Property.SetValue(obj, translatedValues);
                    }
                }
                else
                {
                    throw new DCPBadRequestException($"Translation property '{propMeta.Property.Name}' must be string or IEnumerable<string>");
                }
            }

            foreach (var propMeta in meta.AllProperties)
            {
                if (propMeta.Property.GetCustomAttributes<ConnectionManagerAttribute>().Any(a => a.Action == RequestPipelineAction.Transform))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = propMeta.Property.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                if (value == null) continue;

                if (propMeta.IsEnumerable)
                {
                    foreach (var item in (System.Collections.IEnumerable)value)
                    {
                        if (item != null && item.GetType().IsClass && item.GetType() != typeof(string))
                        {
                            TranslateObject(item, dict, visited);
                        }
                    }
                }
                else if (propMeta.IsClass)
                {
                    TranslateObject(value, dict, visited);
                }
            }
        }

        public bool GetValidationStatus()
        {
            return _bridges.All(x => x.IsGxP);
        }

        private static List<string> ExtractIds<T>(
            T model,
            IEnumerable<PropertyInfo> properties)
        {
            var ids = new List<string>();

            foreach (var prop in properties)
            {
                var value = prop.GetValue(model);
                if (value == null) continue;

                switch (value)
                {
                    case string s:
                        ids.Add(s);
                        break;

                    case IEnumerable<string> enumerable:
                        ids.AddRange(enumerable.Where(x => x != null));
                        break;

                    default:
                        throw new DCPBadRequestException(
                            "Translation properties must be string or IEnumerable<string>");
                }
            }

            return ids;
        }

        private static PropertyInfo[] GetTranslatableProperties<T>()
        {
            return typeof(T)
                .GetProperties()
                .Where(p => p.GetCustomAttributes<ConnectionManagerAttribute>()
                    .Any(a => a.Action == RequestPipelineAction.Decode))
                .ToArray();
        }

        private static IEnumerable<IGrouping<ConnectionBridgeStrategyType?, BridgeContextDTO>>
            GroupBridgesByStrategy(
                IEnumerable<string> ids,
                IEnumerable<ConnectionBridgeDTO> bridges)
        {
            return ids
                .Select(id => new
                {
                    Id = id,
                    SDatasourceId = WebIdEncoding.Decode(id).DatasourceId
                })
                .GroupJoin(
                    bridges,
                    s => s.SDatasourceId,
                    b => b.SConId,
                    (s, b) => new { s.Id, Bridges = b })
                .SelectMany(
                    s => s.Bridges.DefaultIfEmpty(),
                    (s, bridge) => new BridgeContextDTO
                    {
                        Id = s.Id,
                        Definition = bridge?.Definition,
                        SDatasourceId = bridge?.SConId,
                        TDatasourceId = bridge?.TConId,
                        Strategy = bridge?.Strategy
                    })
                .Where(x => x.Strategy != null)
                .GroupBy(x => x.Strategy);
        }

        private async Task<Dictionary<string, List<string>>> ExecuteStrategies(
            IEnumerable<IGrouping<ConnectionBridgeStrategyType?, BridgeContextDTO>> groupedBridges,
            List<string> ids)
        {
            var dict = new Dictionary<string, List<string>>();

            foreach (var group in groupedBridges)
            {
                switch (group.Key)
                {
                    case ConnectionBridgeStrategyType.Attribute:
                        await ExecuteAttributeStrategy(group, ids, dict);
                        break;

                    case ConnectionBridgeStrategyType.IdLookUp:
                        ExecuteLookupStrategy(group, dict);
                        break;

                    case ConnectionBridgeStrategyType.QueryLookUp:
                        await ExecuteQueryLookupStrategy(group, dict);
                        break;

                    default:
                        throw new DCPBadRequestException("Unsupported strategy");
                }
            }

            return dict;
        }

        private async Task ExecuteAttributeStrategy(
            IEnumerable<BridgeContextDTO> group,
            List<string> ids,
            Dictionary<string, List<string>> dict,
            CancellationToken cancellationToken = default)
        {
            var sPipeline =
                new PluginPipeline<RequestContainer,
                    Dictionary<string, Dictionary<string, object>>>(
                    _cache, _serviceProvider, _connectionStringLoader)
                .WithDefaultMapFunction()
                .WithDefaultTransformFunction()
                .WithDefaultReduceFunction();

            var metaData = await sPipeline.RunMapReduceAsync<IEventTranslation>(
                (datasource, args, token) =>
                    datasource.GetAttribtuesAsync((List<string>)args[0]),
                mapArgIndex: 0,
                args: new object[] { ids },
                cancellationToken);

            var searchContext =
                new List<ConnectionContext<Dictionary<string,
                    Dictionary<string, object>>>>();

            foreach (var item in group)
            {
                var bridge = new AttributeMatchingStrategy(item.Definition);

                searchContext.Add(
                    new ConnectionContext<
                        Dictionary<string, Dictionary<string, object>>>
                    {
                        Request = bridge.TranslateForward(metaData),
                        DatasourceId = item.TDatasourceId!.Value
                    });
            }

            var tPipeline =
                new PluginPipeline<
                    Dictionary<string, Dictionary<string, object>>,
                    Dictionary<string, List<string>>>(
                    _cache, _serviceProvider, _connectionStringLoader)
                .WithDefaultMapFunction()
                .WithDefaultTransformFunction()
                .WithDefaultReduceFunction();

            var result = await tPipeline.RunReduceAsync<IEventTranslation>(
                (plugin, attributes, token) =>
                    plugin.FindByAttributesAsync(attributes),
                groups: searchContext,
                cancellationToken);

            var encodedResult = EncodeAttributeResults(result, group);

            MergeTranslations(dict, encodedResult);
        }

        private static Dictionary<string, List<string>> EncodeAttributeResults(
        Dictionary<string, List<string>> result,
        IEnumerable<BridgeContextDTO> group)
        {
            var encodedDict = new Dictionary<string, List<string>>();
            var groupList = group.ToList();

            var sourceDatasourceMap = groupList
                .Where(x => x.SDatasourceId.HasValue && !string.IsNullOrEmpty(x.Id))
                .ToDictionary(x => WebIdEncoding.Decode(x.Id).WebId, x => x.SDatasourceId!.Value);

            var targetDatasourceMap = groupList
                .Where(x => x.TDatasourceId.HasValue && !string.IsNullOrEmpty(x.Id))
                .ToDictionary(x => WebIdEncoding.Decode(x.Id).WebId, x => x.TDatasourceId!.Value);

            foreach (var kvp in result)
            {
                string rawSourceKey = kvp.Key;

                int sourceDatasourceId = sourceDatasourceMap.TryGetValue(rawSourceKey, out var sDsId)
                    ? sDsId
                    : throw new DCPBadRequestException($"Missing source datasource id for {rawSourceKey}");

                string encodedSourceKey = WebIdEncoding.Encode(
                    webId: rawSourceKey,
                    datasourceId: sourceDatasourceId,
                    format: WebIdType.DCP);

                int targetDatasourceId = targetDatasourceMap.TryGetValue(rawSourceKey, out var tDsId)
                    ? tDsId
                    : throw new DCPBadRequestException($"Missing target datasource id for {rawSourceKey}");

                var encodedValues = kvp.Value
                    .Select(rawTargetId => WebIdEncoding.Encode(
                        webId: rawTargetId,
                        datasourceId: targetDatasourceId,
                        format: WebIdType.DCP))
                    .Distinct()
                    .ToList();

                if (!encodedDict.ContainsKey(encodedSourceKey))
                {
                    encodedDict[encodedSourceKey] = new List<string>();
                }

                encodedDict[encodedSourceKey].AddRange(encodedValues);
            }

            return encodedDict;
        }

        private static void ExecuteLookupStrategy(
            IEnumerable<BridgeContextDTO> group,
            Dictionary<string, List<string>> dict)
        {
            foreach (var item in group)
            {
                var strategy = new LookUpStrategy(item.Definition);
                var translated = strategy.TranslateForward(item.Id, item.TDatasourceId);

                if (string.IsNullOrWhiteSpace(translated))
                    continue;

                AddTranslation(dict, item.Id, translated);
            }
        }

        private async Task ExecuteQueryLookupStrategy(
            IEnumerable<BridgeContextDTO> group,
            Dictionary<string, List<string>> dict,
            CancellationToken cancellationToken = default)
        {
            var pipeline = new PluginPipeline<object, Dictionary<string, List<string>>>(
                _cache, _serviceProvider, _connectionStringLoader);

            foreach (var item in group.ToList())
            {
                var queryDefinition = item.Definition as QueryLookUpStrategyDefiniton
                    ?? throw new DCPBadRequestException("Invalid definition for QueryLookUp strategy");

                var queryStrategy = new QueryLookUpStrategy(queryDefinition);

                var conString = await _connectionStringLoader.Load([item.SDatasourceId!.Value]);
                var con = pipeline.GetPluginInstance<IRelationDataSource>(conString.First())
                    ?? throw new InvalidOperationException(
                        $"Failed to create plugin instance for datasource '{item.SDatasourceId}'");

                var matches = await queryStrategy.FindMatchingByQuery(WebIdEncoding.Decode(item.Id).WebId, con);

                if (matches.Count == 0) continue;

                var lookupStrategy = new LookUpStrategy(new IdLookUpStrategyDefiniton());

                foreach (var (originalId, candidates) in matches)
                {
                    foreach (var candidate in candidates)
                    {
                        var translated = lookupStrategy.TranslateForward(candidate, item.TDatasourceId);
                        if (string.IsNullOrWhiteSpace(translated)) continue;

                        AddTranslation(dict, originalId, translated);
                    }
                }
            }
        }

        private static void ApplyTranslations<T>(
            T model,
            IEnumerable<PropertyInfo> properties,
            Dictionary<string, List<string>> dict)
        {
            foreach (var prop in properties)
            {
                var value = prop.GetValue(model);
                if (value == null) continue;

                switch (value)
                {
                    case string s:
                        if (dict.TryGetValue(s, out var values))
                            prop.SetValue(model, values.FirstOrDefault());
                        break;

                    case IEnumerable<string> enumerable:
                        prop.SetValue(model,
                            enumerable
                                .Where(s => dict.ContainsKey(s))
                                .SelectMany(s => dict[s])
                                .ToList());
                        break;
                }
            }
        }

        private static Dictionary<string, List<string>> BuildReverseDictionary(
        Dictionary<string, List<string>> dict,
        List<string> ids)
        {
            if (dict == null || ids == null) return new Dictionary<string, List<string>>();

            return dict
                .SelectMany(kvp => kvp.Value.Select(v => new
                {
                    TargetRawKey = v,
                    SourceRawKey = kvp.Key
                }))
                .GroupBy(x => x.TargetRawKey)
                .ToDictionary(
                    g => g.Key,
                    g => g.SelectMany(item =>
                        ids.Where(encodedId => !string.IsNullOrEmpty(encodedId) &&
                                               (encodedId == item.SourceRawKey ||
                                                WebIdEncoding.Decode(encodedId).WebId == item.SourceRawKey))
                    )
                    .Distinct()
                    .ToList()
                );
        }

        private static void MergeTranslations(
            Dictionary<string, List<string>> target,
            Dictionary<string, List<string>> source)
        {
            foreach (var kvp in source)
            {
                foreach (var value in kvp.Value)
                {
                    AddTranslation(target, kvp.Key, value);
                }
            }
        }

        private static void AddTranslation(
            Dictionary<string, List<string>> dict,
            string source,
            string target)
        {
            if (!dict.TryGetValue(source, out var list))
            {
                dict[source] = new List<string> { target };
                return;
            }

            if (!list.Contains(target))
                list.Add(target);
        }
    }
}
