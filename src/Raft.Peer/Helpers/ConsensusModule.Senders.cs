using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raft.Peer.Models;

namespace Raft.Peer.Helpers
{
    public partial class ConsensusModule
    {
        // leader -{AppendEntries}-> followers
        // this := leader
        private void DoAppendEntries()
        {
            // send requestVotes
            int i;
            for (i = 0; i < this.settings.PeerCount; i++)
            {
                int followerIndex = i;
                _ = Task.Run(async () =>
                {
                    await DoAppendEntriesWithFollowerIndexAsync(followerIndex);
                });
            }
        }

        private async Task DoAppendEntriesWithFollowerIndexAsync(int followerIndex)
        {
            AppendEntriesArgs args;
            lock (this)
            {
                int prevLogIndex = this.state.NextIndex[followerIndex] - 1;
                int prevLogTerm = -1;
                if (prevLogIndex >= 0)
                {
                    prevLogTerm = this.state.PersistentState.Log[prevLogIndex].Term;
                }
                args = new()
                {
                    Entries = null,
                    Term = this.state.PersistentState.CurrentTerm,
                    LeaderId = this.settings.ThisPeerId,
                    LeaderCommit = this.state.CommitIndex,
                    PrevLogIndex = prevLogIndex,
                    PrevLogTerm = prevLogTerm,
                };
                // set entries
                if (this.state.PersistentState.Log.Count - 1 >= this.state.NextIndex[followerIndex])
                {
                    args.Entries = this.state.PersistentState.Log
                        .Skip(this.state.NextIndex[followerIndex])
                        .ToList();
                }
            }
            // TODO: timeout exception => release threads
            // if timeout and have logs, no re-send
            CancellationTokenSource tokenSource = new();
            Task<AppendEntriesReply> task = this.SendAppendEntriesAsync(this, followerIndex, args, tokenSource.Token);
            AppendEntriesReply reply = await task;
            if (reply.Term > this.state.PersistentState.CurrentTerm)
            {
                // step down
                StepDown(reply.Term);
                return;
            }
            if (reply.Success)
            {
                // TODO: review
                if (this.state.NextIndex[followerIndex] < this.state.PersistentState.Log.Count)
                {
                    this.state.NextIndex[followerIndex]++;
                }
                this.state.MatchIndex[followerIndex] = reply.MatchIndex;
            }
            else
            {
                this.state.NextIndex[followerIndex]--;
                await DoAppendEntriesWithFollowerIndexAsync(followerIndex);
            }
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
        }
        
        private async void DoRequestVote()
        {
            RequestVoteArgs args = new()
            {
                CandidateId = this.settings.ThisPeerId,
                Term = this.state.PersistentState.CurrentTerm,
            };
            List<Task> tasks = new();
            int i;
            for (i = 0; i < this.settings.PeerCount; i++)
            {
                if (i == this.settings.ThisPeerId)
                {
                    continue;
                }
                int peerIndex = i;
                Task task = Task.Run(async () =>
                {
                    // TODO: timeout exception => release threads
                    // if timeout, must re-send
                    RequestVoteReply reply = null;
                    int initialTerm = this.state.PersistentState.CurrentTerm;
                    do
                    {
                        var tokenSource = new CancellationTokenSource();
                        Task<RequestVoteReply> requestTask = this.SendRequestVoteAsync(this, peerIndex, args, tokenSource.Token);
                        if (await Task.WhenAny(
                            requestTask,
                            Task.Delay(this.settings.TimerHeartbeatTimeout)) == requestTask)
                        {
                            // task completed within timeout
                            reply = await requestTask;
                        }
                        else
                        {
                            // timeout
                            // re-send requestVote
                            tokenSource.Cancel();
                        }
                    } while (reply != null &&
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
                            this.state.ServerState = ServerState.Leader;
                            // send heartbeats before any other server time out.
                            // establish authority
                            // prevent new elections
                            DoAppendEntries();
                            // reinitialize after election
                            InitiateLeaderState();
                        }
                    }
                });
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);
        }
    }
}
