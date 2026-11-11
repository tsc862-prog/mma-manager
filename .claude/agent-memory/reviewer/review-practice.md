---
name: review-practice
description: How PR reviews run on this machine - read-only via git show/gh, CI logs lack assertion text, branches move mid-review
metadata:
  type: feedback
---

Reviews are read-only: read the branch with `git show origin/<branch>:<path>` and `gh pr diff`, never check out in the main checkout. On 2026-11-11 the lead banned local dotnet build/test/longsim (machine crashing .NET); CI on GitHub is the build/test source of truth.

**Why:** local .NET processes were corrupting; CI is authoritative.
**How to apply:**
- Re-fetch and compare the PR head SHA before reporting; on PR #17 the author pushed a fix mid-review.
- CI test logs (`gh run view <id> --log-failed`) show only `[FAIL]` and test name, no assertion message (minimal verbosity, no trx artifact). Reason the failure out from the test body.
- Fight-day code (`CardFiller.PadToMinimum`, `RestoreHeadliner` in `RunEventCore`) runs after the calendar guards, so check it against any "booked ahead" invariant.
