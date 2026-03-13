using Squirrel;

namespace Orbital;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must be called before any WPF initialization.
        // Handles install/update/uninstall hooks and exits early for those cases.
        SquirrelAwareApp.HandleEvents(
            onInitialInstall: (v, tools) =>
            {
                tools.CreateShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
            },
            onAppUpdate: (v, tools) =>
            {
                tools.CreateShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
            },
            onAppUninstall: (v, tools) =>
            {
                tools.RemoveShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
            }
        );

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
