# Raft

## Requirement

In this lab you'll implement Raft as a Go object type with associated methods, meant to be used as a module in a larger service. A set of Raft instances talk to each other with RPC to maintain replicated logs. Your Raft interface will support an indefinite sequence of numbered commands, also called log entries. The entries are numbered with index numbers. The log entry with a given index will eventually be committed. At that point, your Raft should send the log entry to the larger service for it to execute.

## References

-   <https://pdos.csail.mit.edu/6.824/labs/lab-raft.html>
-   <http://thesecretlivesofdata.com/raft/>
-   <https://thesquareplanet.com/blog/students-guide-to-raft/>
    -   > If an existing entry conflicts with a new one (same index but different terms), delete the existing entry and all that follow it.
        -   the arguments in `appendEntries` should contain an extra field `leaderLastLogIndex`, which is used to truncate the excessive entries in the followers. This way prevents cases that:

            \-   it exists 5 servers.

            \-   servers #1 and #2 each have entries from #1 and #2 term.

            \-   servers #3, #4, and #5 each have an entry from #1 term only.

            \-   server #1 was the old leader.

            \-   a client has just submitted an entry to server #1 when server #1 was in #2 term.

            \-   server #3 is the leader now.

            Then the client will hag for the response since server #1 cannot determine if the entry of #2 term will be overwritten or committed eventually. To solve the problem:

            \-   server #1 and server #2 should remove the excessive entries comparing to the leader, server #3.

            \-   the leader should commit an entry when all the other followers have owned it, even if the term of the entry is not the same as the `currentTerm`.
-   sample code
    -   <https://medium.com/@arpith/raft-electing-a-leader-4062a3eea068>
    -   <https://eli.thegreenplace.net/2020/implementing-raft-part-2-commands-and-log-replication/>

## Known Issues

-   Follower unexpectedly adding duplicated entries.
    -   fixed
-   The election timeout timer never fire when all peers are candidates and the network latency is great.
    -   suppose to happen since the lagged reply of the requestVote always refresh the timer.
-   lastLogIndex of the requestVote RPC arguments from Candidate sometimes become 0.
    -   due to a wrong condition of log removal
    -   fixed
-   `SubmitAsync` sometimes blocks forever.
