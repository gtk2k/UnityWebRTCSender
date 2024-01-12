using System;
using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;

public class WebRTC_Quest : MonoBehaviour
{
    [SerializeField] private string signalingUrl;
    [SerializeField] private GameObject streamDisplay;
    [SerializeField] private Camera _streamCamera;

    private WebSocketSignalingClient signalingClient;
    private RTCPeerConnection peer = null;
    private static RenderTexture SendTexture;
    private List<RTCIceCandidate> candidatePool = new List<RTCIceCandidate>();
    private bool isCreateDesc;

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
        StartCoroutine(WebRTC.Update());

        isCreateDesc = false;

        SendTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.BGRA32, 0);

        signalingClient = new WebSocketSignalingClient(signalingUrl);
        signalingClient.OnOpen += SignalingClient_OnOpen;
        signalingClient.OnDesc += SignalingClient_OnDesc;
        signalingClient.onCand += SignalingClient_onCand;
        signalingClient.Connect();
    }
    private void Update()
    {
        if (SendTexture != null)
        {
            var prev = _streamCamera.targetTexture;
            _streamCamera.targetTexture = SendTexture;
            _streamCamera.Render();
            _streamCamera.targetTexture = prev;
        }
    }


    private void SignalingClient_OnOpen()
    {
        Debug.Log($"=== Quest SignalingClient_OnOpen");

        CreatePeer();
    }

    private void SignalingClient_OnDesc(RTCSessionDescription desc)
    {
        Debug.Log($"=== Quest SignalingClient_OnDesc: {desc.type}");
        Debug.Log(desc.sdp);

        StartCoroutine(SetDesc(Side.Remote, desc));
    }

    private void SignalingClient_onCand(RTCIceCandidate cand)
    {
        Debug.Log($"=== Quest SignalingClient_onCand");

        peer.AddIceCandidate(cand);
    }

    private RTCPeerConnection CreatePeer()
    {
        Debug.Log($"=== Quest CreatePeer 1");

        peer = new RTCPeerConnection(ref config);
        peer.OnIceCandidate = (cand) =>
        {
            Debug.Log($"=== Quest OnIceCandidate: {cand.Candidate}");

            if (isCreateDesc)
            {
                signalingClient.Send(SignalingMessage.FromCand(cand));
            }
            else
            {
                candidatePool.Add(cand);
            }
        };
        peer.OnIceGatheringStateChange = (state) =>
        {
            Debug.Log($"=== Quest OnIceGatheringStateChange > {state}");
        };
        peer.OnConnectionStateChange = (state) =>
        {
            Debug.Log($"=== Quest OnConnectionStateChange > {state}");
        };
        peer.OnTrack = (e) =>
        {
            if (e.Track is VideoStreamTrack track)
            {
                track.OnVideoReceived += tex =>
                {
                    streamDisplay.GetComponent<Renderer>().material.mainTexture = tex;
                };
            }
        };
        try
        {
            var videoTrack = new VideoStreamTrack(SendTexture);
            peer.AddTrack(videoTrack);
            StartCoroutine(CreateDesc(RTCSdpType.Offer));
        }
        catch (Exception ex)
        {
            Debug.LogError($"=== Quest CreatePeer Error: {ex.Message}");
        }
        return peer;
    }

    private IEnumerator SetDesc(Side side, RTCSessionDescription desc)
    {
        Debug.Log($"=== Quest SetDesc: {side}, {desc.type}");

        var op = side == Side.Local ? peer.SetLocalDescription(ref desc) : peer.SetRemoteDescription(ref desc);
        yield return op;
        if (op.IsError)
        {
            Debug.LogError($"Set {desc.type} Error: {op.Error.message}");
            yield break;
        }

        if (side == Side.Local)
        {
            isCreateDesc = true;
            signalingClient.Send(SignalingMessage.FromDesc(desc));
            foreach (var cand in candidatePool)
            {
                signalingClient.Send(SignalingMessage.FromCand(cand));
            }
            candidatePool.Clear();
        }
        else if (desc.type == RTCSdpType.Offer)
        {
            yield return StartCoroutine(CreateDesc(RTCSdpType.Answer));
        }
    }

    private IEnumerator CreateDesc(RTCSdpType type)
    {
        Debug.Log($"=== Quest CreateDesc: {type}");

        var op = type == RTCSdpType.Offer ? peer.CreateOffer() : peer.CreateAnswer();
        yield return op;
        if (op.IsError)
        {
            Debug.LogError($"Create {type} Error: {op.Error.message}");
        }
        var desc = op.Desc;
        yield return StartCoroutine(SetDesc(Side.Local, desc));
    }
}
