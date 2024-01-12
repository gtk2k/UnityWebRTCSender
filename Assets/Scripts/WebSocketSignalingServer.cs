using System;
using System.Collections.Generic;
using System.Threading;
using Unity.WebRTC;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;

public class WebSocketSignalingServer
{
    public event Action<string> OnWSClientConnect;
    public event Action<string, RTCSessionDescription> OnDesc;
    public event Action<string, RTCIceCandidate> OnCand;

    private WebSocketServer wss;
    private int port;

    public event Action OnOpen;
    public event Action<string> OnNewPeer;
    public event Action<string, string> OnOffer;
    public event Action<string, string> OnAnswer;
    public event Action<string, string, string, int> OnIceCandidate;

    private SynchronizationContext context;

    private Dictionary<string, WebSocketBehavior> websocketClients = new Dictionary<string, WebSocketBehavior>();

    private class SignalingBehaviour: WebSocketBehavior
    {
        public event Action<string> OnWSOpen;
        public event Action<string, string> OnWSMessage;
        public event Action<string, ushort, string> OnWSClose;
        public event Action<string, string> OnWSError;

        protected override void OnOpen()
        {
            OnWSOpen?.Invoke(ID);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            OnWSMessage?.Invoke(ID, e.Data);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            OnWSClose?.Invoke(ID, e.Code, e.Reason);
        }

        protected override void OnError(ErrorEventArgs e)
        {
            OnWSError?.Invoke(ID, e.Message);
        }
    }

    public WebSocketSignalingServer(int port)
    {
        context = SynchronizationContext.Current;

        this.port = port;
    }

    public void Start()
    {
        context = SynchronizationContext.Current;

        Debug.Log($"=== WebSocketSignalingServer Start: Port:{this.port}");
        wss = new WebSocketServer(this.port);
        wss.AddWebSocketService<SignalingBehaviour>("/", (behaviour) =>
        {
            behaviour.OnWSOpen += Behaviour_OnWSOpen;
            behaviour.OnWSMessage += Behaviour_OnWSMessage;
            behaviour.OnWSClose += Behaviour_OnWSClose;
            behaviour.OnWSError += Behaviour_OnWSError;
        });
        wss.Start();
    }

    public void Stop()
    {
        wss?.Stop();
        wss = null;
    }

    private void Behaviour_OnWSOpen(string id)
    {
        context.Post(_ =>
        {
            Debug.Log($"WebSocket Client [{id}] Cnnected");
            OnWSClientConnect.Invoke(id);
        }, null);
    }

    private void Behaviour_OnWSMessage(string id, string data)
    {
        context.Post(_ =>
        {
            var msg = JsonUtility.FromJson<SignalingMessage>(data);
            switch (msg.type)
            {
                case "connect":
                    OnWSClientConnect?.Invoke(id);
                    break;
                case "offer":
                case "answer":
                    var desc = msg.ToDesc();
                    OnDesc?.Invoke(id, desc);
                    break;
                case "candidate":
                    var cand = msg.ToCand();
                    OnCand?.Invoke(id, cand);
                    break;
            }
        }, null);
    }

    private void Behaviour_OnWSClose(string id, ushort code, string reason)
    {
        context.Post(_ =>
        {
            Debug.Log($"WebSocket Client [{id}] Closed");
        }, null);
    }

    private void Behaviour_OnWSError(string id, string errorMessage)
    {
        context.Post(_ =>
        {
            Debug.Log($"WebSocket Client [{id}] Error: {errorMessage}");
        }, null);
    }

    public void Send(string id, SignalingMessage msg)
    {
        var json = JsonUtility.ToJson(msg);
        wss.WebSocketServices["/"].Sessions[id].WebSocket.Send(json);
    }
}
