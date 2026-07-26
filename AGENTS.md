# AGENTS.md

This file defines default working rules for coding agents in this repository.

## Project Context

- Engine: Unity
- Primary language: C#
- Custom package development: com.mirov.manifestor in SharedPackages

## Style Rules

- When writing Unity class (MonoBehaviour, EditorWindow and similar), always put Unity methods (Awake, Start, Update, ...) to the top of the class just below fields declaration

## Safety Rules

- Do not run destructive git/file commands unless explicitly requested.
- Do not revert unrelated user changes.
- If unexpected modifications appear in files being edited, stop and ask before proceeding.

## Validation

- After edits, run lightweight checks when possible:
  - Search for stale references (`rg`).
  - Verify compile-sensitive renames/usages in touched files.
- If full Unity compile/runtime verification is not possible in terminal, clearly state that.

- Do not add tests unless explicitely requested

## Communication Preferences

- Be concise and implementation-focused.
- For substantial changes, provide:
  - What changed
  - Why it changed
  - Any required Unity Inspector or prefab wiring
- Include file paths for every changed file.

## Review Preferences

- In reviews, prioritize:
  - Bugs/regressions
  - Missing edge-case handling
  - Missing tests or validation coverage
- Keep summaries short; findings first.
