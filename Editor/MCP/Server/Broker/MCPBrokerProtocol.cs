// Copyright (C) Funplay. Licensed under MIT.

namespace Funplay.Editor.MCP.Server
{
    internal static class MCPBrokerProtocol
    {
        public const int Version = 1;
        public const string Name = "funplay-unity-mcp-broker";
        public const string HealthPath = "/_funplay/broker/health";
        public const string AttachPath = "/_funplay/broker/attach";
        public const string PullPath = "/_funplay/broker/pull";
        public const string PushPath = "/_funplay/broker/push";
        public const string DetachPath = "/_funplay/broker/detach";
        public const string ShutdownPath = "/_funplay/broker/shutdown";
        public const string TokenHeader = "X-Funplay-Broker-Token";
        public const string SessionHeader = "X-Funplay-Broker-Session";
        public const string ReqIdHeader = "X-Funplay-Broker-ReqId";
        public const string RedeliveryHeader = "X-Funplay-Broker-Redelivery";
        public const string BrokerHeader = "X-Funplay-Broker";
    }
}
