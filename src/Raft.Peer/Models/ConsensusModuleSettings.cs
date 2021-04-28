using System;
namespace Raft.Peer.Models
{
    public class ConsensusModuleSettings
    {
        // The election timeout is the amount of time a follower waits until becoming a candidate.
        public TimeSpan TimerElectionTimeoutLowerBound { get; set; } = TimeSpan.FromMilliseconds(150);
        public TimeSpan TimerElectionTimeoutHigherBound { get; set; } = TimeSpan.FromMilliseconds(300);
        public TimeSpan TimerHeartbeatTimeout { get; set; } = TimeSpan.FromMilliseconds(60);
        public int PeerCount { get; set; }
        public int ThisPeerId { get; set; }
    }
}
