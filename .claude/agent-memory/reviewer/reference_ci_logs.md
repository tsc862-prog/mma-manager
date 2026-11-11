---
name: reference-ci-logs
description: How to read the real build/test result of a PR from GitHub CI when local dotnet runs are off-limits; -v q hides assertion text
metadata:
  type: reference
---

CI (workflow job `build-and-test`) runs `dotnet test -c Release -v q` on GitHub runners for every PR branch.
- `gh pr checks <n>` gives pass/fail; `gh run list --limit 12 --json headBranch,conclusion,...` compares against main/sibling branches.
- `gh run view <runId> --log-failed | grep "\[FAIL\]"` names failing tests; -v q prints NO assertion messages, so the cause must be inferred from the test source.
- PersistenceTests.TestWorld_PopulatesEveryModelProperty / EverySchemaColumn_IsExercisedBySomeRow (seed 99, 30 weeks) break whenever a sim change stops some rare entity from ever occurring in that world, even with no model change.

**Why:** 2026-11-11 the owner's machine was crashing .NET processes; reviews were told not to run dotnet locally and to judge from CI. The author's PR said "CI must be the judge" and CI was red.
**How to apply:** always check `gh pr checks` before approving; never trust an author's "passed before merging main".
