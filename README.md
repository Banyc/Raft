# Raft

## Requirement

In this lab you'll implement Raft as a Go object type with associated methods, meant to be used as a module in a larger service. A set of Raft instances talk to each other with RPC to maintain replicated logs. Your Raft interface will support an indefinite sequence of numbered commands, also called log entries. The entries are numbered with index numbers. The log entry with a given index will eventually be committed. At that point, your Raft should send the log entry to the larger service for it to execute.

## References

-   <https://pdos.csail.mit.edu/6.824/labs/lab-raft.html>
-   <http://thesecretlivesofdata.com/raft/>
-   <https://thesquareplanet.com/blog/students-guide-to-raft/>
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
