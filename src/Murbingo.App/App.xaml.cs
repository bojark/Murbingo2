using System.Windows;

namespace Murbingo.App;
/// <summary>Starts the WPF host while keeping presentation separate from game and physics modules.</summary>
public partial class App : Application
{
    /// <summary>Creates the host window after application resources have loaded so styles and icons are available.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
