namespace Raft.Peer.Models
{
    // peers -> candidate
    public class RequestVoteReply
    {
        /// <summary>
        /// currentTerm, for candidate to update itself
        /// </summary>
        public int Term { get; set; }
        /// <summary>
        /// true means candidate received vote
        /// </summary>
        public bool VoteGranted { get; set; }
    }
}
