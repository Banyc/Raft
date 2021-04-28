namespace Raft.Peer.Models
{
    public class SetValueReply
    {
        public bool IsSucceeded { get; set; }
        public int LeaderId { get; set; }
        public bool IsLeaderKnown { get; set; }
    }
}
