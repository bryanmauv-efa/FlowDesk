using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using TermServMultiScreen.Core;
using TermServMultiScreen.Services;

namespace TermServMultiScreen.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage() => InitializeComponent();

    public string VersionText =>
        $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} — outil interne efa";

    public string UsageText => CommandLine.Usage;

    public string StackText =>
        "C# · .NET 10 · WinUI 3 · Windows App SDK · CommunityToolkit.Mvvm · Windows Community Toolkit. "
        + "Énumération des écrans par EnumDisplayMonitors et EnumDisplaySettingsEx : pixels réels, insensible à la mise à l'échelle.";

    public string MachineText =>
        $"{Environment.MachineName} · {Environment.OSVersion.VersionString} · "
        + $"{AppServices.Monitors.Monitors.Count} écran(s) détecté(s)";
}
