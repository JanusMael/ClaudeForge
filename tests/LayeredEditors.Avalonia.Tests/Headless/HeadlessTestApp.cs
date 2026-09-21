using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Headless;

[assembly: AvaloniaTestApplication(typeof(HeadlessTestApp))]

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Headless;

/// <summary>
/// Avalonia application for the headless tests in this assembly: dispatcher, layout and
/// input, no rendering. Tests obtain the shared session through
/// <see cref="HeadlessUnitTestSession.GetOrStartForAssembly"/> and dispatch a
/// <em>synchronous</em> body onto its UI thread — an <c>async</c> body returns a
/// <c>Task&lt;Task&gt;</c> nothing awaits, which makes the test unable to fail (see the
/// headless section of the root <c>AGENTS.md</c>).
/// </summary>
/// <remarks>
/// Unlike the other headless apps in this repo, this one loads a theme: the single
/// <c>SemiBundle.axaml</c> include that is the whole of what ClaudeForge's and
/// OpenCodeForge's <c>App.axaml</c> take from this library. Tests here assert on control
/// TEMPLATES — what a default template contributes to the automation tree — so a bare
/// application would leave every control untemplated and the assertions with nothing to
/// look at. Loading exactly the host include, and nothing app-specific, means a pass here
/// is a statement about every host that takes the bundle.
/// </remarks>
public sealed class HeadlessTestApp : Application
{
    private const string ThemeBundleUri = "avares://LayeredEditors.Avalonia/Themes/SemiBundle.axaml";

    public override void Initialize()
    {
        Uri uri = new(ThemeBundleUri);
        Styles.Add(new StyleInclude(uri) { Source = uri });
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<HeadlessTestApp>()
                         .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
    }
}
