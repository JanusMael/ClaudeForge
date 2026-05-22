using System.Runtime.CompilerServices;

// Allow the library test project to inspect internal helpers — currently
// FileDrop.ParseExtensions / HasAcceptedExtension for the behaviour-level
// unit tests.  Internal (not public) because these are implementation
// details of the attached behaviour: callers attach the behaviour via
// AXAML and don't reach into the helpers directly.
[assembly: InternalsVisibleTo("LayeredEditors.Avalonia.Tests")]