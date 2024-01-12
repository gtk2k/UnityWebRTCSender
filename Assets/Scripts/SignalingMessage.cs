using Unity.WebRTC;

public class SignalingMessage
{
    public string type;
    public string sdp;
    public string candidate;
    public string sdpMid;
    public int? sdpMLineIndex;

    public RTCSessionDescription ToDesc()
    {
        return new RTCSessionDescription
        {
            type = type == "offer" ? RTCSdpType.Offer : RTCSdpType.Answer,
            sdp = sdp
        };
    }

    public RTCIceCandidate ToCand()
    {
        return new RTCIceCandidate(new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid,
            sdpMLineIndex = sdpMLineIndex
        });
    }

    public static SignalingMessage FromDesc(RTCSessionDescription desc)
    {
        return new SignalingMessage
        {
            type = desc.type == RTCSdpType.Offer ? "offer" : "answer",
            sdp = desc.sdp
        };
    }

    public static SignalingMessage FromCand(RTCIceCandidate cand)
    {
        return new SignalingMessage
        {
            type = "candidate",
            candidate = cand.Candidate,
            sdpMid = cand.SdpMid,
            sdpMLineIndex = cand.SdpMLineIndex
        };
    }
}
