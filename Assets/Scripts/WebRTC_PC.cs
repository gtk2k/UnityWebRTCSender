using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;

public class WebRTC_PC : MonoBehaviour
{
    [SerializeField] private int signalingServerPort;
    [SerializeField] private GameObject streamDisplay;
    [SerializeField] private Camera _streamCamera;

    private WebSocketSignalingServer signalingServer;
    private Dictionary<string, RTCPeerConnection> peers = new Dictionary<string, RTCPeerConnection>();
    private Dictionary<string, bool> isSendDesc = new Dictionary<string, bool>();
    private Dictionary<string, List<RTCIceCandidate>> candidatePool = new Dictionary<string, List<RTCIceCandidate>>();
    private RenderTexture SendTexture;
    
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

    private void OnEnable()
    {
        Debug.Log($"=== WebRTC_PC Start");

        StartCoroutine(WebRTC.Update());

        SendTexture = new RenderTexture(1920,1024, 24, RenderTextureFormat.BGRA32, 0);

        var cb = SendTexture.colorBuffer;

        signalingServer = new WebSocketSignalingServer(signalingServerPort);
        signalingServer.OnWSClientConnect += SignalingServer_OnWSClientConnect;
        signalingServer.OnDesc += SignalingServer_OnDesc;
        signalingServer.OnCand += SignalingServer_OnCand;
        signalingServer.Start();
    }

    private void OnDisable()
    {
        Debug.Log($"=== WebRTC_PC Stop");

        signalingServer?.Stop();
        signalingServer = null;
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


    private void SignalingServer_OnWSClientConnect(string id)
    {
        Debug.Log($"=== SignalingServer_OnWSClientConnect: {id}");

        if (!peers.ContainsKey(id))
        {
            isSendDesc.Add(id, false);
            candidatePool.Add(id, new List<RTCIceCandidate>());
            var peer = CreatePeer(id);
            peers.Add(id, peer);
            //StartCoroutine(CreateDesc(id, RTCSdpType.Offer));
        }
    }

    private RTCPeerConnection CreatePeer(string id)
    {
        Debug.Log($"=== PC CreatePeer [{id}]");

        var peer = new RTCPeerConnection(ref config);
        peer.OnIceCandidate = (cand) =>
        {
            Debug.Log($"=== PC OnIceCandidate [{id}]: {cand.Candidate}");
            if (isSendDesc[id])
            {
                signalingServer.Send( id, SignalingMessage.FromCand(cand));
            }
            else
            {
                candidatePool[id].Add(cand);
            }
        };
        peer.OnIceGatheringStateChange = (state) =>
        {
            Debug.Log($"PC OnIceGatheringStateChange [{id}], {state}");
        };
        peer.OnConnectionStateChange = (state) =>
        {
            Debug.Log($"PC OnConnectionStateChange [{id}]: {state}");
        };
        peer.OnTrack = (e) =>
        {
            Debug.Log($"PC OnTrack");
            if (e.Track is VideoStreamTrack track)
            {
                Debug.Log($"PC OnVideoStreamTrack");
                track.OnVideoReceived += tex =>
                {
                    Debug.Log($"PC OnVideoReceived");
                    streamDisplay.GetComponent<Renderer>().material.mainTexture = tex;
                };
            }
        };
        var videoTrack = new VideoStreamTrack(SendTexture);
        var sender = peer.AddTrack(videoTrack);

        return peer;
    }

    private void SignalingServer_OnDesc(string id, RTCSessionDescription desc)
    {
        Debug.Log($"=== PC SignalingServer_OnDesc [{id}]: {desc.type}");
        Debug.Log(desc.sdp);
        StartCoroutine(SetDesc(id, Side.Remote, desc));
    }

    private void SignalingServer_OnCand(string id, RTCIceCandidate cand)
    {
        Debug.Log($"=== PC SignalingServer_OnCand [{id}]: {cand.Candidate}");

        peers[id].AddIceCandidate(cand);
    }

    private IEnumerator CreateDesc(string id, RTCSdpType type)
    {
        Debug.Log($"=== PC CreateDesc [{id}]: {type}");

        var peer = peers[id];
        var op = type == RTCSdpType.Offer ? peer.CreateOffer() : peer.CreateAnswer();
        yield return op;
        if (op.IsError)
        {
            Debug.LogError($"CreateDesc Error [{id}]: {op.Error.message}");
            yield break;
        }
        var desc = op.Desc;
        yield return StartCoroutine(SetDesc(id, Side.Local, desc));
    }

    private IEnumerator SetDesc(string id, Side side, RTCSessionDescription desc)
    {
        Debug.Log($"=== PC SetDesc [{id}]: {side}. {desc.type}");

        var peer = peers[id];
        var op = side == Side.Local ? peer.SetLocalDescription(ref desc): peer.SetRemoteDescription(ref desc);
        yield return op;
        if (op.IsError)
        {
            Debug.LogError($"PC Set {side} {desc.type} Error [{id}]: {op.Error.message}");
            yield break;
        }

        if(side == Side.Local)
        {
            isSendDesc[id] = true;
            Debug.Log($"PC Send {desc.type}");
            signalingServer.Send(id, SignalingMessage.FromDesc(desc));
            foreach(var cand in candidatePool[id])
            {
                Debug.Log($"PC Send cand");
                signalingServer.Send(id, SignalingMessage.FromCand(cand));
            }
            candidatePool[id].Clear();
        }
        else if(desc.type == RTCSdpType.Offer)
        {
            yield return StartCoroutine(CreateDesc(id, RTCSdpType.Answer));
        }
    }
}
