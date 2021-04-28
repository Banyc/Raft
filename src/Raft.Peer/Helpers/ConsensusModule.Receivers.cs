using System;
using Raft.Peer.Models;

namespace Raft.Peer.Helpers
{
    public partial class ConsensusModule
    {
        // leader -> followers
        //   - replicate log entries
        //   - heartbeat
        // this := follower
        public AppendEntriesReply AppendEntries(AppendEntriesArgs arguments)
        {
            lock (this)
            {
                this.timerElectionTimeout.Stop();

                AppendEntriesReply result = new();

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
                            arguments.Term)
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

                    // append new entries
                    this.state.PersistentState.Log.AddRange(arguments.Entries);

                    // commit
                    if (arguments.LeaderCommit > this.state.CommitIndex)
                    {
                        this.state.CommitIndex = Math.Min(
                            arguments.LeaderCommit,
                            this.state.PersistentState.Log.Count - 1);
                    }

                    result.MatchIndex = this.state.PersistentState.Log.Count - 1;
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
                    StepDown(arguments.Term);
                }

                // return the updated currentTerm
                result.Term = this.state.PersistentState.CurrentTerm;

                ConditionalInitiateTimerElectionTimeout();
                return result;
            }
        }

        // candidate -> followers
        // this := follower
        public RequestVoteReply RequestVote(RequestVoteArgs arguments)
        {
            lock (this)
            {
                this.timerElectionTimeout.Stop();

                RequestVoteReply result = new();

                if (arguments.Term < this.state.PersistentState.CurrentTerm)
                {
                    result.VoteGranted = false;
                }
                else
                {
                    if (arguments.Term > this.state.PersistentState.CurrentTerm)
                    {
                        // step down
                        StepDown(arguments.Term);
                        // now vote for the new term
                        // the previous vote was stale
                        this.state.PersistentState.VotedFor = null;
                    }

                    result.VoteGranted =
                        (
                            this.state.PersistentState.VotedFor == null ||
                            this.state.PersistentState.VotedFor == arguments.CandidateId
                        ) &&
                        (
                            // candidate’s log is at least as up-to-date as receiver’s log
                            arguments.LastLogIndex >= this.state.PersistentState.Log.Count - 1
                        )
                        ;

                    if (result.VoteGranted)
                    {
                        this.state.PersistentState.VotedFor = arguments.CandidateId;
                    }
                }

                result.Term = this.state.PersistentState.CurrentTerm;

                ConditionalInitiateTimerElectionTimeout();
                return result;
            }
        }

        // no guarante to be committed
        // client -> leader
        public SetValueReply SetValue((string, int) command)
        {
            SetValueReply reply = new();
            lock (this)
            {
                if (this.state.ServerState != ServerState.Leader)
                {
                    reply.IsLeaderKnown = this.state.LeaderId != null;
                    if (reply.IsLeaderKnown)
                    {
                        reply.LeaderId = this.state.LeaderId.Value;
                    }
                }
                else
                {
                    ConsensusEntry entry = new()
                    {
                        Command = command,
                        Index = this.state.PersistentState.Log.Count,
                        Term = this.state.PersistentState.CurrentTerm,
                    };
                    this.state.PersistentState.Log.Add(entry);
                }
            }
            return reply;
        }
    }
}
