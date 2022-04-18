using System;
using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;

public class WebRTCManager : MonoBehaviour
{
    [SerializeField] private string signalingUrl;
    [SerializeField] private RawImage preview;
    private WebSocketSignaler signaler;
    //private Dictionary<string, RTCPeerConnection> peers = new Dictionary<string, RTCPeerConnection>();
    private RTCPeerConnection peer;
    private string myPeerId;
    private MediaStream sendStream;
    private static RenderTexture SendTexture;
    private bool webrtcInitialized;
    private bool sendSessionDescfription;
    private List<RTCIceCandidate> candidatePool = new List<RTCIceCandidate>();


    private enum Side
    {
        Local,
        Remote,
    }

    public enum PeerType
    {
        Sender,
        Receiver
    }

    private RTCConfiguration config = new RTCConfiguration
    {
        iceServers = new[]
        {
            new RTCIceServer
            {
                urls = new []{"stun:stun.l.google.com:19302"}
            }
        }
    };

    private void Start()
    {
        Debug.Log($"=== WebRTCManager Start()");
        try
        {
            if (SendTexture == null)
            {
                SendTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.BGRA32, 0);
                SendTexture.Create();
                preview.texture = SendTexture;
            }
            myPeerId = Guid.NewGuid().ToString("N");
            signaler = new WebSocketSignaler(signalingUrl);
            signaler.OnOpen += Signaler_OnOpen;
            signaler.OnNewPeer += Signaler_OnNewPeer;
            signaler.OnOffer += Signaler_OnOffer;
            signaler.OnAnswer += Signaler_OnAnswer;
            signaler.OnIceCandidate += Signaler_OnIceCandidate;
            signaler.Connect();
        }
        catch (Exception ex)
        {
            Debug.LogError(ex.Message);
        }
    }

    private void Update()
    {
        if (SendTexture != null)
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(SendTexture);
        }
    }

    private void Signaler_OnOpen()
    {
        Debug.Log($"=== WebRTCManager Signaler_OnOpen()");
        SendSignalingMessage(type: "new");
    }

    private void Signaler_OnNewPeer(string peerId)
    {
        Debug.Log($"=== WebRTCManager Signaler_OnNewPeer() > peerId: {peerId}");
        CreatePeer(peerId);
        StartCoroutine(CreateSessionDescription(peerId, RTCSdpType.Offer));
    }

    private void Signaler_OnOffer(string peerId, string sdp)
    {
        Debug.Log($"=== WebRTCManager Signaler_OnOffer() > peerId: {peerId}");
        StartCoroutine(SetSessionDescription(peerId, Side.Remote, RTCSdpType.Offer, sdp));
    }

    private void Signaler_OnAnswer(string peerId, string sdp)
    {
        Debug.Log($"=== WebRTCManager Signaler_OnAnswer() > peerId: {peerId}");
        StartCoroutine(SetSessionDescription(peerId, Side.Remote, RTCSdpType.Answer, sdp));
    }

    public RTCPeerConnection CreatePeer(string peerId)
    {
        Debug.Log($"=== WebRTCManager CreatePeer() > peerId: {peerId}");
        if (!webrtcInitialized)
        {
            webrtcInitialized = true;
            WebRTC.Initialize(EncoderType.Software);
        }
        peer = new RTCPeerConnection(ref config);
        peer.OnIceCandidate = (iceCandidate) =>
        {
            if (sendSessionDescfription)
            {
                SendSignalingMessage(
                    dst: peerId,
                    type: "candidate",
                    candidate: iceCandidate.Candidate,
                    sdpMid: iceCandidate.SdpMid,
                    sdpMLineIndex: iceCandidate.SdpMLineIndex.Value);
            }
            else
            {
                candidatePool.Add(iceCandidate);
            }
        };
        peer.OnIceGatheringStateChange = (state) =>
        {
            Debug.Log($"OnIceGatheringStateChange > {state}");
        };
        peer.OnConnectionStateChange = (state) =>
        {
            Debug.Log($"OnConnectionStateChange > {state}");
        };
        if (sendStream == null)
        {
            sendStream = new MediaStream();
        }
        var videoTrack = new VideoStreamTrack(SendTexture);
        peer.AddTrack(videoTrack, sendStream);
        StartCoroutine(WebRTC.Update());
        return peer;
    }

    private void Signaler_OnIceCandidate(string peerId, string candidate, string sdpMid, int sdpMLineIndex)
    {
        Debug.Log($"=== WebRTCManager Signaler_OnIceCandidate() > peerId: {peerId}");
        var iceCandidate = new RTCIceCandidate(new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid,
            sdpMLineIndex = sdpMLineIndex
        });
        Debug.Log($"AddIceCandidate");
        peer.AddIceCandidate(iceCandidate);
    }

    private IEnumerator CreateSessionDescription(string peerId, RTCSdpType type)
    {
        Debug.Log($"=== WebRTCManager CreateSessionDescription() > peerId: {peerId}, type:{type}");
        var op = type == RTCSdpType.Offer ? peer.CreateOffer() : peer.CreateAnswer();
        yield return op;
        if (op.IsError)
        {
            Debug.LogError($"Set {type} Error > {op.Error.message}");
            yield break;
        }
        yield return StartCoroutine(SetSessionDescription(peerId, Side.Local, op.Desc));
    }

    private IEnumerator SetSessionDescription(string peerId, Side side, RTCSdpType type, string sdp)
    {
        Debug.Log($"=== WebRTCManager SetSessionDescription() > peerId: {peerId}, side: {side}, type:{type}");
        var desc = new RTCSessionDescription
        {
            type = type,
            sdp = sdp
        };
        yield return StartCoroutine(SetSessionDescription(peerId, side, desc));
    }

    private IEnumerator SetSessionDescription(string peerId, Side side, RTCSessionDescription desc)
    {
        Debug.Log($"=== WebRTCManager SetSessionDescription() >  peerId: {peerId}, side: {side}, type:{desc.type}");
        var op = side == Side.Local ? peer.SetLocalDescription(ref desc) : peer.SetRemoteDescription(ref desc);
        if (op.IsError)
        {
            Debug.LogError($"Set {side} {desc.type} > {op.Error.message}");
            yield break;
        }
        if (side == Side.Local)
        {
            var msg = new SignalingMessage
            {
                dst = peerId,
                type = desc.type.ToString().ToLower(),
                sdp = desc.sdp
            };
            SendSignalingMessage(type: desc.type.ToString().ToLower(), sdp: desc.sdp);
            sendSessionDescfription = true;
            foreach(var c in candidatePool)
            {
                SendSignalingMessage(type: "candidate", candidate: c.Candidate, sdpMid: c.SdpMid, sdpMLineIndex: c.SdpMLineIndex.Value);
            }
            candidatePool.Clear();
        }
        else if (desc.type == RTCSdpType.Offer)
        {
            StartCoroutine(CreateSessionDescription(peerId, RTCSdpType.Answer));
        }
    }

    private void SendSignalingMessage(
        string dst = null,
        string type = null,
        string sdp = null,
        string candidate = null,
        string sdpMid = null,
        int sdpMLineIndex = 0)
    {
        try
        {
            Debug.Log($"=== WebRTCManager SendSignalingMessage() > dst: {dst}, type: {type}");
            var msg = new SignalingMessage
            {
                src = myPeerId,
                peerType = "sender",
                dst = dst,
                type = type,
                sdp = sdp,
                candidate = candidate,
                sdpMid = sdpMid,
                sdpMLineIndex = sdpMLineIndex
            };
            var json = JsonUtility.ToJson(msg);
            signaler.Send(msg);
        }
        catch (Exception ex)
        {
            Debug.LogError(ex.Message);
        }
    }
}
