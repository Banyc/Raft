namespace Raft.Peer.Models
{
    // Invoked by candidates to gather votes
    public class RequestVoteArgs
    {
        /// <summary>
        /// candidate's term
        /// </summary>
        public int Term { get; set; }
        /// <summary>
        /// candidate requesting vote
        /// </summary>
        public int CandidateId { get; set; }
        /// <summary>
        /// index of candidate's last log entry
        /// </summary>
        public int LastLogIndex { get; set; }
        /// <summary>
        /// term of candidate's last log entry
        /// </summary>
        public int LastLogTerm { get; set; }
    }
}
