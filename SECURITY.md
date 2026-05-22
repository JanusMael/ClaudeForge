# Security Policy

## Reporting a vulnerability

If you believe you've found a security issue in ClaudeForge — anything that
could expose credentials, leak secrets, corrupt user configuration, or be used
to escalate privileges — please report it privately rather than opening a
public issue.

**Preferred channel:** [GitHub private vulnerability reporting](https://github.com/JanusMael/ClaudeForge/security/advisories/new).
This routes the report to the maintainers only, without disclosing it to the
public until a fix is ready.

**Alternative:** email **bbennewitz@gmail.com** with the subject line
`[ClaudeForge security]`. Encrypt with PGP if you prefer; key on request.

Please include:

- A clear description of the vulnerability and the conditions under which it
  reproduces.
- A proof-of-concept if you have one (sanitized — no real API keys or other
  secrets in the artefacts you attach).
- The version / commit you tested against.
- Your name + contact for credit, if you'd like to be acknowledged in the
  release notes.

## Response timeline

| Stage | Target |
|---|---|
| Acknowledgement of report | within 3 business days |
| Initial triage + severity rating | within 7 business days |
| Fix or mitigation plan | within 30 days for high-severity issues |

Severity follows the [CVSS 3.1](https://www.first.org/cvss/v3.1/specification-document)
qualitative ratings (None / Low / Medium / High / Critical). Critical issues
that can compromise user data or credentials are prioritised over feature
work; expect a faster response and a coordinated disclosure window.

## Disclosure policy

We follow a **coordinated disclosure** model:

1. You report privately via one of the channels above.
2. We acknowledge, triage, and work with you on a fix.
3. Once a patched release ships, we publish a security advisory crediting you
   (unless you ask to remain anonymous) and the CVE if one was assigned.

If 90 days pass after acknowledgement without a fix and without an explicit
extension agreed with you, you may disclose publicly — we'll do our best to
keep that scenario from happening.

## Out of scope

The following are not in scope for this policy (please don't open
vulnerability reports for them):

- Vulnerabilities in **third-party packages** (Avalonia, Semi.Avalonia,
  CommunityToolkit.Mvvm, Markdown.Avalonia, Skia, etc.) — report those
  upstream. ClaudeForge will pick up the fix on its next dependency-bump
  cycle (see [`.github/dependabot.yml`](./.github/dependabot.yml)).
- Vulnerabilities in **Claude Code / Claude Desktop** themselves — report
  those to Anthropic directly.
- Issues that require **physical access** to the user's machine or
  **administrative privileges already granted** to the attacker.
- Behaviour the user explicitly opts into (e.g. the `--dangerously-skip-permissions`
  flag in Claude Code; ClaudeForge surfaces the setting honestly with a
  standing red danger banner but does not prevent the user from enabling it).

## Privacy posture (for context)

ClaudeForge is a local-only tool. The app does not:

- Send telemetry or analytics anywhere.
- Phone home for updates (releases are pulled from GitHub explicitly by the
  user — see [`docs/AVALONIA-GOTCHAS.md`](./docs/AVALONIA-GOTCHAS.md) and
  [CLAUDE.md](./CLAUDE.md) for the no-auto-update rationale).
- Read or write files outside the user's `~/.claude/`, the open project's
  `.claude/`, and (for Sanitized backups) the user-chosen backup directory.

The diagnostic log written to `<exe-dir>/logs/app-YYYYMMDD-HH.txt` redacts
secret-bearing values through `SensitiveKeys` before any write — see
[CLAUDE.md § "User-action audit logging"](./CLAUDE.md) for the redaction
contract. If you find a code path where a secret leaks into a log, that
qualifies as a security issue and should be reported privately.

## Thank you

Researchers who report responsibly make this software safer for everyone.
We appreciate the time you take to investigate and disclose carefully.
