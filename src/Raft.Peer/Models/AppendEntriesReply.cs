namespace Raft.Peer.Models
{
    // peers -> leader
    public class AppendEntriesReply
    {
        /// <summary>
        /// currentTerm, for leader to update itself
        /// </summary>
        public int Term { get; set; }
        /// <summary>
        /// true if follower contained entry matching prevLogIndex and prevLogTerm
        /// </summary>
        public bool Success { get; set; }
        /// <summary>
        /// the first index it stores for that term
        /// </summary>
        public int MatchIndex { get; set; } = 0;
    }
}
