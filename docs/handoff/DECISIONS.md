# Handoff Decision Index

The authoritative decision log is the repository-root [`DECISIONS.md`](../../DECISIONS.md). Do not create a second independent history here.

Key boundaries for Phase 1:

1. identity/keyframe/preview approval is not runtime approval;
2. behavior identity is separate from replaceable artwork versions;
3. all request sources share one eligibility/arbitration/execution pipeline;
4. Pupu is pinned read-only engineering reference, not a rename/re-skin base;
5. the six-tab UX and developer mode expose services and diagnostics but do not own behavior state;
6. preview, simulation, and developer-forced execution never write production memory;
7. Windows real-renderer validation remains a distinct release gate.

Add durable approvals and superseding decisions to the root log with dates and consequences.
