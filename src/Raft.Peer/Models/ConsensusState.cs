using System.Collections.Generic;
namespace Raft.Peer.Models
{
    public enum ServerState
    {
        Follower,
        Candidate,
        Leader,
    }

    // (Updated on stable storage before responding to RPCs)
    public class ConsensusPersistentState
    {
        /// <summary>
        /// latest term server has seen
        ///   (initialized to 0 on first boot, increases monotonically)
        /// </summary>
        public int CurrentTerm { get; set; } = 0;
        /// <summary>
        /// candidateId that received vote in current term (or null if none)
        /// </summary>
        public int? VotedFor { get; set; } = null;
        /// <summary>
        /// log entries; each entry contains command for state machine,
        ///   and term when entry was received by leader (first index is 1)
        /// </summary>
        public List<ConsensusEntry> Log { get; set; } = new() { null };
        // {
        public int VoteCount { get; set; } = 0;
        // }
    }

    public class ConsensusState
    {
        // {
        public ServerState ServerState { get; set; } = ServerState.Follower;
        public int? LeaderId { get; set; } = null;
        // }
        public ConsensusPersistentState PersistentState { get; set; }
        /// <summary>
        /// index of highest log entry known to be committed (initialized to 0, increases monotonically)
        /// </summary>
        public int CommitIndex { get; set; } = 0;
        /// <summary>
        /// index of highest log entry applied to state machine (initialized to 0, increases monotonically)
        /// </summary>
        public int LastApplied { get; set; } = 0;
        // leader only
        //   (Reinitialized after election)
        // {
        /// <summary>
        /// for each server, index of the next log entry to send to that server (initialized to leader last log index + 1)
        /// </summary>
        public List<int> NextIndex { get; set; } = new();
        /// <summary>
        /// for each server, index of highest log entry known to be replicated on server (initialized to 0, increases monotonically)
        /// </summary>
        public List<int> MatchIndex { get; set; } = new();
        // }
    }
}
