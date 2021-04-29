using System.Collections.Generic;

namespace Raft.Peer.Models
{
    // Invoked by leader
    // usage:
    //  -   heartbeat
    //  -   replicate log entries
    public class AppendEntriesArgs
    {
        /// <summary>
        /// leader's term
        /// </summary>
        public int Term { get; set; }
        /// <summary>
        /// so follower can redirect clients
        /// </summary>
        public int LeaderId { get; set; }
        /// <summary>
        /// index of log entry immediately preceding new ones
        /// </summary>
        public int PrevLogIndex { get; set; }
        /// <summary>
        /// term of prevLogIndex entry
        /// </summary>
        public int PrevLogTerm { get; set; }
        /// <summary>
        /// log entries to store
        ///   (empty for heartbeat; may send more than one for efficiency)
        /// </summary>
        public List<ConsensusEntry> Entries { get; set; } = new();
        /// <summary>
        /// leader's commitIndex
        /// </summary>
        public int LeaderCommit { get; set; }
        /// <summary>
        /// remove excessive logs from the followers
        /// </summary>
        public int LeaderLastLogIndex { get; set; }
    }
}
