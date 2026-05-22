---
name: Bug report
about: Something isn't working correctly
labels: bug
---

## Describe the bug

A clear description of what the problem is.

## Steps to reproduce

1. Open the app
2. Navigate to …
3. Do …
4. See error

## Expected behaviour

What you expected to happen.

## Actual behaviour

What actually happened. Include error messages and stack traces if available.

## Environment

- **OS:** (e.g. Windows 11, macOS 15, Ubuntu 24.04)
- **App version:** (shown in the About page, or `--version`)
- **Claude Code version:** (run `claude --version` in a terminal)
- **.NET version:** (run `dotnet --version`)

## Log file (if relevant)

ClaudeForge writes a rolling log next to the executable at
`<exe-dir>/logs/app-YYYYMMDD-HH.txt`. The startup line printed to stderr names the
exact path. Secret-bearing values are auto-redacted in the log before write — skim
before pasting and trim to the relevant minutes if it's noisy.

## Screenshots

If applicable, add screenshots to help explain the problem.

## Additional context

Any other relevant information (project settings, custom profiles, etc.).
