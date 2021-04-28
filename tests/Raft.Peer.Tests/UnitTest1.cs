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
        private (string, int) previousCommand = ("a", 1);
        private readonly bool isShowElectionDebugMessage = true;
        private readonly bool isShowHeartbeatDebugMessage = false;

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

            Submit(consensusModules);

            Task.Delay(TimeSpan.FromMilliseconds(1000)).Wait();

            Submit(consensusModules);

            while (true)
            {
                // keep running
            }
        }

        private void Submit(List<ConsensusModule> consensusModules)
        {
            ConsensusModule consensusModule = consensusModules[0];
            SetValueReply reply;
            do
            {
                reply = consensusModule.SetValue((previousCommand.Item1, previousCommand.Item2 + 1));
                if (!reply.IsSucceeded)
                {
                    if (!reply.IsLeaderKnown)
                    {
                        Console.WriteLine("[submit] leader unknown");
                        return;
                    }
                    consensusModule = consensusModules[reply.LeaderId];
                }
            } while (!reply.IsSucceeded);
            Console.WriteLine("[submit] succeeded");
        }

        private async Task<AppendEntriesReply> AppendEntriesAsyncEventHandler(ConsensusModule sender, int targetPeerId, AppendEntriesArgs arguments, CancellationToken cancellationToken)
        {
            if (this.isShowHeartbeatDebugMessage) Console.WriteLine($"[appendEntries] {arguments.LeaderId} |-->  {targetPeerId}");
            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(2, 30)));
            if (this.isShowHeartbeatDebugMessage) Console.WriteLine($"[appendEntries] {arguments.LeaderId}  -->| {targetPeerId}");
            var reply = consensusModules[targetPeerId].AppendEntries(arguments);
            string statusChar = reply.Success ? "+" : "x";
            if (this.isShowHeartbeatDebugMessage) Console.WriteLine($"[appendEntries] {arguments.LeaderId}  <{statusChar}-| {targetPeerId}");
            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(2, 30)));
            if (this.isShowHeartbeatDebugMessage) Console.WriteLine($"[appendEntries] {arguments.LeaderId} |<{statusChar}-  {targetPeerId}");
            return reply;
        }

        private async Task<RequestVoteReply> RequestVoteAsyncEventHandler(ConsensusModule sender, int targetPeerId, RequestVoteArgs arguments, CancellationToken cancellationToken)
        {
            if (this.isShowElectionDebugMessage) Console.WriteLine($"[requestVote] {arguments.CandidateId}.{arguments.Term} |-->  {targetPeerId}");
            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(2, 30)));
            if (this.isShowElectionDebugMessage) Console.WriteLine($"[requestVote] {arguments.CandidateId}.{arguments.Term}  -->| {targetPeerId}");
            var reply = consensusModules[targetPeerId].RequestVote(arguments);
            string statusChar = reply.VoteGranted ? "+" : "x";
            if (this.isShowElectionDebugMessage) Console.WriteLine($"[requestVote] {arguments.CandidateId}.{arguments.Term}  <{statusChar}-| {targetPeerId}.{reply.Term}");
            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(2, 30)));
            if (this.isShowElectionDebugMessage) Console.WriteLine($"[requestVote] {arguments.CandidateId}.{arguments.Term} |<{statusChar}-  {targetPeerId}.{reply.Term}");
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
                    // TimerHeartbeatTimeout = TimeSpan.FromMilliseconds(50),
                    // TimerElectionTimeoutHigherBound = TimeSpan.FromMilliseconds(10001),
                    // TimerElectionTimeoutLowerBound = TimeSpan.FromMilliseconds(10000),
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
