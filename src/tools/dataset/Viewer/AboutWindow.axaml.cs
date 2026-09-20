using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace JassCardEye.Dataset.Viewer;

/// <summary>The window behind "About JassCardEye Dataset Tool" in the app menu.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = VersionLine();
    }

    // "Version 1.0.0 (a1b2c3d)": the SDK appends the commit the build came from to the informational
    // version, and that commit is what tells two builds of the same version apart.
    private static string VersionLine()
    {
        var info = typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info)) return "";

        var parts = info.Split('+', 2);
        var commit = parts.Length == 2 ? $" ({parts[1][..System.Math.Min(7, parts[1].Length)]})" : "";
        return $"Version {parts[0]}{commit}";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
