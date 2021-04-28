using System.Collections.Generic;
namespace Raft.Peer.Models
{
    public class ConsensusEntry
    {
        public int Index { get; set; }
        public int Term { get; set; }
        public KeyValuePair<string, int> Command { get; set; }
    }
}
