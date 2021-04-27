using System.Collections.Generic;
using System;
using Raft.Peer.Helpers;
using Raft.Peer.Models;
using Xunit;
using System.Threading.Tasks;
using System.Threading;

namespace Raft.Peer.Tests
{
    public class UnitTest1
    {
        private List<ConsensusModule> consensusModules;
        private readonly Random random = new();

        [Fact]
        public void Test()
        {
            int peerCount = 5;

            (List<ConsensusModule> consensusModules, List<ConsensusStateMachine> stateMachines)
                = BuildConsensusModules(peerCount);
            this.consensusModules = consensusModules;

            foreach (var consensus in consensusModules)
            {
                consensus.SendAppendEntriesAsync += AppendEntriesAsyncEventHandler;
                consensus.SendRequestVoteAsync += RequestVoteAsyncEventHandler;
            }

            foreach (var consensus in consensusModules)
            {
                consensus.Start();
            }

            while (true)
            {
                // keep running
            }
        }

        private async Task<AppendEntriesReply> AppendEntriesAsyncEventHandler(ConsensusModule sender, int targetPeerId, AppendEntriesArgs arguments, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[appendEntries] {arguments.LeaderId} |-->  {targetPeerId}");
            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(2, 30)));
            Console.WriteLine($"[appendEntries] {arguments.LeaderId}  -->| {targetPeerId}");
            var reply = consensusModules[targetPeerId].AppendEntries(arguments);
            string statusChar = reply.Success ? "+" : "x";
            Console.WriteLine($"[appendEntries] {arguments.LeaderId}  <{statusChar}-| {targetPeerId}");
            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(2, 30)));
            Console.WriteLine($"[appendEntries] {arguments.LeaderId} |<{statusChar}-  {targetPeerId}");
            return reply;
        }

        private async Task<RequestVoteReply> RequestVoteAsyncEventHandler(ConsensusModule sender, int targetPeerId, RequestVoteArgs arguments, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[requestVote] {arguments.CandidateId} |-->  {targetPeerId}");
            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(2, 30)));
            Console.WriteLine($"[requestVote] {arguments.CandidateId}  -->| {targetPeerId}");
            var reply = consensusModules[targetPeerId].RequestVote(arguments);
            string statusChar = reply.VoteGranted ? "+" : "x";
            Console.WriteLine($"[requestVote] {arguments.CandidateId}  <{statusChar}-| {targetPeerId}");
            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(2, 30)));
            Console.WriteLine($"[requestVote] {arguments.CandidateId} |<{statusChar}-  {targetPeerId}");
            return reply;
        }

        private (List<ConsensusModule>, List<ConsensusStateMachine>) BuildConsensusModules(int peerCount)
        {
            List<ConsensusModule> consensusModules = new();
            List<ConsensusStateMachine> stateMachines = new();
            int i;
            for (i = 0; i < peerCount; i++)
            {
                ConsensusSettings settings = new()
                {
                    PeerCount = peerCount,
                    ThisPeerId = i,
                };
                (ConsensusModule consensus, ConsensusStateMachine stateMachine) =
                    BuildConsensusModule(settings);
                consensusModules.Add(consensus);
                stateMachines.Add(stateMachine);
            }
            return (consensusModules, stateMachines);
        }

        private (ConsensusModule, ConsensusStateMachine) BuildConsensusModule(ConsensusSettings settings)
        {
            ConsensusStateMachine stateMachine = new();
            ConsensusModule consensus = new(settings, stateMachine);
            return (consensus, stateMachine);
        }
    }
}
