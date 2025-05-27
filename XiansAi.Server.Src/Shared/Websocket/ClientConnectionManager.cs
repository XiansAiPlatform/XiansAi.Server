using System.Collections.Concurrent;

namespace XiansAi.Server.Shared.Websocket
{
    public class ClientConnectionManager
    {
        private readonly ConcurrentDictionary<string, string> _threadConnections = new(); // threadId -> connectionId
        private readonly ConcurrentDictionary<string, string> _connectionThreads = new(); // connectionId -> threadId

        public void AddConnection(string threadId, string connectionId)
        {
            _threadConnections[threadId] = connectionId;
            _connectionThreads[connectionId] = threadId;
        }

        public void RemoveConnection(string connectionId)
        {
            if (_connectionThreads.TryRemove(connectionId, out var threadId))
            {
                _threadConnections.TryRemove(threadId, out _);
            }
        }

        public string? GetConnectionId(string? threadId)
        {
            if (threadId == null)
            {
                Console.WriteLine("threadId is null");
                return null;
            }
            else { 
                _threadConnections.TryGetValue(threadId, out var connectionId);
                return connectionId;
            }
        }
    }
}
