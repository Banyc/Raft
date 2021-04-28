using System.Collections.Generic;
namespace Raft.Peer.Helpers
{
    public class ConsensusModuleStatesMachine
    {
        public Dictionary<string, int> Map { get; set; }

        public void Apply((string, int) command)
        {
            this.Map[command.Item1] = command.Item2;
        }
    }
}
