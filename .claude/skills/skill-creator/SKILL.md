---
name: skill-creator
description: Create new skills, modify and improve existing skills, and measure skill performance. Use when users want to create a skill from scratch, edit, or optimize an existing skill, run evals to test a skill, benchmark skill performance with variance analysis, or optimize a skill's description for better triggering accuracy.
---

# Skill Creator

A skill for creating new skills and iteratively improving them.

At a high level, the process of creating a skill goes like this:

- Decide what you want the skill to do and roughly how it should do it
- Write a draft of the skill
- Create a few test prompts and run claude-with-access-to-the-skill on them
- Help the user evaluate the results both qualitatively and quantitatively
- Rewrite the skill based on feedback
- Repeat until satisfied

## Communicating with the user

Pay attention to context cues to understand how to phrase your communication. The default case:
- "evaluation" and "benchmark" are borderline, but OK
- for "JSON" and "assertion" you want to see serious cues from the user that they know what those things are before using them without explaining them

---

## Creating a skill

### Capture Intent

Start by understanding the user's intent. Extract answers from the conversation history first — the tools used, the sequence of steps, corrections the user made, input/output formats observed.

1. What should this skill enable Claude to do?
2. When should this skill trigger? (what user phrases/contexts)
3. What's the expected output format?
4. Should we set up test cases to verify the skill works?

### Interview and Research

Proactively ask questions about edge cases, input/output formats, example files, success criteria, and dependencies. Wait to write test prompts until you've got this part ironed out.

### Write the SKILL.md

Based on the user interview, fill in these components:

- **name**: Skill identifier
- **description**: When to trigger, what it does. Make it a little "pushy" so it triggers reliably.
- **compatibility**: Required tools, dependencies (optional, rarely needed)
- **the rest of the skill**

### Skill Writing Guide

#### Anatomy of a Skill

```
skill-name/
├── SKILL.md (required)
│   ├── YAML frontmatter (name, description required)
│   └── Markdown instructions
└── Bundled Resources (optional)
    ├── scripts/    - Executable code for deterministic/repetitive tasks
    ├── references/ - Docs loaded into context as needed
    └── assets/     - Files used in output (templates, icons, fonts)
```

#### Progressive Disclosure

Skills use a three-level loading system:
1. **Metadata** (name + description) - Always in context (~100 words)
2. **SKILL.md body** - In context whenever skill triggers (<500 lines ideal)
3. **Bundled resources** - As needed

**Key patterns:**
- Keep SKILL.md under 500 lines
- Reference files clearly from SKILL.md with guidance on when to read them
- For large reference files (>300 lines), include a table of contents

#### Writing Patterns

Prefer using the imperative form in instructions.

**Defining output formats:**
```markdown
## Report structure
ALWAYS use this exact template:
# [Title]
## Executive summary
## Key findings
## Recommendations
```

**Examples pattern:**
```markdown
## Commit message format
**Example 1:**
Input: Added user authentication with JWT tokens
Output: feat(auth): implement JWT-based authentication
```

### Writing Style

Try to explain to the model *why* things are important. Use theory of mind and try to make the skill general. Start by writing a draft and then look at it with fresh eyes and improve it.

### Test Cases

After writing the skill draft, come up with 2-3 realistic test prompts. Save test cases to `evals/evals.json`:

```json
{
  "skill_name": "example-skill",
  "evals": [
    {
      "id": 1,
      "prompt": "User's task prompt",
      "expected_output": "Description of expected result",
      "files": []
    }
  ]
}
```

## Running and evaluating test cases (Claude.ai / Windows)

Since you're not in Claude Code with subagents, adapt the workflow as follows:

**Running test cases**: For each test case, read the skill's SKILL.md, then follow its instructions to accomplish the test prompt yourself, one at a time.

**Reviewing results**: Present results directly in the conversation. For each test case, show the prompt and the output. Ask for feedback inline: "How does this look? Anything you'd change?"

**The iteration loop**: Improve the skill, rerun the test cases, ask for feedback — repeat until satisfied.

**Description optimization**: Requires the `claude` CLI tool which is only available in Claude Code. Skip it if you're on Claude.ai.

---

## Improving the skill

### How to think about improvements

1. **Generalize from the feedback.** Don't make fiddly overfitting changes — try to understand the broader intent and improve the skill for general use.
2. **Keep the prompt lean.** Remove things that aren't pulling their weight.
3. **Explain the why.** Try to explain the reasoning behind instructions, not just issue commands.
4. **Look for repeated work across test cases.** If you notice patterns across runs, bundle helper scripts into the skill.

### The iteration loop

After improving the skill:

1. Apply your improvements to the skill
2. Rerun all test cases
3. Ask for user feedback
4. Read feedback, improve again, repeat

Keep going until:
- The user says they're happy
- The feedback is all empty (everything looks good)
- You're not making meaningful progress

---

## Description Optimization (Claude Code only)

After the skill is in good shape, you can optimize the description field to improve triggering accuracy. This requires the `claude` CLI and `run_loop.py` script — only available in Claude Code environments.

---

## Core loop summary

- Figure out what the skill is about
- Draft or edit the skill
- Run test prompts (yourself on Claude.ai, via subagents in Claude Code)
- Evaluate the outputs with the user
- Repeat until satisfied
- Package the final skill

Good luck!
