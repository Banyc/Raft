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
        private int transportationTimeHighBoundMillisecond = 30;
        private int transportationTimeLowBoundMillisecond = 2;
        private KeyValuePair<string, int> previousCommand = new("a", 0);
        private readonly bool isShowElectionDebugMessage = true;
        private readonly bool isShowHeartbeatDebugMessage = false;
        private double chanceToDropAPackage = 0.01;

        [Fact]
        public async void Test()
        {
            int peerCount = 5;

            (List<ConsensusModule> consensusModules, List<ConsensusModuleStatesMachine> stateMachines)
                = BuildConsensusModules(peerCount);
            this.consensusModules = consensusModules;

            foreach (var consensus in consensusModules)
            {
                consensus.SendAppendEntriesAsync += AppendEntriesAsyncEventHandler;
                consensus.SendRequestVoteAsync += RequestVoteAsyncEventHandler;
            }

            foreach (var consensus in consensusModules)
            {
                await consensus.StartAsync();
            }

            // Submit(consensusModules);

            await Task.Delay(TimeSpan.FromMilliseconds(1000));

            await SubmitAsync(consensusModules);

            await Task.Delay(TimeSpan.FromMilliseconds(1000));

            // network is extremely jamming
            Console.WriteLine("[network] network is extremely jamming");
            transportationTimeLowBoundMillisecond = 300;
            transportationTimeHighBoundMillisecond = 300;

            await Task.Delay(TimeSpan.FromMilliseconds(1000));

            // network is jamming
            Console.WriteLine("[network] network is jamming");
            transportationTimeLowBoundMillisecond = 2;
            transportationTimeHighBoundMillisecond = 80;
            chanceToDropAPackage = 0.1;

            await Task.Delay(TimeSpan.FromMilliseconds(1000));

            await SubmitAsync(consensusModules);

            await Task.Delay(TimeSpan.FromMilliseconds(1000));

            // network is stable
            Console.WriteLine("[network] network is stable");
            transportationTimeLowBoundMillisecond = 2;
            transportationTimeHighBoundMillisecond = 30;
            chanceToDropAPackage = 0.01;

            await Task.Delay(TimeSpan.FromMilliseconds(1000));

            await SubmitAsync(consensusModules);

            await Task.Delay(TimeSpan.FromMilliseconds(1000));

            // network is ideal
            Console.WriteLine("[network] network is ideal");
            transportationTimeLowBoundMillisecond = 0;
            transportationTimeHighBoundMillisecond = 0;
            chanceToDropAPackage = 0;

            await Task.Delay(TimeSpan.FromMilliseconds(1000));

            await SubmitAsync(consensusModules);

            await Task.Delay(TimeSpan.FromMilliseconds(1000));

            // while (true)
            // {
            //     // keep running
            // }
        }

        private async Task SubmitAsync(List<ConsensusModule> consensusModules)
        {
            int previousPeerId = 0;
            ConsensusModule consensusModule = consensusModules[previousPeerId];
            SetValueReply reply;
            KeyValuePair<string, int> command = new(previousCommand.Key, previousCommand.Value + 1);
            this.previousCommand = command;
            do
            {
                reply = await consensusModule.SetValueAsync(command);
                if (!reply.IsSucceeded)
                {
                    if (!reply.IsLeaderKnown)
                    {
                        Console.WriteLine($"[submit] peer {previousPeerId} does not know leader. Retry after 1 sec.");
                        await Task.Delay(TimeSpan.FromMilliseconds(1000));
                    }
                    else
                    {
                        Console.WriteLine($"[submit] Commit to peer {previousPeerId} failed. Retrying to leader {reply.LeaderId}.");
                        consensusModule = consensusModules[reply.LeaderId];
                    }
                    if (reply.IsCommitInFutureUnclear)
                    {
                        Console.WriteLine("[submit] Term has changed before commit. Stop retrying.");
                        return;
                    }
                    previousPeerId = reply.LeaderId;
                }
            } while (!reply.IsSucceeded && !reply.IsCommitInFutureUnclear);
            Console.WriteLine($"[submit] succeeded ({reply.LeaderId})");
        }

        private async Task<AppendEntriesReply> AppendEntriesAsyncEventHandler(ConsensusModule sender, int targetPeerId, AppendEntriesArgs arguments, CancellationToken cancellationToken)
        {
            // if (arguments.Entries.Count > 0) Console.WriteLine($"[appendEntries] {arguments.LeaderId}.{arguments.Term} |-->  {targetPeerId}");
            if (this.isShowHeartbeatDebugMessage) Console.WriteLine($"[appendEntries] {arguments.LeaderId} |-->  {targetPeerId}");
            if (this.random.NextDouble() <= this.chanceToDropAPackage)
            {
                if (this.isShowHeartbeatDebugMessage) Console.WriteLine($"[appendEntries] {arguments.LeaderId}  -->x {targetPeerId}");
                while (!cancellationToken.IsCancellationRequested) { }
                if (this.isShowHeartbeatDebugMessage) Console.WriteLine($"[appendEntries] {arguments.LeaderId}  -->_ {targetPeerId}");
                return null;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(transportationTimeLowBoundMillisecond, transportationTimeHighBoundMillisecond)));
            if (this.isShowHeartbeatDebugMessage) Console.WriteLine($"[appendEntries] {arguments.LeaderId}  -->| {targetPeerId}");
            var reply = await consensusModules[targetPeerId].AppendEntriesAsync(arguments);
            string statusChar = reply.Success ? "+" : "x";
            if (this.isShowHeartbeatDebugMessage) Console.WriteLine($"[appendEntries] {arguments.LeaderId}  <{statusChar}-| {targetPeerId}");
            if (this.random.NextDouble() <= this.chanceToDropAPackage)
            {
                if (this.isShowHeartbeatDebugMessage) Console.WriteLine($"[appendEntries] {arguments.LeaderId} x<{statusChar}-  {targetPeerId}");
                while (!cancellationToken.IsCancellationRequested) { }
                if (this.isShowHeartbeatDebugMessage) Console.WriteLine($"[appendEntries] {arguments.LeaderId} _<{statusChar}-  {targetPeerId}");
                return null;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(transportationTimeLowBoundMillisecond, transportationTimeHighBoundMillisecond)));
            if (this.isShowHeartbeatDebugMessage) Console.WriteLine($"[appendEntries] {arguments.LeaderId} |<{statusChar}-  {targetPeerId}");
            return reply;
        }

        private async Task<RequestVoteReply> RequestVoteAsyncEventHandler(ConsensusModule sender, int targetPeerId, RequestVoteArgs arguments, CancellationToken cancellationToken)
        {
            if (this.isShowElectionDebugMessage) Console.WriteLine($"[requestVote] {arguments.CandidateId}.{arguments.Term} |-->  {targetPeerId}");
            if (this.random.NextDouble() <= this.chanceToDropAPackage)
            {
                if (this.isShowElectionDebugMessage) Console.WriteLine($"[requestVote] {arguments.CandidateId}.{arguments.Term}  -->x {targetPeerId}");
                while (!cancellationToken.IsCancellationRequested) { }
                if (this.isShowElectionDebugMessage) Console.WriteLine($"[requestVote] {arguments.CandidateId}.{arguments.Term}  -->_ {targetPeerId}");
                return null;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(transportationTimeLowBoundMillisecond, transportationTimeHighBoundMillisecond)));
            if (this.isShowElectionDebugMessage) Console.WriteLine($"[requestVote] {arguments.CandidateId}.{arguments.Term}  -->| {targetPeerId}");
            var reply = await consensusModules[targetPeerId].RequestVoteAsync(arguments);
            string statusChar = reply.VoteGranted ? "+" : "x";
            if (this.isShowElectionDebugMessage) Console.WriteLine($"[requestVote] {arguments.CandidateId}.{arguments.Term}  <{statusChar}-| {targetPeerId}.{reply.Term}");
            if (this.random.NextDouble() <= this.chanceToDropAPackage)
            {
                if (this.isShowElectionDebugMessage) Console.WriteLine($"[requestVote] {arguments.CandidateId}.{arguments.Term} x<{statusChar}-  {targetPeerId}.{reply.Term}");
                while (!cancellationToken.IsCancellationRequested) { }
                if (this.isShowElectionDebugMessage) Console.WriteLine($"[requestVote] {arguments.CandidateId}.{arguments.Term} _<{statusChar}-  {targetPeerId}.{reply.Term}");
                return null;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(this.random.Next(transportationTimeLowBoundMillisecond, transportationTimeHighBoundMillisecond)));
            if (this.isShowElectionDebugMessage) Console.WriteLine($"[requestVote] {arguments.CandidateId}.{arguments.Term} |<{statusChar}-  {targetPeerId}.{reply.Term}");
            return reply;
        }

        private (List<ConsensusModule>, List<ConsensusModuleStatesMachine>) BuildConsensusModules(int peerCount)
        {
            List<ConsensusModule> consensusModules = new();
            List<ConsensusModuleStatesMachine> stateMachines = new();
            int i;
            for (i = 0; i < peerCount; i++)
            {
                ConsensusModuleSettings settings = new()
                {
                    PeerCount = peerCount,
                    ThisPeerId = i,
                    // TimerHeartbeatTimeout = TimeSpan.FromMilliseconds(500),
                    // TimerElectionTimeoutHigherBound = TimeSpan.FromMilliseconds(10001),
                    // TimerElectionTimeoutLowerBound = TimeSpan.FromMilliseconds(9000),
                };
                JsonPersistenceSettings settingsPersistence = new()
                {
                    PersistenceFilePath = $"persistence.{i}.json"
                };
                (ConsensusModule consensus, ConsensusModuleStatesMachine stateMachine) =
                    BuildConsensusModule(settings, settingsPersistence);
                consensusModules.Add(consensus);
                stateMachines.Add(stateMachine);
            }
            return (consensusModules, stateMachines);
        }

        private (ConsensusModule, ConsensusModuleStatesMachine) BuildConsensusModule(ConsensusModuleSettings settings, JsonPersistenceSettings settingsPersistence)
        {
            ConsensusModuleStatesMachine stateMachine = new();
            JsonPersistence<ConsensusModulePersistentState> persistence = new(settingsPersistence);
            ConsensusModule consensus = new(settings, stateMachine, persistence);
            return (consensus, stateMachine);
        }
    }
}
