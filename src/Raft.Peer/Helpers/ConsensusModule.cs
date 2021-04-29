using System.Xml.Linq;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Raft.Peer.Models;

namespace Raft.Peer.Helpers
{
    public partial class ConsensusModule
    {
        private ConsensusModuleStates state;
        // The election timeout is the amount of time a follower waits until becoming a candidate.
        private readonly System.Timers.Timer timerElectionTimeout = new();
        private readonly ConsensusModuleSettings settings;
        private readonly ConsensusModuleStatesMachine stateMachine;
        private readonly Random random = new();
        private readonly JsonPersistence<ConsensusModulePersistentState> persistence;

        // public delegate AppendEntriesReply AppendEntriesEventHandler(ConsensusModule sender, int targetPeerId, AppendEntriesArgs arguments);
        // public event AppendEntriesEventHandler SendAppendEntries;
        public delegate Task<AppendEntriesReply> AppendEntriesAsyncEventHandler(ConsensusModule sender, int targetPeerId, AppendEntriesArgs arguments, CancellationToken cancellationToken);
        /// <summary>
        /// return null if timeout
        /// </summary>
        public event AppendEntriesAsyncEventHandler SendAppendEntriesAsync;

        // public delegate RequestVoteReply RequestVoteEventHandler(ConsensusModule sender, int targetPeerId, RequestVoteArgs arguments);
        // public event RequestVoteEventHandler SendRequestVote;
        public delegate Task<RequestVoteReply> RequestVoteAsyncEventHandler(ConsensusModule sender, int targetPeerId, RequestVoteArgs arguments, CancellationToken cancellationToken);
        /// <summary>
        /// return null if timeout
        /// </summary>
        public event RequestVoteAsyncEventHandler SendRequestVoteAsync;

        public ConsensusModule(ConsensusModuleSettings settings, ConsensusModuleStatesMachine stateMachine, JsonPersistence<ConsensusModulePersistentState> persistence)
        {
            this.settings = settings;
            this.stateMachine = stateMachine;
            this.persistence = persistence;
            // timer {
            this.timerElectionTimeout.AutoReset = false;
            this.timerElectionTimeout.Elapsed += TimerElectionTimeout_Elapsed;
            // }
        }

        public async Task StartAsync()
        {
            this.state = new();
            var persistentState = await persistence.LoadAsync();
            if (persistentState != default)
            {
                this.state.PersistentState = persistentState;
            }
            ConditionalInitiateTimerElectionTimeout();
        }

        // this method lasts for a whole term
        private async Task BecomeLeaderAsync()
        {
            // DEBUG only
            Console.WriteLine($"[requestVote] {this.settings.ThisPeerId} becomes leader");
            this.state.ServerState = ServerState.Leader;
            this.timerElectionTimeout.Stop();
            this.state.NextIndex.Clear();
            this.state.MatchIndex.Clear();
            int lastLogIndex = this.state.PersistentState.Log.Count - 1;
            int i;
            for (i = 0; i < this.settings.PeerCount; i++)
            {
                this.state.NextIndex.Add(lastLogIndex + 1);
                this.state.MatchIndex.Add(0);
            }
            // send heartbeats before any other server time out.
            // establish authority
            // prevent new elections
            await DoAppendEntriesAsync(term: this.state.PersistentState.CurrentTerm);
        }

        private void InitiateTimerElectionTimeoutInterval()
        {
            this.timerElectionTimeout.Interval = NextDouble(
                this.settings.TimerElectionTimeoutLowerBound.TotalMilliseconds,
                this.settings.TimerElectionTimeoutHigherBound.TotalMilliseconds
            );
        }

        private void ConditionalInitiateTimerElectionTimeout()
        {
            if (this.state.ServerState == ServerState.Follower ||
                this.state.ServerState == ServerState.Candidate)
            {
                InitiateTimerElectionTimeoutInterval();
                this.timerElectionTimeout.Start();
            }
        }

        private double NextDouble(double minValue, double maxValue)
        {
            double x = this.random.NextDouble();
            double a = minValue;
            double b = maxValue;

            return
                (b - a) * x + a;
        }

        private async Task StepDownAsync(int newTerm)
        {
            Task persistenceTask = null;
            // TODO: review: the persistent state does not seem to require persistence
            if (newTerm > this.state.PersistentState.CurrentTerm)
            {
                // now vote for the new term
                // the previous vote was stale
                this.state.PersistentState.VotedFor = null;
                this.state.PersistentState.CurrentTerm = newTerm;
                this.state.LeaderId = null;
                persistenceTask = this.persistence.SaveAsync(this.state.PersistentState);
            }
            this.state.ServerState = ServerState.Follower;
            // stop heartbeat

            // notify client handler to cancel commit
            // Monitor.PulseAll(this);
            if (persistenceTask != null)
            {
                await persistenceTask;
            }
        }

        private void UpdateStateMachine()
        {
            // update state machine
            while (this.state.CommitIndex > this.state.LastApplied)
            {
                this.stateMachine.Apply(this.state.PersistentState.Log[this.state.LastApplied + 1].Command);
                this.state.LastApplied++;
            }
        }

        // candidate -{requestVote}-> followers
        // this := candidate
        private async void TimerElectionTimeout_Elapsed(object sender, ElapsedEventArgs e)
        {
            Task persistenceTask = null;
            lock (this)
            {
                // TODO: review: currentTerm and voteFor do not seem to require persistence
                // After the election timeout the follower becomes a candidate
                this.state.ServerState = ServerState.Candidate;
                // and starts a new election term...
                this.state.PersistentState.CurrentTerm++;
                // ...votes for itself...
                this.state.PersistentState.VotedFor = this.settings.ThisPeerId;
                this.state.PersistentState.VoteCount = 0;
                this.state.PersistentState.VoteCount++;

                persistenceTask = this.persistence.SaveAsync(this.state.PersistentState);

                // reset election timer
                ConditionalInitiateTimerElectionTimeout();
            }

            await persistenceTask;

            // send requestVotes
            await DoRequestVoteAsync();
        }
    }
}
