using System;
using System.Threading;
using System.Windows;

namespace Orbit
{
    public partial class App : Application
    {
        private RadialMenuWindow _radialMenu;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Background mode (prevent app from closing when windows are hidden)
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _radialMenu = new RadialMenuWindow();

            SystemHookManager.OnMouseUp += SystemHookManager_OnMouseUp;
            SystemHookManager.StartMouseHook();
        }

        private void SystemHookManager_OnMouseUp(object sender, SystemHookManager.MousePoint e)
        {
            // Do not block the hook thread. Run asynchronously.
            System.Threading.Tasks.Task.Run(() =>
            {
                // Simple delay to allow OS to finish text selection process
                Thread.Sleep(50);
                
                string selectedText = ClipboardHelper.GetSelectedText();

                if (!string.IsNullOrWhiteSpace(selectedText))
                {
                    Dispatcher.Invoke(() =>
                    {
                        _radialMenu.ShowAtCursor(e.X, e.Y, selectedText);
                    });
                }
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemHookManager.StopMouseHook();
            base.OnExit(e);
        }
    }
}
