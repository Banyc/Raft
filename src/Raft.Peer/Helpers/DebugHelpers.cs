using System;
using System.Text;
using Raft.Peer.Models;

namespace Raft.Peer.Helpers
{
    public static class DebugHelpers
    {
        public static void PrintPeerLog(ConsensusModuleStates state, ConsensusModuleSettings settings, int followerIndex)
        {
            StringBuilder result = new();
            result.Append("Leader ID: ").Append(settings.ThisPeerId).Append(", follower index: ").Append(followerIndex).AppendLine();
            int i;
            result.AppendLine("nextIndex:");
            for (i = 0; i < settings.PeerCount; i++)
            {
                result.Append(state.NextIndex[i]).Append(", ");
            }
            result.AppendLine();
            result.AppendLine("matchIndex:");
            for (i = 0; i < settings.PeerCount; i++)
            {
                result.Append(state.MatchIndex[i]).Append(", ");
            }
            result.AppendLine();

            Console.WriteLine(result);
        }
    }
}
