using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raft.Peer.Models;

namespace Raft.Peer.Helpers
{
    public partial class ConsensusModule
    {
        #region "appendEntries"
        // leader -{AppendEntries}-> followers
        // this := leader
        private async Task DoAppendEntriesAsync(int term)
        {
            List<Task> tasks = new();
            // send requestVotes
            int i;
            for (i = 0; i < this.settings.PeerCount; i++)
            {
                int followerIndex = i;
                if (followerIndex == this.settings.ThisPeerId)
                {
                    continue;
                }
                Task task = Task.Run(async () =>
                {
                    await DoAppendEntriesWithFollowerIndexAsync(followerIndex, term);
                });
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);
        }

        private async Task DoAppendEntriesWithFollowerIndexAsync(int followerIndex, int term)
        {
            // the first appendEntries should be a pure (no-op) hearbeat
            bool isWithoutEntries = true;
            AppendEntriesReply reply;
            bool isKeepLoopping = true;
            bool isPrintDebugInfo = false;

            // until new election
            while (isKeepLoopping)
            {
                // until a successful reply
                do
                {
                    reply = null;
                    AppendEntriesArgs args;
                    lock (this)
                    {
                        int prevLogIndex = this.state.NextIndex[followerIndex] - 1;
                        int prevLogTerm = -1;
                        if (prevLogIndex > 0)
                        {
                            prevLogTerm = this.state.PersistentState.Log[prevLogIndex].Term;
                        }
                        args = new()
                        {
                            Entries = new List<ConsensusEntry>(),
                            Term = this.state.PersistentState.CurrentTerm,
                            LeaderId = this.settings.ThisPeerId,
                            LeaderCommit = this.state.CommitIndex,
                            PrevLogIndex = prevLogIndex,
                            PrevLogTerm = prevLogTerm,
                        };
                        isPrintDebugInfo = false;
                        // set entries
                        if (this.state.PersistentState.Log.Count - 1 >= this.state.NextIndex[followerIndex] && !isWithoutEntries)
                        {
                            args.Entries = this.state.PersistentState.Log
                                .Skip(this.state.NextIndex[followerIndex])
                                .ToList();
                            isPrintDebugInfo = true;
                        }
                    }
                    // if timeout and have logs, no immediate re-send
                    using var tokenSource = new CancellationTokenSource();
                    try
                    {
                        tokenSource.CancelAfter(this.settings.TimerHeartbeatTimeout);
                        Task<AppendEntriesReply> task = this.SendAppendEntriesAsync(this, followerIndex, args, tokenSource.Token);
                        // task completed within timeout
                        reply = await task;
                    }
                    catch (TaskCanceledException)
                    {
                        // timeout
                        break;
                    }
                    lock (this)
                    {
                        if (reply.Term > this.state.PersistentState.CurrentTerm)
                        {
                            // step down
                            StepDown(reply.Term);
                            break;
                        }
                        if (!(
                            this.state.ServerState == ServerState.Leader &&
                            reply.Term == this.state.PersistentState.CurrentTerm
                            ))
                        {
                            // received an outdated appendEntries RPC
                            // discard
                            break;
                        }
                        if (reply.Success)
                        {
                            // this.state.NextIndex[followerIndex] += args.Entries.Count;
                            this.state.NextIndex[followerIndex] = reply.MatchIndex + 1;
                            this.state.MatchIndex[followerIndex] = this.state.NextIndex[followerIndex] - 1;

                            // leader commits
                            while (true)
                            {
                                int newCommitIndex = this.state.CommitIndex + 1;
                                int matchIndexCount = 0;
                                int i;
                                for (i = 0; i < this.settings.PeerCount; i++)
                                {
                                    if (this.state.MatchIndex[i] > newCommitIndex)
                                    {
                                        matchIndexCount++;
                                    }
                                }
                                if (newCommitIndex > this.state.CommitIndex &&
                                    matchIndexCount > this.settings.PeerCount / 2 &&
                                    this.state.PersistentState.Log[newCommitIndex].Term == this.state.PersistentState.CurrentTerm)
                                {
                                    this.state.CommitIndex = newCommitIndex;
                                }
                                else
                                {
                                    break;
                                }
                            }

                            UpdateStateMachine();
                        }
                        else
                        {
                            this.state.NextIndex[followerIndex]--;
                        }
                    }
                } while (reply.Success == false);
                isWithoutEntries = false;
                await Task.Delay(this.settings.TimerHeartbeatTimeout);
                lock (this)
                {
                    isKeepLoopping = term == this.state.PersistentState.CurrentTerm && this.state.ServerState == ServerState.Leader;
                }
                // DEBUG only
                if (isPrintDebugInfo && reply?.Success == true)
                {
                    DebugHelpers.PrintPeerLog(this.state, this.settings, followerIndex);
                }
            }
        }
        #endregion

        private async Task DoRequestVoteAsync()
        {
            List<Task> tasks = new();
            int i;
            for (i = 0; i < this.settings.PeerCount; i++)
            {
                if (i == this.settings.ThisPeerId)
                {
                    continue;
                }
                int peerIndex = i;
                Task appendEntriesTask = null;
                Task task = Task.Run(async () =>
                {
                    RequestVoteArgs args;
                    int initialTerm;
                    lock (this)
                    {
                        int lastLogIndex = this.state.PersistentState.Log.Count - 1;
                        int lastLogTerm = -1;
                        if (lastLogIndex > 0)
                        {
                            lastLogTerm = this.state.PersistentState.Log[lastLogIndex].Term;
                        }
                        args = new()
                        {
                            CandidateId = this.settings.ThisPeerId,
                            Term = this.state.PersistentState.CurrentTerm,
                            LastLogIndex = lastLogIndex,
                            LastLogTerm = lastLogTerm,
                        };
                        initialTerm = this.state.PersistentState.CurrentTerm;
                    }
                    RequestVoteReply reply = null;
                    do
                    {
                        // if timeout, must re-send
                        using var tokenSource = new CancellationTokenSource();
                        try
                        {
                            tokenSource.CancelAfter(this.settings.TimerHeartbeatTimeout);
                            Task<RequestVoteReply> requestTask = this.SendRequestVoteAsync(this, peerIndex, args, tokenSource.Token);
                            // task completed within timeout
                            reply = await requestTask;
                        }
                        catch (TaskCanceledException)
                        {
                            // timeout
                        }
                    } while (reply == null &&
                        initialTerm == this.state.PersistentState.CurrentTerm);
                    lock (this)
                    {
                        if (initialTerm != this.state.PersistentState.CurrentTerm)
                        {
                            // the old term has expired
                            // the reply should be discarded
                            return;
                        }
                        if (reply.Term > this.state.PersistentState.CurrentTerm)
                        {
                            // step down
                            StepDown(reply.Term);
                            return;
                        }
                        if (this.state.ServerState != ServerState.Candidate ||
                            reply.Term < this.state.PersistentState.CurrentTerm)
                        {
                            return;
                        }
                        if (reply.VoteGranted)
                        {
                            this.state.PersistentState.VoteCount++;
                        }
                        if (this.state.PersistentState.VoteCount > this.settings.PeerCount / 2)
                        {
                            // this candidate has gained majority votes.
                            // reinitialize after election
                            // send heartbeats before any other server time out.
                            // establish authority
                            // prevent new elections
                            appendEntriesTask = BecomeLeaderAsync();
                        }
                    }
                });
                tasks.Add(task);
                if (appendEntriesTask != null)
                {
                    tasks.Add(appendEntriesTask);
                }
            }
            await Task.WhenAll(tasks);
        }
    }
}
