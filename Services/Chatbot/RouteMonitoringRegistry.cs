using HcmcRainVision.Backend.Models.DTOs;
using HcmcRainVision.Backend.Models.Constants;
using System.Collections.Concurrent;

namespace HcmcRainVision.Backend.Services.Chatbot
{
    public class RouteMonitoringRegistry
    {
        private readonly ConcurrentDictionary<string, MonitoredRouteState> _routes = new();

        public string GetGroupName(string routeId) => $"{AppConstants.SignalRGroups.RouteGroupPrefix}{routeId}";

        public void Upsert(string routeId, string connectionId, List<RoutePointDto> routePoints, string? origin, string? destination)
        {
            _routes.AddOrUpdate(
                routeId,
                _ => new MonitoredRouteState(routeId, routePoints, origin, destination, connectionId),
                (_, existing) => existing.WithUpdate(routePoints, origin, destination, connectionId));
        }

        public bool RemoveConnectionFromRoute(string routeId, string connectionId)
        {
            if (!_routes.TryGetValue(routeId, out var state))
            {
                return false;
            }

            state.RemoveConnection(connectionId);
            if (state.ConnectionCount == 0)
            {
                _routes.TryRemove(routeId, out _);
            }

            return true;
        }

        public List<string> RemoveConnectionFromAllRoutes(string connectionId)
        {
            var removedRouteIds = new List<string>();

            foreach (var entry in _routes)
            {
                entry.Value.RemoveConnection(connectionId);
                if (entry.Value.ConnectionCount == 0)
                {
                    _routes.TryRemove(entry.Key, out _);
                    removedRouteIds.Add(entry.Key);
                }
            }

            return removedRouteIds;
        }

        public List<MonitoredRouteSnapshot> GetSnapshots()
        {
            return _routes.Values
                .Select(x => new MonitoredRouteSnapshot
                {
                    RouteId = x.RouteId,
                    RoutePoints = x.RoutePoints,
                    Origin = x.Origin,
                    Destination = x.Destination,
                    ConnectionCount = x.ConnectionCount,
                    UpdatedAtUtc = x.UpdatedAtUtc
                })
                .ToList();
        }

        private class MonitoredRouteState
        {
            private readonly object _sync = new();
            private readonly HashSet<string> _connectionIds;

            public string RouteId { get; }
            public List<RoutePointDto> RoutePoints { get; private set; }
            public string? Origin { get; private set; }
            public string? Destination { get; private set; }
            public DateTime UpdatedAtUtc { get; private set; }
            public int ConnectionCount
            {
                get
                {
                    lock (_sync)
                    {
                        return _connectionIds.Count;
                    }
                }
            }

            public MonitoredRouteState(string routeId, List<RoutePointDto> routePoints, string? origin, string? destination, string connectionId)
            {
                RouteId = routeId;
                RoutePoints = routePoints;
                Origin = origin;
                Destination = destination;
                UpdatedAtUtc = DateTime.UtcNow;
                _connectionIds = new HashSet<string>(StringComparer.Ordinal) { connectionId };
            }

            public MonitoredRouteState WithUpdate(List<RoutePointDto> routePoints, string? origin, string? destination, string connectionId)
            {
                lock (_sync)
                {
                    RoutePoints = routePoints;
                    Origin = origin;
                    Destination = destination;
                    UpdatedAtUtc = DateTime.UtcNow;
                    _connectionIds.Add(connectionId);
                    return this;
                }
            }

            public void RemoveConnection(string connectionId)
            {
                lock (_sync)
                {
                    _connectionIds.Remove(connectionId);
                    UpdatedAtUtc = DateTime.UtcNow;
                }
            }
        }

        public class MonitoredRouteSnapshot
        {
            public string RouteId { get; set; } = string.Empty;
            public List<RoutePointDto> RoutePoints { get; set; } = new();
            public string? Origin { get; set; }
            public string? Destination { get; set; }
            public int ConnectionCount { get; set; }
            public DateTime UpdatedAtUtc { get; set; }
        }
    }
}
