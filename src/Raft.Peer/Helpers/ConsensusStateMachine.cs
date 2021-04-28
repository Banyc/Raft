using System.Collections.Generic;
namespace Raft.Peer.Helpers
{
    public class ConsensusModuleStatesMachine
    {
        public Dictionary<string, int> Map { get; set; }

        public void Apply(KeyValuePair<string, int> command)
        {
            this.Map[command.Key] = command.Value;
        }
    }
}
