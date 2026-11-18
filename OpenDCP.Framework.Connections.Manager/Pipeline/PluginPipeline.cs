using Microsoft.Extensions.Logging;
using OpenDCP.Framework.Configuration.Model.Attributes;
using OpenDCP.Framework.Configuration.Model.Constants.Datasource;
using OpenDCP.Framework.Configuration.Model.Errors;
using OpenDCP.Framework.Configuration.Model.Extensions;
using OpenDCP.Framework.Configuration.Model.Responses.Datasource;
using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.Connections.Manager.Helpers;
using OpenDCP.Framework.Connections.Manager.Interfaces;
using OpenDCP.Framework.Connections.Manager.Models;
using OpenDCP.Framework.Connections.Manager.PluginManagement;
using OpenDCP.Framework.Exceptions;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace OpenDCP.Framework.Connections.Manager.Pipeline
{
    public class PluginPipeline<TRequest, TResponse>
    {
        private readonly DatasourceCache _cache;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConnectionStringLoader _connectionStringLoader;
        private readonly bool _debug;

        private List<ConnectionStringTransferDTO> _connectionStrings = new List<ConnectionStringTransferDTO>();
        private Func<TRequest, TRequest>? _decodeFunction;
        private Func<TRequest, Task<IEnumerable<ConnectionContext<TRequest>>>>? _mapFunction = null;
        private Func<ConnectionContext<TResponse>, TResponse>? _transformFunction;
        private Func<IEnumerable<TResponse>, TResponse>? _mergeFunction;
        private WebIdType _webIdFormat;

        private static readonly ConcurrentDictionary<Type, CachedTypeMetadata> ReflectionCache = new();

        private readonly ILogger<PluginPipeline<TRequest, TResponse>> _logger;
        private static readonly string LogFilePath = Path.Combine(Path.GetTempPath(), "pipeline.log");
        private static readonly object FileLock = new object();

        public PluginPipeline(DatasourceCache datasourceCache, IServiceProvider sp, IConnectionStringLoader loader, bool debug = false)
        {
            _cache = datasourceCache;
            _serviceProvider = sp;
            _connectionStringLoader = loader;
            _webIdFormat = WebIdType.DCP;
            _debug = debug;

            _logger = (ILogger<PluginPipeline<TRequest, TResponse>>)sp.GetService(typeof(ILogger<PluginPipeline<TRequest, TResponse>>))
              ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginPipeline<TRequest, TResponse>>.Instance;
        }

        public async Task<TResponse> RunMapReduceAsync<TInterface>(
             Func<TInterface, TRequest, CancellationToken, Task<TResponse>> pluginActionAsync,
             TRequest model,
             CancellationToken cancellationToken = default)
         where TInterface : class
        {
            if (_mapFunction == null)
                throw new Exception("Mapping function not defined");

            if (_decodeFunction != null)
            {
                model = _decodeFunction(model);
            }

            var groups = await _mapFunction(model);

            return await RunMapReduceCoreAsync<TInterface>(
                groups,
                (plugin, ctx, token) => pluginActionAsync(plugin, ctx.Request, token),
                null,
                cancellationToken);
        }

        public async Task<TResponse> RunMapReduceWithTimeoutAsync<TInterface>(
            Func<TInterface, TRequest, CancellationToken, Task<TResponse>> pluginActionAsync,
            TRequest model,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        where TInterface : class
        {
            if (_mapFunction == null)
                throw new Exception("Mapping function not defined");

            if (_decodeFunction != null)
            {
                model = _decodeFunction(model);
            }

            var groups = await _mapFunction(model);
            return await RunMapReduceCoreAsync<TInterface>(
                groups,
                (plugin, ctx, token) => pluginActionAsync(plugin, ctx.Request, token),
                timeout,
                cancellationToken);
        }

        public async Task<bool> ValidateDatasourceRegistration<TInterface>(TRequest model)
        {
            if (_mapFunction == null)
                throw new Exception("Mapping function not defined");

            if (_decodeFunction != null)
            {
                model = _decodeFunction(model);
            }

            var groups = await _mapFunction(model);

            // Load connection strings
            _connectionStrings = await _connectionStringLoader.Load(
                groups.Select(g => g.DatasourceId).Distinct().ToList());

            var registeredPlugins = _cache.GetPluginConnectionTypes<TInterface>();

            return _connectionStrings.All(x => registeredPlugins.Contains(x.ConnectionType));
        }

        public async Task<TResponse> RunMapReduceAsync<TInterface>(
            Func<TInterface, object[], CancellationToken, Task<TResponse>> pluginActionAsync,
            int? mapArgIndex,
            object[] args,
            CancellationToken cancellationToken = default)
            where TInterface : class
        {
            if (_mapFunction == null)
                throw new Exception("Mapping function not defined");

            var model = new RequestContainer
            {
                Args = args,
                MapArgs = mapArgIndex != null ? args[(int)mapArgIndex] : null,
            };

            var request = (TRequest)(object)model;

            if (_decodeFunction != null)
            {
                request = _decodeFunction.Invoke(request) ?? request;
            }

            var groups = await _mapFunction(request);

            return await RunMapReduceCoreAsync<TInterface>(
                groups,
                (plugin, ctx, token) =>
                {
                    var localArgs = (object[])args.Clone();

                    if (mapArgIndex != null)
                    {
                        var mappedValue = ((RequestContainer)(object)ctx.Request!).MapArgs ?? new RequestContainer();
                        localArgs[(int)mapArgIndex] = mappedValue;
                    }

                    return pluginActionAsync(plugin, localArgs, token);
                },
                null,
                cancellationToken);
        }

        public async Task<TResponse> RunMapReduceAsync<TInterface>(
            Func<TInterface, CancellationToken, Task<TResponse>> pluginActionAsync,
            CancellationToken cancellationToken = default)
            where TInterface : class
        {
            var dummyRequest = Activator.CreateInstance<TRequest>();

            if (_mapFunction == null)
                throw new ArgumentNullException("Mapping function not defined");

            var groups = await _mapFunction(dummyRequest);

            return await RunMapReduceCoreAsync<TInterface>(
                groups,
                (plugin, ctx, token) => pluginActionAsync(plugin, token),
                null,
                cancellationToken);
        }

        public async Task<TResponse> RunMapReduceWithTimeoutAsync<TInterface>(
           Func<TInterface, CancellationToken, Task<TResponse>> pluginActionAsync,
           TimeSpan timeout,
           CancellationToken cancellationToken = default)
           where TInterface : class
        {
            var dummyRequest = Activator.CreateInstance<TRequest>();

            if (_mapFunction == null)
                throw new ArgumentNullException("Mapping function not defined");

            var groups = await _mapFunction(dummyRequest);

            return await RunMapReduceCoreAsync<TInterface>(
                groups,
                (plugin, ctx, token) => pluginActionAsync(plugin, token),
                timeout,
                cancellationToken);
        }

        public async Task<List<ConnectionTypeContext<TResponse>>> RunOnAllTypesAsync<TInterface>(
            Func<TInterface, object[], CancellationToken, Task<TResponse>> pluginActionAsync,
            object[] args,
            CancellationToken cancellationToken = default)
            where TInterface : class
        {

            var connectionTypes = _cache.GetPluginConnectionTypes();

            // Execute plugin per datasource
            var tasks = connectionTypes.Select(async type =>
            {
                var plugin = _cache.CreatePluginByConnectionType<TInterface>(
                    type,
                    _serviceProvider,
                    new object[] { }
                );

                var response = new ConnectionTypeContext<TResponse>
                {
                    ConnectionType = type,
                    Request = await pluginActionAsync(plugin, args, cancellationToken)
                };

                return response;

            }).ToList();

            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }


        public async Task<TResponse> RunOnConnectionTypeAsync<TInterface>(
            Func<TInterface, object[], CancellationToken, Task<TResponse>> pluginActionAsync,
            string connectionType,
            object[] args,
            CancellationToken cancellationToken = default)
            where TInterface : class
        {
            var plugin = _cache.CreatePluginByConnectionType<TInterface>(
                    connectionType,
                    _serviceProvider,
                    new object[] { }
                );

            return await pluginActionAsync(plugin, args, cancellationToken);
        }

        private async Task<TResponse> RunMapReduceCoreAsync<TInterface>(
            IEnumerable<ConnectionContext<TRequest>> groups,
            Func<TInterface, ConnectionContext<TRequest>, CancellationToken, Task<TResponse>> pluginCall,
            TimeSpan? timeout = null,
            CancellationToken externalToken = default)
            where TInterface : class
        {

            var requestId = Guid.NewGuid();

            if (_transformFunction == null)
                throw new ArgumentNullException("Transform function not defined");
            if (_mergeFunction == null)
                throw new ArgumentNullException("Reduce function not defined");

            _logger.LogTrace("Pipeline processing started. RequestId: {RequestId}", requestId);
            LogToFile("Pipeline started");

            // Load connection strings
            _connectionStrings = await _connectionStringLoader.Load(
                groups.Select(g => g.DatasourceId).Distinct().ToList());
            LogToFile("Connection strings loaded");

            //Create cancelation token if needed
            using var timeoutCts = new CancellationTokenSource();
            if (timeout.HasValue)
                timeoutCts.CancelAfter(timeout.Value);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken, timeoutCts.Token);
            var activeToken = linkedCts.Token;

            // Execute plugin per datasource
            var tasks = groups.Select(async group =>
            {
                try
                {
                    activeToken.ThrowIfCancellationRequested();

                    var conn = _connectionStrings.FirstOrDefault(x => x.DatasourceId == group.DatasourceId);

                    if (conn == null)
                    {
                        if (_connectionStrings.Count() == 1 && group.DatasourceId == 0)
                        {
                            conn = _connectionStrings.First();
                        }
                        else
                        {
                            if (typeof(TResponse).IsAssignableTo(typeof(DatasourceResponse)))
                            {
                                var errorResponse = Activator.CreateInstance<TResponse>();

                                var error = new DatasourceError
                                {
                                    Error = "Mapping Error",
                                    ErrorCode = (byte)ErrorCode.FaildMapStep,
                                    Identifier = group.DatasourceId.ToString()
                                };

                                var errorsProperty = typeof(TResponse).GetProperty("Errors");
                                if (errorsProperty != null)
                                {
                                    var list = (List<DatasourceError>?)errorsProperty.GetValue(errorResponse);
                                    if (list == null)
                                    {
                                        list = new List<DatasourceError>();
                                        errorsProperty.SetValue(errorResponse, list);
                                    }
                                    list.Add(error);
                                }

                                return errorResponse;
                            }
                            else
                            {
                                throw new Exception("Mapping Error");
                            }
                        }
                    }

                    _logger.LogTrace("Creating plugin. RequestId: {RequestId}, DatasourceId: {DatasourceId}", requestId, group.DatasourceId);
                    LogToFile($"Creating plugin. RequestId: {requestId}, DatasourceId: {group.DatasourceId}");

                    var plugin = _cache.CreatePluginByConnectionType<TInterface>(
                        conn.ConnectionType,
                        _serviceProvider,
                        new object[] { (int?)conn.DatasourceId, conn.ConnectionString }
                    );

                    _logger.LogTrace("Plugin created. RequestId: {RequestId}, DatasourceId: {DatasourceId}", requestId, group.DatasourceId);
                    LogToFile($"Plugin created. RequestId: {requestId}, DatasourceId: {group.DatasourceId}");

                    var response = new ConnectionContext<TResponse>
                    {
                        DatasourceId = group.DatasourceId,
                        Request = await pluginCall(plugin, group, activeToken)
                    };

                    _logger.LogTrace("Fetch finished. RequestId: {RequestId}, DatasourceId: {DatasourceId}", requestId, group.DatasourceId);
                    LogToFile($"Fetch finished. RequestId: {requestId}, DatasourceId: {group.DatasourceId}");

                    return _transformFunction(response);
                }
                catch (OperationCanceledException ex)
                {
                    if (timeoutCts.IsCancellationRequested && !externalToken.IsCancellationRequested)
                    {
                        if (typeof(TResponse).IsAssignableTo(typeof(DatasourceResponse)))
                        {
                            var errorResponse = Activator.CreateInstance<TResponse>();

                            var error = new DatasourceError
                            {
                                Error = "Operation timeout",
                                ErrorCode = (byte)ErrorCode.OperationTimeout,
                                Identifier = group.DatasourceId.ToString()
                            };

                            var errorsProperty = typeof(TResponse).GetProperty("Errors");
                            if (errorsProperty != null)
                            {
                                var list = (List<DatasourceError>?)errorsProperty.GetValue(errorResponse);
                                if (list == null)
                                {
                                    list = new List<DatasourceError>();
                                    errorsProperty.SetValue(errorResponse, list);
                                }
                                list.Add(error);
                            }

                            return errorResponse;
                        }
                        else
                        {
                            throw new Exception(ex.Message);
                        }
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    LogToFile(ex.Message);
                    LogToFile(ex.StackTrace);
                    throw;
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);

            _logger.LogInformation("Pipeline processing ended. RequestId: {RequestId}", requestId);

            return _mergeFunction(results);
        }

        internal TInterface GetPluginInstance<TInterface>(ConnectionStringTransferDTO conn)
        {
            return _cache.CreatePluginByConnectionType<TInterface>(
                        conn.ConnectionType,
                        _serviceProvider,
                        new object[] { conn.DatasourceId, conn.ConnectionString }
                    );
        }

        public Task<TResponse> RunReduceAsync<TInterface>(
                Func<TInterface, TRequest, CancellationToken, Task<TResponse>> pluginActionAsync,
                IEnumerable<ConnectionContext<TRequest>> groups,
                CancellationToken cancellationToken = default)
                where TInterface : class
        {
            return RunReduceInternalAsync<TInterface>(
                (plugin, ctx, token) => pluginActionAsync(plugin, ctx.Request, token),
                groups,
                cancellationToken);
        }

        public Task<TResponse> RunReduceAsync<TInterface>(
            Func<TInterface, object[], CancellationToken, Task<TResponse>> pluginActionAsync,
            IEnumerable<ConnectionContext<object[]>> groups,
            object[] args,
            CancellationToken cancellationToken = default)
            where TInterface : class
        {
            // Convert ConnectionContext<object[]> into ConnectionContext<TRequest> shape 
            // so we can reuse the internal method
            var convertedGroups = groups.Select(g => new ConnectionContext<TRequest>
            {
                DatasourceId = g.DatasourceId,
                Request = (TRequest)(object)g.Request  // if TRequest is object[] in this overload
            });

            return RunReduceInternalAsync<TInterface>(
                (plugin, ctx, token) => pluginActionAsync(plugin, (object[])(object)ctx.Request!, token),
                convertedGroups,
                cancellationToken);
        }

        public PluginPipeline<TRequest, TResponse> WithDefaultMapFunction()
        {
            _mapFunction = request => Task.FromResult(MapByAction(request));
            return this;
        }

        public PluginPipeline<TRequest, TResponse> WithConcretMapFunction()
        {
            _mapFunction = request => Task.FromResult(MapConcrete(request));
            return this;
        }

        public PluginPipeline<TRequest, TResponse> WithMapOnAll(int siteId, DataType dataType)
        {
            _mapFunction = request => MapToAllAsync(request, siteId, dataType);
            return this;
        }

        public PluginPipeline<TRequest, TResponse> WithCustomMapFunction(Func<TRequest, Task<IEnumerable<ConnectionContext<TRequest>>>> func)
        {
            _mapFunction = func;
            return this;
        }

        public PluginPipeline<TRequest, TResponse> WithDefaultTransformFunction()
        {
            _transformFunction = response => Transform(response);
            return this;
        }


        public PluginPipeline<TRequest, TResponse> WithoutTransformFunction()
        {
            _transformFunction = response => response.Request;
            return this;
        }

        public PluginPipeline<TRequest, TResponse> WithCustomTransformFunction(Func<ConnectionContext<TResponse>, TResponse> func)
        {
            _transformFunction = func;
            return this;
        }

        public PluginPipeline<TRequest, TResponse> WithDefaultReduceFunction()
        {
            _mergeFunction = responses => Reduce(responses.ToList());
            return this;
        }

        public bool GetValidationStatus()
        {
            return _connectionStrings.All(x => x.IsGxP);
        }

        private static TResponse Reduce(List<TResponse> responses)
        {
            if (responses == null || responses.Count == 0) return default!;
            if (responses.Count == 1) return responses[0];

            var result = responses[0];
            var meta = PipelineReflectionCache.GetTypeMetadata(typeof(TResponse));

            // Instantly find all properties marked for Reduce using reflection cache
            var reduceProperties = meta.AllProperties
                .Where(m => m.Property.GetCustomAttributes<ConnectionManagerAttribute>()
                             .Any(a => a.Action == RequestPipelineAction.Reduce));

            foreach (var metaProp in reduceProperties)
            {
                var prop = metaProp.Property;

                if (!metaProp.IsEnumerable)
                {
                    throw new DCPBadRequestException($"Property {prop.Name} must be a list or IEnumerable<T> to be reduced.");
                }

                var listType = prop.PropertyType.IsInterface
                    ? typeof(List<>).MakeGenericType(prop.PropertyType.GetGenericArguments()[0])
                    : prop.PropertyType;

                var mergedList = Activator.CreateInstance(listType) as IList
                    ?? throw new InvalidOperationException($"Failed to create a list instance for type '{listType.FullName}'");

                foreach (var res in responses)
                {
                    var value = prop.GetValue(res) as IEnumerable;
                    if (value == null) continue;

                    foreach (var item in value)
                        mergedList.Add(item);
                }

                prop.SetValue(result, mergedList);
            }

            return result;
        }

        private TResponse Transform(ConnectionContext<TResponse> source)
        {
            var response = source.Request;
            var visited = new HashSet<object>();

            TransformObject(response, source, visited);

            return response;
        }

        private void DecodeRequest(TRequest request)
        {
            if (request == null)
                return;

            var visited = new HashSet<object>();

            DecodeRequestObject(request, visited);
        }
        public PluginPipeline<TRequest, TResponse> WithDefaultDecodeFunction()
        {
            _decodeFunction = request =>
            {
                DecodeRequest(request);
                return request;
            };

            return this;
        }

        private void DecodeRequestObject(object? obj, HashSet<object> visited)
        {
            if (obj == null || !visited.Add(obj)) return;

            var meta = PipelineReflectionCache.GetTypeMetadata(obj.GetType());

            foreach (var prop in meta.DecodeProperties)
            {
                var value = prop.Property.GetValue(obj);
                if (value == null) continue;

                if (prop.IsString)
                {
                    prop.Property.SetValue(obj, WebIdEncoding.Decode((string)value).WebId);
                }
                else if (prop.IsStringEnumerable)
                {
                    var decodedList = ((IEnumerable<string>)value).Select(v => WebIdEncoding.Decode(v).WebId).ToList();
                    prop.Property.SetValue(obj, decodedList);
                }
            }

            foreach (var prop in meta.AllProperties)
            {
                var value = prop.Property.GetValue(obj);
                if (value == null) continue;

                if (prop.IsEnumerable)
                {
                    foreach (var item in (IEnumerable)value)
                    {
                        if (item != null && item.GetType().IsClass && item.GetType() != typeof(string))
                        {
                            DecodeRequestObject(item, visited);
                        }
                    }
                }
                else if (prop.IsClass)
                {
                    DecodeRequestObject(value, visited);
                }
            }
        }

        private void TransformObject(object? obj, ConnectionContext<TResponse> context, HashSet<object> visited)
        {
            if (obj == null || !visited.Add(obj)) return;

            var meta = PipelineReflectionCache.GetTypeMetadata(obj.GetType());
            // 1. Process ALL properties marked for Transformation
            foreach (var prop in meta.TransformProperties)
            {
                var value = prop.Property.GetValue(obj);
                if (value == null) continue;

                if (prop.IsString)
                {
                    var encoded = WebIdEncoding.Encode((string)value, context.DatasourceId, 1, _webIdFormat);
                    prop.Property.SetValue(obj, encoded);
                }
                else if (prop.IsStringEnumerable)
                {
                    var list = ((IEnumerable<string>)value)
                        .Select(val => WebIdEncoding.Encode(val, context.DatasourceId, 1, _webIdFormat))
                        .ToList();
                    prop.Property.SetValue(obj, list);
                }
            }

            // 2. Drill down into nested paths
            foreach (var prop in meta.AllProperties)
            {
                var value = prop.Property.GetValue(obj);
                if (value == null) continue;

                if (prop.IsEnumerable)
                {
                    foreach (var item in (IEnumerable)value)
                    {
                        if (item != null && item.GetType().IsClass && item.GetType() != typeof(string))
                        {
                            TransformObject(item, context, visited);
                        }
                    }
                }
                else if (prop.IsClass)
                {
                    TransformObject(value, context, visited);
                }
            }
        }

        private static IEnumerable<ConnectionContext<TRequest>> MapConcrete(TRequest source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            if (source is RequestContainer container)
            {
                foreach (var context in HandleRequestContainer(container))
                {
                    yield return context;
                }

                yield break;
            }

            var distinctDatasources = new HashSet<int>();

            var meta = PipelineReflectionCache.GetTypeMetadata(typeof(TRequest));

            foreach (var rm in meta.MapProperties)
            {
                ExtractDatasourceIds(rm.Property.GetValue(source), distinctDatasources);
            }

            if (!distinctDatasources.Any())
            {
                foreach (var prop in meta.AllProperties)
                {
                    var val = prop.Property.GetValue(source);
                    ExtractDatasourceIds(val, distinctDatasources);
                }
            }

            if (!distinctDatasources.Any())
            {
                distinctDatasources.Add(0);
            }

            foreach (var dsId in distinctDatasources)
            {
                yield return new ConnectionContext<TRequest>
                {
                    DatasourceId = dsId,
                    Request = source
                };
            }
        }

        private static IEnumerable<ConnectionContext<TRequest>> MapByAction(TRequest source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            if (source is RequestContainer container)
            {
                foreach (var context in HandleRequestContainer(container))
                {
                    yield return context;
                }

                yield break;
            }

            var meta = PipelineReflectionCache.GetTypeMetadata(typeof(TRequest));

            var rootMapProps = meta.MapProperties;

            var nestedMapTargets = new List<(PropertyInfo CollectionOrClassProp, PropertyMetadata ChildMapProp, object ActiveValue, bool IsCollection)>();

            var runtimeType = source.GetType();

            foreach (var mp in meta.AllProperties.Where(p => p.IsClass || p.IsEnumerable))
            {
                var propValue = mp.Property.GetValue(source);
                if (propValue == null) continue;

                if (mp.IsEnumerable && propValue is IEnumerable collection)
                {
                    foreach (var item in collection)
                    {
                        if (item == null) continue;
                        var childMeta = PipelineReflectionCache.GetTypeMetadata(item.GetType());

                        //Grab only the highest encountered map property on this level
                        var foundChildMap = childMeta.MapProperties.FirstOrDefault();

                        if (foundChildMap != null)
                        {
                            nestedMapTargets.Add((mp.Property, foundChildMap, collection, true));
                            break;
                        }
                    }
                }
                else if (mp.IsClass)
                {
                    var childMeta = PipelineReflectionCache.GetTypeMetadata(propValue.GetType());
                    var foundChildMap = childMeta.MapProperties.FirstOrDefault();

                    if (foundChildMap != null)
                    {
                        nestedMapTargets.Add((mp.Property, foundChildMap, propValue, false));
                    }
                }
            }

            if (!rootMapProps.Any() && !nestedMapTargets.Any())
            {
                throw new ArgumentException("The input model has no mapping defined on either root or nested layers.");
            }

            var distinctDatasources = new HashSet<int>();

            foreach (var rm in rootMapProps)
            {
                ExtractDatasourceIds(rm.Property.GetValue(source), distinctDatasources);
            }

            foreach (var target in nestedMapTargets)
            {
                if (target.IsCollection)
                {
                    foreach (var item in (IEnumerable)target.ActiveValue)
                    {
                        if (item == null) continue;
                        ExtractDatasourceIds(target.ChildMapProp.Property.GetValue(item), distinctDatasources);
                    }
                }
                else
                {
                    ExtractDatasourceIds(target.ChildMapProp.Property.GetValue(target.ActiveValue), distinctDatasources);
                }
            }

            if (!distinctDatasources.Any()) yield break;

            foreach (var dsId in distinctDatasources)
            {
                var rootCopy = ShallowCopy(source);

                foreach (var rm in rootMapProps)
                {
                    var rawVal = rm.Property.GetValue(rootCopy);
                    var filteredVal = FilterValueForDatasource(rawVal, dsId, rm.IsString, rm.IsStringEnumerable);
                    rm.Property.SetValue(rootCopy, filteredVal);
                }

                foreach (var target in nestedMapTargets)
                {
                    if (target.IsCollection)
                    {
                        var copiedCollection = target.CollectionOrClassProp.GetValue(rootCopy) as IList;
                        if (copiedCollection == null) continue;

                        for (int i = 0; i < copiedCollection.Count; i++)
                        {
                            var chunkItem = copiedCollection[i];
                            if (chunkItem == null) continue;

                            var rawVal = target.ChildMapProp.Property.GetValue(chunkItem);
                            var filteredVal = FilterValueForDatasource(rawVal, dsId, target.ChildMapProp.IsString, target.ChildMapProp.IsStringEnumerable);
                            target.ChildMapProp.Property.SetValue(chunkItem, filteredVal);
                        }
                    }
                    else
                    {
                        var copiedChildObject = target.CollectionOrClassProp.GetValue(rootCopy);
                        if (copiedChildObject == null) continue;

                        var rawVal = target.ChildMapProp.Property.GetValue(copiedChildObject);
                        var filteredVal = FilterValueForDatasource(rawVal, dsId, target.ChildMapProp.IsString, target.ChildMapProp.IsStringEnumerable);
                        target.ChildMapProp.Property.SetValue(copiedChildObject, filteredVal);
                    }
                }

                yield return new ConnectionContext<TRequest>
                {
                    DatasourceId = dsId,
                    Request = rootCopy
                };
            }
        }

        private static IEnumerable<ConnectionContext<TRequest>> HandleRequestContainer(RequestContainer container)
        {
            var mapValue = container.MapArgs;

            if (mapValue is string singleWebId)
            {
                // Decode a single WebId
                var info = WebIdEncoding.Decode(singleWebId);

                yield return new ConnectionContext<TRequest>
                {
                    DatasourceId = info.DatasourceId,
                    Request = (TRequest)(object)new RequestContainer
                    {
                        Args = container.Args,
                        MapArgs = info.WebId
                    }
                };
            }
            else if (mapValue is IEnumerable<string> multipleWebIds)
            {
                // Decode and group by datasource
                var decoded = multipleWebIds
                    .Select(WebIdEncoding.Decode)
                    .GroupBy(d => d.DatasourceId);

                foreach (var group in decoded)
                {
                    yield return new ConnectionContext<TRequest>
                    {
                        DatasourceId = group.Key,
                        Request = (TRequest)(object)new RequestContainer
                        {
                            Args = container.Args,
                            MapArgs = group.Select(x => x.WebId).ToList()
                        }
                    };
                }
            }
            else
            {
                throw new DCPBadRequestException(
                    $"RequestContainer.MapArgs must be string or IEnumerable<string>, but got {mapValue?.GetType().Name ?? "null"}"
                );
            }
        }

        private static void ExtractDatasourceIds(object? value, HashSet<int> targetSet)
        {
            if (value == null) return;

            if (value is string singleStr)
            {
                if (!string.IsNullOrEmpty(singleStr))
                {
                    var decoded = WebIdEncoding.Decode(singleStr);
                    if (decoded.DatasourceId > 0)
                    {
                        targetSet.Add(decoded.DatasourceId);
                    }
                }
            }
            else if (value is IEnumerable<string> strEnum)
            {
                foreach (var str in strEnum.Where(s => !string.IsNullOrEmpty(s)))
                {
                    var decoded = WebIdEncoding.Decode(str);
                    if (decoded.DatasourceId > 0)
                    {
                        targetSet.Add(decoded.DatasourceId);
                    }
                }
            }
        }

        private static object? FilterValueForDatasource(object? value, int activeDsId, bool isString, bool isEnumerable)
        {
            if (value == null) return null;

            if (isString && value is string singleStr)
            {
                if (string.IsNullOrEmpty(singleStr)) return string.Empty;
                var decoded = WebIdEncoding.Decode(singleStr);
                return decoded.DatasourceId == activeDsId ? decoded.WebId : string.Empty;
            }

            if (isEnumerable && value is IEnumerable<string> strEnum)
            {
                return strEnum
                    .Select(s => WebIdEncoding.Decode(s))
                    .Where(d => d.DatasourceId == activeDsId)
                    .Select(d => d.WebId)
                    .ToList();
            }

            return value;
        }

        private async Task<IEnumerable<ConnectionContext<TRequest>>> MapToAllAsync(TRequest request, int siteId, DataType dataType)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Load all datasource IDs from the cache or a configured list
            var connections = await _connectionStringLoader.Load(siteId, dataType);

            if (connections.Count == 0)
            {
                throw new DCPBadRequestException($"Site {siteId} does not have any connections for data type {dataType.ToName()}");
            }

            return connections.Select(x => new ConnectionContext<TRequest>
            {
                DatasourceId = x.DatasourceId,
                Request = ShallowCopy(request)
            }).ToList();
        }

        private static TRequest ShallowCopy(TRequest source)
        {
            if (source == null) return default!;

            var type = typeof(TRequest);
            var copy = Activator.CreateInstance<TRequest>();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                var value = prop.GetValue(source);
                if (value == null) continue;

                if (prop.PropertyType.IsGenericType && prop.PropertyType != typeof(string))
                {
                    var enumerableInterface = prop.PropertyType
                        .GetInterfaces()
                        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                        ?? (prop.PropertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? prop.PropertyType : null);

                    if (enumerableInterface != null)
                    {
                        var itemType = enumerableInterface.GetGenericArguments()[0];
                        var newList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;

                        foreach (var item in (IEnumerable)value)
                        {
                            if (item == null) continue;

                            if (itemType == typeof(string) || itemType.IsValueType)
                            {
                                newList.Add(item);
                            }
                            else
                            {
                                var itemCopy = Activator.CreateInstance(itemType)!;
                                foreach (var childProp in itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                                {
                                    if (childProp.CanRead && childProp.CanWrite)
                                    {
                                        childProp.SetValue(itemCopy, childProp.GetValue(item));
                                    }
                                }
                                newList.Add(itemCopy);
                            }
                        }

                        prop.SetValue(copy, newList);
                        continue;
                    }
                }

                prop.SetValue(copy, value);
            }

            return copy;
        }

        private async Task<TResponse> RunReduceInternalAsync<TInterface>(
            Func<TInterface, ConnectionContext<TRequest>, CancellationToken, Task<TResponse>> pluginActionAsync,
            IEnumerable<ConnectionContext<TRequest>> groups,
            CancellationToken cancellationToken = default)
            where TInterface : class
        {
            if (_transformFunction == null)
                throw new Exception("Transform function not defined");

            if (_mergeFunction == null)
                throw new Exception("Reduce function not defined");

            // Load connection strings
            _connectionStrings = await _connectionStringLoader.Load(groups.Select(x => x.DatasourceId).Distinct().ToList());

            var tasks = groups.Select(async group =>
            {
                var conn = _connectionStrings.First(x => x.DatasourceId == group.DatasourceId);

                var datasource = _cache.CreatePluginByConnectionType<TInterface>(
                    conn.ConnectionType,
                    _serviceProvider,
                    new object[] { conn.DatasourceId, conn.ConnectionString }
                );

                var response = new ConnectionContext<TResponse>
                {
                    DatasourceId = group.DatasourceId,
                    Request = await pluginActionAsync(datasource, group, cancellationToken)
                };

                return _transformFunction(response);
            });

            var results = await Task.WhenAll(tasks);
            return _mergeFunction(results);
        }

        private void LogToFile(string message)
        {
            if (!_debug)
                return;

            try
            {
                lock (FileLock)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    File.AppendAllText(LogFilePath, $"[{timestamp}] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }

    }
}
