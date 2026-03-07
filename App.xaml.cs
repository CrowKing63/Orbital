using System;
using System.Drawing;
using System.Threading;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace Orbit
{
    public partial class App : System.Windows.Application
    {
        private RadialMenuWindow _radialMenu = null!;
        private WinForms.NotifyIcon _notifyIcon = null!;
        private ActionExecutorService? _actionExecutor;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            SettingsManager.LoadSettings();
            RebuildActionExecutor();
            InitializeTrayIcon();

            _radialMenu = new RadialMenuWindow(_actionExecutor);

            SystemHookManager.OnMouseUp += SystemHookManager_OnMouseUp;
            SystemHookManager.OnLongPress += SystemHookManager_OnLongPress;
            SystemHookManager.StartMouseHook();
        }

        private void RebuildActionExecutor()
        {
            string apiKey = SettingsManager.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                _actionExecutor = null;
                return;
            }
            string baseUrl = string.IsNullOrWhiteSpace(SettingsManager.CurrentSettings.ApiBaseUrl)
                ? "https://api.openai.com/v1"
                : SettingsManager.CurrentSettings.ApiBaseUrl;
            string modelName = string.IsNullOrWhiteSpace(SettingsManager.CurrentSettings.ModelName)
                ? "gpt-4o-mini"
                : SettingsManager.CurrentSettings.ModelName;
            _actionExecutor = new ActionExecutorService(new OpenAiApiService(apiKey, baseUrl, modelName));
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new WinForms.NotifyIcon
            {
                Text = "Orbit - 텍스트 AI 도우미",
                Icon = SystemIcons.Application,
                Visible = true
            };

            var menu = new WinForms.ContextMenuStrip();

            menu.Items.Add("설정", null, (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var win = new SettingsWindow();
                    win.ShowDialog();
                    // 설정 창 닫힌 후 API 키 변경 반영
                    RebuildActionExecutor();
                    _radialMenu.UpdateActionExecutor(_actionExecutor);
                });
            });

            menu.Items.Add(new WinForms.ToolStripSeparator());

            menu.Items.Add("종료", null, (s, e) =>
            {
                _notifyIcon.Visible = false;
                Dispatcher.Invoke(Shutdown);
            });

            _notifyIcon.ContextMenuStrip = menu;

            // 더블클릭으로도 설정 열기
            _notifyIcon.DoubleClick += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var win = new SettingsWindow();
                    win.ShowDialog();
                    RebuildActionExecutor();
                    _radialMenu.UpdateActionExecutor(_actionExecutor);
                });
            };
        }

        private void SystemHookManager_OnMouseUp(object? sender, SystemHookManager.MousePoint e)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
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

        private void SystemHookManager_OnLongPress(object? sender, SystemHookManager.MousePoint e)
        {
            // 롱프레스: 텍스트 선택 없이 팝업 표시 (붙여넣기 등 비-LLM 액션용)
            Dispatcher.Invoke(() =>
            {
                _radialMenu.ShowAtCursor(e.X, e.Y, string.Empty);
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemHookManager.StopMouseHook();
            _notifyIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
