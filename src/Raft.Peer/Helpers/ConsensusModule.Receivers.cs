using System.Threading;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Raft.Peer.Models;

namespace Raft.Peer.Helpers
{
    public partial class ConsensusModule
    {
        // leader -> followers
        //   - replicate log entries
        //   - heartbeat
        // this := follower
        public async Task<AppendEntriesReply> AppendEntriesAsync(AppendEntriesArgs arguments)
        {
            // possible to receive duplicated appendEntries due to the reply not reaching the leader.
            AppendEntriesReply result = new();
            Task persistenceTask = null;
            Task stepDownTask = null;
            lock (this)
            {
                this.timerElectionTimeout.Stop();

                if (
                    // term must match
                    arguments.Term < this.state.PersistentState.CurrentTerm ||
                    !
                    // conditions that cause a success
                    (
                        // log still empty
                        arguments.PrevLogIndex == 0 ||
                        // term of the previous log must match
                        (
                            arguments.PrevLogIndex > 0 &&
                            arguments.PrevLogIndex < this.state.PersistentState.Log.Count &&
                            this.state.PersistentState.Log[arguments.PrevLogIndex].Term ==
                                arguments.PrevLogTerm
                        )
                    )
                )
                {
                    // from illegal leader
                    result.Success = false;

                    // candidate state unchanged

                    // remove the conflict logs
                    if (arguments.PrevLogIndex > 0 &&
                        arguments.PrevLogIndex < this.state.PersistentState.Log.Count &&
                        this.state.PersistentState.Log[arguments.PrevLogIndex].Term !=
                            arguments.PrevLogTerm)
                    {
                        // this.state.PersistentState.Log.RemoveAt(arguments.PrevLogIndex);
                        this.state.PersistentState.Log.RemoveRange(
                            arguments.PrevLogIndex,
                            this.state.PersistentState.Log.Count - arguments.PrevLogIndex);
                    }
                }
                else
                {
                    result.Success = true;

                    // update leader ID
                    this.state.LeaderId = arguments.LeaderId;

                    // truncate excessive log comparing to leader's whole log.
                    if (this.state.PersistentState.Log.Count - 1 > arguments.LeaderLastLogIndex)
                    {
                        this.state.PersistentState.Log.RemoveRange(
                            arguments.LeaderLastLogIndex + 1,
                            this.state.PersistentState.Log.Count - (arguments.LeaderLastLogIndex + 1));
                    }

                    // append new entries
                    bool isSavePersistentStateLater = false;
                    int i;
                    for (i = 0; i < arguments.Entries.Count; i++)
                    {
                        var entry = arguments.Entries[i];
                        int indexOfEntry = (arguments.PrevLogIndex + 1) + i;
                        if (indexOfEntry < this.state.PersistentState.Log.Count)
                        {
                            this.state.PersistentState.Log[indexOfEntry] = entry;
                        }
                        else
                        {
                            this.state.PersistentState.Log.Add(entry);
                        }
                        isSavePersistentStateLater = true;
                    }
                    // save
                    if (isSavePersistentStateLater)
                    {
                        persistenceTask = this.persistence.SaveAsync(this.state.PersistentState);
                    }

                    // commit
                    if (arguments.LeaderCommit > this.state.CommitIndex)
                    {
                        this.state.CommitIndex = Math.Min(
                            arguments.LeaderCommit,
                            this.state.PersistentState.Log.Count - 1);
                    }

                    result.MatchIndex = arguments.PrevLogIndex + arguments.Entries.Count;
                }

                // update state machine
                UpdateStateMachine();
                // concede to the new term
                if (this.state.PersistentState.CurrentTerm < arguments.Term ||
                    // new leader -> this candidate => this convert to follower
                    (
                        this.state.ServerState == ServerState.Candidate &&
                        this.state.PersistentState.CurrentTerm <= arguments.Term
                    ))
                {
                    // discover a current leader or
                    //  new term
                    // ensure turning candidate -> follower 
                    stepDownTask = StepDownAsync(arguments.Term);
                }

                // return the updated currentTerm
                result.Term = this.state.PersistentState.CurrentTerm;

                ConditionalInitiateTimerElectionTimeout();
            }
            if (stepDownTask != null)
            {
                await stepDownTask;
            }
            // wait saving
            if (persistenceTask != null)
            {
                await persistenceTask;
            }
            return result;
        }

        // candidate -> followers
        // this := follower
        public async Task<RequestVoteReply> RequestVoteAsync(RequestVoteArgs arguments)
        {
            RequestVoteReply result = new();
            Task persistenceTask = null;
            Task stepDownTask = null;
            lock (this)
            {
                this.timerElectionTimeout.Stop();

                if (arguments.Term < this.state.PersistentState.CurrentTerm)
                {
                    result.VoteGranted = false;
                }
                else
                {
                    if (arguments.Term > this.state.PersistentState.CurrentTerm)
                    {
                        // step down
                        stepDownTask = StepDownAsync(arguments.Term);
                    }

                    result.VoteGranted =
                        (
                            this.state.PersistentState.VotedFor == null ||
                            this.state.PersistentState.VotedFor == arguments.CandidateId
                        ) &&
                        (
                            // candidate's log is at least as up-to-date as receiver's log
                            arguments.LastLogIndex >= this.state.PersistentState.Log.Count - 1
                        )
                        ;

                    // DEBUG only
                    if (arguments.LastLogIndex < this.state.PersistentState.Log.Count - 1)
                    {
                        Console.WriteLine($"[requestVote] candidate's ({arguments.CandidateId}) lastLogIndex {arguments.LastLogIndex} < receiver's ({this.settings.ThisPeerId}) lastLogIndex {this.state.PersistentState.Log.Count - 1}");
                    }

                    if (result.VoteGranted)
                    {
                        this.state.PersistentState.VotedFor = arguments.CandidateId;
                        persistenceTask = this.persistence.SaveAsync(this.state.PersistentState);
                    }
                }

                result.Term = this.state.PersistentState.CurrentTerm;

                ConditionalInitiateTimerElectionTimeout();
            }
            if (stepDownTask != null)
            {
                await stepDownTask;
            }
            // wait saving
            if (persistenceTask != null)
            {
                await persistenceTask;
            }
            return result;
        }

        // guarante to be committed
        // client -> leader
        public async Task<SetValueReply> SetValueAsync(KeyValuePair<string, int> command)
        {
            Task persistenceTask = null;
            int entryIndex = 0;
            int entryTerm = -1;
            SetValueReply reply = new();
            reply.IsCommitInFutureUnclear = false;
            bool isWaitForCommit = false;
            lock (this)
            {
                if (this.state.ServerState != ServerState.Leader)
                {
                    reply.IsSucceeded = false;
                    reply.IsLeaderKnown = this.state.LeaderId != null;
                    if (reply.IsLeaderKnown)
                    {
                        reply.LeaderId = this.state.LeaderId.Value;
                    }
                }
                else
                {
                    reply.IsLeaderKnown = true;
                    reply.LeaderId = this.settings.ThisPeerId;
                    ConsensusEntry entry = new()
                    {
                        Command = command,
                        Index = this.state.PersistentState.Log.Count,
                        Term = this.state.PersistentState.CurrentTerm,
                    };
                    entryIndex = entry.Index;
                    entryTerm = entry.Term;
                    this.state.PersistentState.Log.Add(entry);
                    persistenceTask = this.persistence.SaveAsync(this.state.PersistentState);
                    isWaitForCommit = true;
                }
            }
            if (isWaitForCommit)
            {
                // busy waiting until committed
                bool isWorthWaiting()
                {
                    lock (this)
                    {
                        return
                            this.state.CommitIndex < entryIndex &&
                            // this.state.PersistentState.CurrentTerm == entryTerm &&
                            entryIndex < this.state.PersistentState.Log.Count &&
                            this.state.PersistentState.Log[entryIndex].Term == entryTerm;
                    }
                }

                while (isWorthWaiting())
                {
                    // Monitor.Wait(this);
                    await Task.Delay(TimeSpan.FromMilliseconds(25));
                }

                lock (this)
                {
                    // TODO:
                    // if (this.state.PersistentState.CurrentTerm != entryTerm)
                    // {
                    //     // not sure if it will be committed or not.
                    //     reply.IsCommitInFutureUnclear = true;
                    // }
                    reply.IsSucceeded =
                        this.state.CommitIndex >= entryIndex &&
                        // this.state.PersistentState.CurrentTerm == entryTerm &&
                        entryIndex < this.state.PersistentState.Log.Count &&
                        this.state.PersistentState.Log[entryIndex].Term == entryTerm;
                }

                if (persistenceTask != null)
                {
                    await persistenceTask;
                }
            }
            return reply;
        }

        public void Crash()
        {
            lock (this)
            {
                this.timerElectionTimeout.Stop();
                this.state.ServerState = ServerState.Dead;
            }
        }
    }
}
