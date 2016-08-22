﻿using System;
using XNet.Libs.Net;
using Proto;
using XNet.Libs.Utility;
using ServerUtility;
using System.Linq;
using org.vxwo.csharp.json;

namespace MapServer
{
    public class Appliaction
    {
        public const int SERVER_TICK = 50;
        
        int port;
        int ServicePort;
        string ServiceHost;
        string configRoot;
        string ServerHost;
        public int ServerID { set; get; }
        private MonitorPool pool;


        internal Client GetClientByID(int clientID)
        {
            return this.ListenServer.CurrentConnectionManager.GetClientByID(clientID);
        }

        internal Client GetClientByUserID(long userID)
        {
            Client res =null;
            this.ListenServer.CurrentConnectionManager.Each((obj) => {
                if ((long)obj.UserState == userID)
                {
                    res = obj;
                    return true;
                }
                return false;
            });
            return res;
        }

        public static Appliaction Current { private set; get; }

        public SocketServer ListenServer { private set; get; }

        /// <summary>
        /// Center server /login server
        /// </summary>
        /// <value>The client.</value>
        public RequestClient Client { private set; get; }

        public volatile bool IsRunning;

        int MaxBattleCount;

        public SyncDictionary<int, RequestClient> GateServerClients { private set; get; }


        public Appliaction(JsonValue config)
        {
            this.configRoot = config["ConfigRoot"].AsString();
            this.port = config["Port"].AsInt();
            this.ServicePort = config["LoginServerProt"].AsInt();
            this.ServiceHost = config["LoginServerHost"].AsString();
            ServerHost = config["ServiceHost"].AsString();
            MaxBattleCount = config["MaxBattle"].AsInt();
            NetProtoTool.EnableLog = config["Log"].AsBoolean();
            Current = this;
            pool = new MonitorPool();
            pool.Init(this.GetType().Assembly);
            GateServerClients  = new SyncDictionary<int, RequestClient>();
        }


        public void TryConnectUserServer(PlayerServerInfo player)
        {
            if (GateServerClients.HaveKey(player.ServerID)) return;
            var client = new RequestClient(player.ServiceHost, player.ServicePort);
            client.UseSendThreadUpdate = true;
            client.Connect();
            GateServerClients.Add(player.ServerID, client);
        }

        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;

            ResourcesLoader.Singleton.LoadAllConfig(configRoot);

           
            var listenHandler = new RequestHandle();
            listenHandler.RegAssembly(this.GetType().Assembly);
            ListenServer = new SocketServer(new ConnectionManager(), port);
            ListenServer.HandlerManager = listenHandler;
            ListenServer.Start();
            Client = new RequestClient(ServiceHost, ServicePort);
            Client.RegAssembly(this.GetType().Assembly);
            Client.UseSendThreadUpdate = true;
            Client.OnConnectCompleted = (s, e) =>
            {
                if (e.Success)
                {
                    
                    var request = Client.CreateRequest<B2L_RegBattleServer, L2B_RegBattleServer>();
                    request.RequestMessage.MaxBattleCount = MaxBattleCount;
                    request.RequestMessage.ServiceHost =  ServerHost;
                    request.RequestMessage.ServicePort = this.port;
                    request.RequestMessage.Version = ProtoTool.GetVersion();
                    request.OnCompleted = (success, r) =>
                    {
                        if (success && r.Code == ErrorCode.OK)
                        {
                            ServerID = r.ServiceServerID;
                            Debuger.Log("Server Reg Success!");
                        }
                    };
                    request.SendRequestSync();
                }
                else
                {
                    Debuger.Log("Can't connect LoginServer!");
                    Stop();
                }
            };
            Client.OnDisconnect = (s, e) => 
            {
                Debuger.Log("Can't connect LoginServer!");
                Stop();
            };
            Client.Connect();
            pool.Start();
        }

        public void Stop()
        {
            if (!IsRunning) 
                return;
            pool.Exit();
            IsRunning = false;
            ListenServer.Stop();
            Client.Disconnect();
        }

        public void Tick()
        {
            pool.Tick();
        }

        public RequestClient GetGateServer(int serverID)
        {
            RequestClient client;
            GateServerClients.TryToGetValue(serverID, out client);
            return client;
        }
    }
}

