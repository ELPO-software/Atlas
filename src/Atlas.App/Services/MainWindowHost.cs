using Avalonia.Controls;

namespace Atlas.App.Services;

/// <summary>Gives the services that need a window access to the main one, which is created after them.</summary>
internal sealed class MainWindowHost
{
    public Window? Window { get; set; }
}
