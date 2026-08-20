---
name: workflow-wait-for-instructions
description: How the user wants me to pace work on the AmaScan project — wait, don't jump ahead, don't build
metadata:
  type: feedback
---

The user drives this work step by step. Do exactly what is asked and then stop and wait for the next instruction — do not proactively make extra edits, wire up additional code, or run builds.

**Why:** The user is methodically reviewing each change and will build/test themselves. Jumping ahead (e.g. running `dotnet build`, editing files not asked for) wastes their time and gets rejected.

**How to apply:**
- Never run a build — the user builds themselves.
- After completing the specific change requested, report it and wait. Don't tack on related changes unless asked.
- When something is ambiguous or I notice an issue, raise it and ask rather than fixing it unprompted.
