# Disclaimer

**ClaudeForge is experimental software under active development.** It is provided as-is,
without warranty of any kind, express or implied. The authors accept no responsibility for
damage to your Claude Code or Claude Desktop installations, configuration files, credentials,
or any other data arising from the use of this software.

## What this software does — and what that means for your data

ClaudeForge reads and writes the configuration files that govern how Claude Code and
Claude Desktop behave on your machine. A saved change lands directly on disk. A mis-saved
value can cause Claude to start with wrong settings, refuse to start at all, or silently
discard options you relied on.

The application ships with a backup-and-restore engine, and we exercise it in tests.
But no test suite can cover every combination of filesystem state, OS permission model,
concurrent process access, and configuration edge case that exists in the real world.
**Backup and restore have been tested — they have not been proven infallible.** If a
backup was made while a Claude process held a file lock, or if the archive was written to
a full disk mid-stream, the restore may be incomplete. Plan accordingly.

## Before you use ClaudeForge

1. **Make your own backup first.** Copy `~/.claude/` (and the Claude Desktop config
   directory for your platform) somewhere safe before making any significant changes.
   Do not rely solely on ClaudeForge's built-in backup for your only copy.
2. **Use the built-in backup feature** before major edit sessions. It exists precisely
   because configuration changes are hard to undo without one.
3. **Review the Save Changes dialog.** It shows exactly which keys will be written,
   removed, or modified — read it before clicking Save.
4. **Run a restore drill before you need it.** Restore from a backup to a scratch
   profile directory once, so you know it works on your machine before something goes wrong.

## Reporting bugs

Bug reports are actively encouraged and genuinely appreciated. If you encounter data
loss, an unexpected crash, or behavior that damages your Claude installation, please
open an issue on GitHub. Include:

- A clear description of the steps that led to the problem.
- The contents of the diagnostic log. ClaudeForge writes a rolling log next to the
  executable at `<exe-dir>/logs/app-YYYYMMDD-HH.txt`. The startup line printed to
  stderr names the exact path. Secret-bearing values are auto-redacted in the log
  before write, but pasting the log into a public issue is still your call — skim it
  for anything you'd rather not share.
- The configuration file(s) involved, if you can share them safely.

Your reports make this software safer for everyone who uses it. Don't assume someone
else has already reported an issue — they may not have.

---

*ClaudeForge is an independent tool and is not affiliated with or endorsed by Anthropic.*
