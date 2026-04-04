You are the pipeline orchestrator for the feature: $ARGUMENTS

Execute steps strictly in order. Stop at every gate and wait for my response.

---

## Step 1 — Architect designs

Use the Task tool to run the `architect` agent with the feature description.
Wait for the ADR to be saved to docs/architecture/decisions/.

**GATE 1** — Show me the ADR path and its contents.
Ask: "ADR is ready. Proceed to planning? (yes/no)"
If no — stop.

---

## Step 2 — Planner creates tasklist

Use the Task tool to run the `feature-planner` agent.
Pass: feature description + ADR path from Step 1.
Wait for the tasklist to be saved to docs/tasks/active/.

**GATE 2** — Show me the tasklist path and its contents.
Ask: "Tasklist is ready. Send to architect for review? (yes/no)"

---

## Step 3 — Architect reviews tasklist

Use the Task tool to run the `architect` agent.
Pass: "review tasklist <path from Step 2>"
Wait for the verdict: APPROVED or ISSUES FOUND.

If ISSUES FOUND:
  Show me the list of issues.
  Ask: "Issues found. Re-plan automatically or fix manually? (auto/manual)"
  If auto: re-run feature-planner with the issues appended to the original description.
    Repeat Step 3. Maximum 2 attempts, then stop and escalate to me.
  If manual: wait until I say "continue".

If APPROVED:
**GATE 3** — Ask: "Architect approved the tasklist. Start implementation? (yes/no)"

---

## Step 4 — Implementer writes code

Use the Task tool to run the `implementer` agent.
Pass: tasklist path from Step 2.
Wait until all tasks are marked [x] and the build is green.
Record the full list of modified files.

**GATE 4** — Show me the build result and the list of modified files.
Ask: "Implementation complete. Run the reviewer? (yes/no)"

---

## Step 5 — Reviewer checks code

Use the Task tool to run the `reviewer` agent.
Pass: the list of modified files from Step 4.
Wait for the verdict.

If "Do not commit":
  Show me the blockers.
  Ask: "Blockers found. Fix automatically or manually? (auto/manual)"
  If auto: run implementer with a patch tasklist derived from the blockers. Repeat Step 5.
    Maximum 2 attempts, then stop and escalate to me.
  If manual: wait until I say "continue".

If "Ready to commit" or "Committable with warnings":
**GATE 5** — Ask: "Review passed. Run test-writer? (yes/no)"

---

## Step 6 — Test-writer writes tests

Use the Task tool to run the `test-writer` agent.
Pass: the list of implemented files from Step 4.
Wait for test files to be created and the build to pass.

---

## Final Summary

Print the pipeline report:
- ADR: <path>
- Tasklist: <path>
- Implemented files: <list>
- Review verdict: <verdict>
- Test files: <list>

Ask: "Pipeline complete. Commit? (yes/no)"
If yes: run git add -A && git commit -m "feat: $ARGUMENTS"