namespace Raft.Peer.Models
{
    public class SetValueReply
    {
        public bool IsSucceeded { get; set; }
        public int LeaderId { get; set; }
        public bool IsLeaderKnown { get; set; }
        // the leader enters new term becoming a follower
        //   before it appendEntries enough followers
        // not sure if the submission will be committed in the future
        public bool IsCommitInFutureUnclear { get; set; }
    }
}
