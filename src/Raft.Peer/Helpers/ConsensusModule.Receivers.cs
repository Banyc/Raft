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
            this.timerElectionTimeout.Stop();

            AppendEntriesReply result = new();

            if (
                // term must match
                arguments.Term < this.state.PersistentState.CurrentTerm ||
                // term of the previous log must match
                !
                (
                    arguments.PrevLogIndex < this.state.PersistentState.Log.Count &&
                    this.state.PersistentState.Log[arguments.PrevLogIndex].Term ==
                        arguments.PrevLogTerm
                )
            )
            {
                // from illegal leader
                result.Success = false;

                // candidate state unchanged

                // remove the conflict logs
                if (arguments.PrevLogIndex < this.state.PersistentState.Log.Count &&
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
            while (this.state.CommitIndex > this.state.LastApplied)
            {
                this.stateMachine.Apply(this.state.PersistentState.Log[this.state.LastApplied + 1].Command);
                this.state.LastApplied++;
            }
            // concede to the new term
            if (this.state.PersistentState.CurrentTerm < arguments.Term ||
                // new leader -> this candidate => this convert to follower
                (
                    this.state.ServerState == ServerState.Candidate &&
                    this.state.PersistentState.CurrentTerm <= arguments.Term
                ))
            {
                // keep up with the new term
                if (this.state.PersistentState.CurrentTerm < arguments.Term)
                {
                    this.state.PersistentState.CurrentTerm = arguments.Term;
                }

                // discover a current leader or
                //  new term
                // ensure turning candidate -> follower 
                StepDown();

                // update leader ID
                this.state.LeaderId = arguments.LeaderId;
            }

            // return the updated currentTerm
            result.Term = this.state.PersistentState.CurrentTerm;

            ConditionalInitiateTimerElectionTimeout();
            return result;
        }

        // candidate -> followers
        // this := follower
        public RequestVoteReply RequestVote(RequestVoteArgs arguments)
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
                    // keep up with the new term
                    this.state.PersistentState.CurrentTerm = arguments.Term;
                    // step down
                    StepDown();
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
}
