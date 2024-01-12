using System;
using System.Threading;
using Unity.WebRTC;
using UnityEngine;
using WebSocketSharp;

public class WebSocketSignalingClient
{
    public event Action OnOpen;
    public event Action<RTCSessionDescription> OnDesc;
    public event Action<RTCIceCandidate> onCand;

    private WebSocket ws;
    private string url;

    private SynchronizationContext context;

    public WebSocketSignalingClient(string url)
    {
        context = SynchronizationContext.Current;

        this.url = url;
    }

    public void Connect()
    {
        try
        {
            ws = new WebSocket(url);
            ws.OnOpen += Ws_OnOpen;
            ws.OnMessage += Ws_OnMessage;
            ws.OnClose += Ws_OnClose;
            ws.OnError += Ws_OnError;
            ws.Connect();
        }
        catch (Exception ex)
        {
            Debug.LogError($"=== WebSocketClient Start Error: {ex.Message}");
        }
    }

    private void Ws_OnOpen(object sender, EventArgs e)
    {
        context.Post(_ =>
        {
            Debug.Log($"=== Ws_OnOpen()");
            OnOpen?.Invoke();
        }, null);
    }

    private void Ws_OnMessage(object sender, MessageEventArgs e)
    {
        context.Post(_ =>
        {
            var msg = JsonUtility.FromJson<SignalingMessage>(e.Data);
            switch (msg.type)
            {
                case "offer":
                case "answer":
                    OnDesc?.Invoke(msg.ToDesc());
                    break;
                case "candidate":
                    onCand?.Invoke(msg.ToCand());
                    break;
            }
        }, null);
    }

    private void Ws_OnClose(object sender, CloseEventArgs e)
    {
        context.Post(_ =>
        {
            Debug.Log($"=== WebSocket Signaling OnClose > code: {e.Code}, reason: {e.Reason}");
        }, null);
    }

    private void Ws_OnError(object sender, ErrorEventArgs e)
    {
        context.Post(_ =>
        {
            Debug.LogError($"=== WebSocket Signaling Error > {e.Exception.Message}");
        }, null);
    }

    public void Send(SignalingMessage msg)
    {
        var json = JsonUtility.ToJson(msg);
        ws.Send(json);
    }

    public void Disconnect()
    {
        ws?.Close();
        ws = null;
    }
}
