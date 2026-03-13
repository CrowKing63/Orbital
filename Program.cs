using Velopack;

namespace Orbital;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must be called before any WPF initialization.
        // Handles install/update/uninstall hooks and exits early for those cases.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
