using System;
using System.Threading;
using UnityEngine;
using WebSocketSharp;

public class WebSocketSignaler
{
    private WebSocket ws;
    private string url;

    public event Action OnOpen;
    public event Action<string> OnNewPeer;
    public event Action<string, string> OnOffer;
    public event Action<string, string> OnAnswer;
    public event Action<string, string, string, int> OnIceCandidate;

    private SynchronizationContext context;

    public WebSocketSignaler(string url)
    {
        context = SynchronizationContext.Current;

        this.url = url;
    }

    public void Connect()
    {
        ws = new WebSocket(url);
        ws.OnOpen += Ws_OnOpen;
        ws.OnMessage += Ws_OnMessage;
        ws.OnClose += Ws_OnClose;
        ws.OnError += Ws_OnError;
        ws.Connect();
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
            try
            {
                var msg = JsonUtility.FromJson<SignalingMessage>(e.Data);
                Debug.Log($"=== WebSocket Signaling OnMessage > {msg.type}");
                switch (msg.type)
                {
                    case "new":
                        OnNewPeer?.Invoke(msg.src);
                        break;
                    case "offer":
                        OnOffer?.Invoke(msg.src, msg.sdp);
                        break;
                    case "answer":
                        OnAnswer?.Invoke(msg.src, msg.sdp);
                        break;
                    case "candidate":
                        OnIceCandidate?.Invoke(msg.src, msg.candidate, msg.sdpMid, msg.sdpMLineIndex);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.Message);
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
