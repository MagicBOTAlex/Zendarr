using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Messaging;
using NzbDrone.Common.Reflection;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists;

namespace NzbDrone.Core.Messaging.Events
{
    public class EventAggregator : IEventAggregator
    {
        private readonly Logger _logger;
        private readonly IServiceFactory _serviceFactory;
        private readonly IRuntimeInfo _runtimeInfo;
        private readonly TaskFactory _taskFactory;
        private readonly Dictionary<string, object> _eventSubscribers;

        private class EventSubscribers<TEvent>
            where TEvent : class, IEvent
        {
            public IHandle<TEvent>[] _syncHandlers;
            public IHandleAsync<TEvent>[] _asyncHandlers;
            public IHandleAsync<IEvent>[] _globalHandlers;

            public EventSubscribers(IServiceFactory serviceFactory)
            {
                _syncHandlers = serviceFactory.BuildAll<IHandle<TEvent>>()
                                              .OrderBy(GetEventHandleOrder)
                                              .ToArray();

                _globalHandlers = serviceFactory.BuildAll<IHandleAsync<IEvent>>()
                                              .ToArray();

                _asyncHandlers = serviceFactory.BuildAll<IHandleAsync<TEvent>>()
                                               .ToArray();
            }
        }

        public EventAggregator(Logger logger, IServiceFactory serviceFactory, IRuntimeInfo runtimeInfo)
        {
            _logger = logger;
            _serviceFactory = serviceFactory;
            _runtimeInfo = runtimeInfo;
            _taskFactory = new TaskFactory();
            _eventSubscribers = new Dictionary<string, object>();
        }

        public void PublishEvent<TEvent>(TEvent @event)
            where TEvent : class, IEvent
        {
            Ensure.That(@event, () => @event).IsNotNull();

            var eventName = GetEventName(@event.GetType());

            if (_runtimeInfo.IsExiting && @event.GetType().HasAttribute<LifecycleEventAttribute>())
            {
                _logger.Warn("Event {0} blocked due to application shutdown", eventName);
                return;
            }

            /*
                        int workerThreads;
                        int completionPortThreads;
                        ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);

                        int maxCompletionPortThreads;
                        int maxWorkerThreads;
                        ThreadPool.GetMaxThreads(out maxWorkerThreads, out maxCompletionPortThreads);


                        int minCompletionPortThreads;
                        int minWorkerThreads;
                        ThreadPool.GetMinThreads(out minWorkerThreads, out minCompletionPortThreads);

                        _logger.Warn("Thread pool state WT:{0} PT:{1}  MAXWT:{2} MAXPT:{3} MINWT:{4} MINPT:{5}", workerThreads, completionPortThreads, maxWorkerThreads, maxCompletionPortThreads, minWorkerThreads, minCompletionPortThreads);
            */

            _logger.Trace("Publishing {0}", eventName);

            EventSubscribers<TEvent> subscribers;
            lock (_eventSubscribers)
            {
                if (!_eventSubscribers.TryGetValue(eventName, out var target))
                {
                    try
                    {
                        _eventSubscribers[eventName] = target = new EventSubscribers<TEvent>(_serviceFactory);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Unable to resolve event subscribers for {0}, container may be disposed", eventName);
                        if (@event is DownloadFailedEvent)
                        {
                            throw;
                        }

                        return;
                    }
                }

                subscribers = target as EventSubscribers<TEvent>;
            }

            // These two projections must be committed together before any side effects.
            if (@event is DownloadFailedEvent failed)
            {
                PersistFailedHistory(failed);
            }

            // call synchronous handlers first.
            var handlers = subscribers._syncHandlers;
            foreach (var handler in handlers)
            {
                if (@event is DownloadFailedEvent && (handler is HistoryService || handler is DownloadHistoryService))
                {
                    continue;
                }

                try
                {
                    _logger.Trace("{0} -> {1}", eventName, handler.GetType().Name);
                    handler.Handle(@event);
                    _logger.Trace("{0} <- {1}", eventName, handler.GetType().Name);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "{0} failed while processing [{1}]", handler.GetType().Name, eventName);
                }
            }

            foreach (var handler in subscribers._globalHandlers)
            {
                var handlerLocal = handler;

                _taskFactory.StartNew(() =>
                {
                    handlerLocal.HandleAsync(@event);
                },
                        TaskCreationOptions.PreferFairness)
                .LogExceptions();
            }

            foreach (var handler in subscribers._asyncHandlers)
            {
                var handlerLocal = handler;

                _taskFactory.StartNew(() =>
                {
                    _logger.Trace("{0} ~> {1}", eventName, handlerLocal.GetType().Name);
                    handlerLocal.HandleAsync(@event);
                    _logger.Trace("{0} <~ {1}", eventName, handlerLocal.GetType().Name);
                },
                        TaskCreationOptions.PreferFairness)
                .LogExceptions();
            }
        }

        private void PersistFailedHistory(DownloadFailedEvent message)
        {
            // DownloadHistory requires an ID; do not leave a one-sided failure for
            // legacy grabs that cannot be correlated to a download.
            if (string.IsNullOrWhiteSpace(message.DownloadId))
            {
                throw new InvalidOperationException("Unable to mark download as failed without a download ID");
            }

            if (message.MovieId == 0 && (message.EpisodeIds == null || message.EpisodeIds.Count == 0))
            {
                throw new InvalidOperationException("Unable to mark download as failed without an episode or movie");
            }

            using var connection = _serviceFactory.Build<IMainDatabase>().OpenConnection();
            using var transaction = connection.BeginTransaction();
            var grab = connection.QueryFirstOrDefault<DownloadHistory>(
                "SELECT * FROM \"DownloadHistory\" WHERE \"DownloadId\" = @DownloadId AND \"EventType\" = @EventType ORDER BY \"Date\" DESC LIMIT 1",
                new { message.DownloadId, EventType = DownloadHistoryEventType.DownloadGrabbed },
                transaction);
            var tracked = message.TrackedDownload;
            var data = new Dictionary<string, string>
            {
                ["DownloadClient"] = message.DownloadClient,
                ["DownloadClientName"] = tracked?.DownloadItem.DownloadClientInfo.Name ?? grab?.Data?.GetValueOrDefault("downloadClientName"),
                ["Message"] = message.Message,
                ["Source"] = message.Source,
                ["ReleaseGroup"] = tracked?.RemoteEpisode?.ParsedEpisodeInfo?.ReleaseGroup ?? tracked?.RemoteMovie?.ParsedMovieInfo?.ReleaseGroup ?? message.Data.GetValueOrDefault("releaseGroup"),
                ["Size"] = tracked?.DownloadItem.TotalSize.ToString() ?? message.Data.GetValueOrDefault("size"),
                ["Indexer"] = tracked?.RemoteEpisode?.Release?.Indexer ?? tracked?.RemoteMovie?.Release?.Indexer ?? message.Data.GetValueOrDefault("indexer")
            };

            var date = DateTime.UtcNow;
            var historySql = "INSERT INTO \"History\" (\"SeriesId\", \"EpisodeId\", \"MovieId\", \"MediaType\", \"EventType\", \"Date\", \"Quality\", \"Languages\", \"SourceTitle\", \"DownloadId\", \"Data\") VALUES (@SeriesId, @EpisodeId, @MovieId, @MediaType, @EventType, @Date, @Quality, @Languages, @SourceTitle, @DownloadId, @Data)";
            foreach (var episodeId in message.MovieId > 0 ? new[] { 0 }.AsEnumerable() : message.EpisodeIds.AsEnumerable())
            {
                connection.Execute(
                    historySql,
                    new
                    {
                        message.SeriesId,
                        EpisodeId = episodeId,
                        message.MovieId,
                        MediaType = message.MovieId > 0 ? MediaType.Movie : MediaType.Series,
                        EventType = message.MovieId > 0 ? (int)MovieHistoryEventType.DownloadFailed : (int)EpisodeHistoryEventType.DownloadFailed,
                        Date = date,
                        message.Quality,
                        message.Languages,
                        message.SourceTitle,
                        message.DownloadId,
                        Data = data
                    },
                    transaction);
            }

            var downloadHistory = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadFailed,
                SeriesId = message.SeriesId,
                MovieId = message.MovieId,
                DownloadId = message.DownloadId,
                SourceTitle = message.SourceTitle,
                Date = date,
                Protocol = tracked?.Protocol ?? grab?.Protocol ?? default,
                DownloadClientId = tracked?.DownloadClient ?? grab?.DownloadClientId ?? 0,
                Data = new Dictionary<string, string>
                {
                    ["DownloadClient"] = tracked?.DownloadItem.DownloadClientInfo.Type ?? message.DownloadClient,
                    ["DownloadClientName"] = data["DownloadClientName"]
                }
            };
            connection.Execute("INSERT INTO \"DownloadHistory\" (\"EventType\", \"SeriesId\", \"MovieId\", \"DownloadId\", \"SourceTitle\", \"Date\", \"Protocol\", \"IndexerId\", \"DownloadClientId\", \"Release\", \"Data\") VALUES (@EventType, @SeriesId, @MovieId, @DownloadId, @SourceTitle, @Date, @Protocol, @IndexerId, @DownloadClientId, @Release, @Data)", downloadHistory, transaction);
            transaction.Commit();
        }

        private static string GetEventName(Type eventType)
        {
            if (!eventType.IsGenericType)
            {
                return eventType.Name;
            }

            return string.Format("{0}<{1}>", eventType.Name.Remove(eventType.Name.IndexOf('`')), eventType.GetGenericArguments()[0].Name);
        }

        internal static int GetEventHandleOrder<TEvent>(IHandle<TEvent> eventHandler)
            where TEvent : class, IEvent
        {
            var method = eventHandler.GetType().GetMethod(nameof(eventHandler.Handle), new Type[] { typeof(TEvent) });

            if (method == null)
            {
                return (int)EventHandleOrder.Any;
            }

            var attribute = method.GetCustomAttributes(typeof(EventHandleOrderAttribute), true).FirstOrDefault() as EventHandleOrderAttribute;

            if (attribute == null)
            {
                return (int)EventHandleOrder.Any;
            }

            return (int)attribute.EventHandleOrder;
        }
    }
}
