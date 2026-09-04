namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Messages;

/// <summary>
/// Sent via <see cref="CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger"/> when
/// the user clicks an environment-variable token inside a
/// <see cref="Controls.LinkifiedTextBlock"/> description.
/// The receiver should navigate to the Environment editor section and highlight
/// <see cref="VarName"/>.
/// </summary>
public sealed record NavigateToEnvVarMessage(string VarName);