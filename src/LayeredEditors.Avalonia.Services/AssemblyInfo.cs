using System.Runtime.CompilerServices;

// Allow the App test project to inspect internal members of ShellLauncher
// (BuildWindowsTerminalPsi, BuildDirectPowerShellPsi, Probe* wrappers).
[assembly: InternalsVisibleTo("ClaudeForge.Tests")]