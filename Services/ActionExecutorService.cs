using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace Orbital
{
    public class ActionExecutorService
    {
        private readonly ILlmApiService? _llmService;

        public bool HasLlmService => _llmService != null;
        public ILlmApiService? LlmService => _llmService;

        public ActionExecutorService(ILlmApiService? llmService)
        {
            _llmService = llmService;
        }

        /// <summary>
        /// 액션을 실행합니다. 반드시 백그라운드 스레드(Task.Run)에서 호출하세요.
        /// 내부적으로 UI 업데이트는 Dispatcher를 통해 처리됩니다.
        /// </summary>
        public async Task ExecuteAsync(ActionProfile action, string selectedText)
        {
            if (action.IsSelectionRequired && string.IsNullOrEmpty(selectedText))
            {
                // Defensive runtime guard
                return;
            }

            // LLM을 사용하지 않는 유틸리티 액션들
            switch (action.ActionType)
            {
                case ActionType.Browser:
                    string url = $"https://www.google.com/search?q={Uri.EscapeDataString(selectedText)}";
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                    });
                    return;

                case ActionType.DirectCopy:
                    ClipboardHelper.CopyToClipboard(selectedText);
                    return;

                case ActionType.Cut:
                    ClipboardHelper.CopyToClipboard(selectedText);
                    ClipboardHelper.DeleteSelectedText();
                    return;

                case ActionType.Paste:
                    System.Threading.Thread.Sleep(150); // allow focus to return to target window after popup hides
                    ClipboardHelper.SimulatePaste();
                    return;
            }

            // LLM 액션
            if (_llmService == null)
            {
                throw new InvalidOperationException("API key is not configured for LLM actions.");
            }

            string prompt = action.PromptFormat.Replace("{text}", selectedText);
            string? systemPrompt = action.CleanOutput
                ? "Return only the processed result. Do not include any explanation, preamble, commentary, or extra text."
                : null;
            string result = await _llmService.CallApiAsync(prompt, systemPrompt);

            switch (action.ActionType)
            {
                case ActionType.Replace:
                    ClipboardHelper.ReplaceSelectedText(result);
                    break;

                case ActionType.Copy:
                    ClipboardHelper.CopyToClipboard(result);
                    break;

                case ActionType.Popup:
                default:
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var tooltip = new ResultTooltipWindow(result);
                        tooltip.Show();
                    });
                    break;
            }
        }
    }
}
