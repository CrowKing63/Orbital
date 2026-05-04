using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

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
        public async Task ExecuteAsync(ActionProfile action, string selectedText, Rect? actionBarBoundsDip = null)
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
                    SoundHelper.PlayActionSound();
                    return;

                case ActionType.DirectCopy:
                    ClipboardHelper.CopyToClipboard(selectedText);
                    SoundHelper.PlayActionSound();
                    return;

                case ActionType.Cut:
                    ClipboardHelper.CopyToClipboard(selectedText);
                    ClipboardHelper.DeleteSelectedText();
                    SoundHelper.PlayActionSound();
                    return;

                case ActionType.Paste:
                    System.Threading.Thread.Sleep(150); // allow focus to return to target window after popup hides
                    ClipboardHelper.SimulatePaste();
                    SoundHelper.PlayActionSound();
                    return;

                case ActionType.SimulateKey:
                    ClipboardHelper.SimulateKey(action.PromptFormat ?? string.Empty);
                    SoundHelper.PlayActionSound();
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
                    var cursor = Cursor.Position;
                    Point cursorDip = new(cursor.X, cursor.Y);
                    Rect? selectionDip = TryGetSelectionBoundsAtCursorDip(cursorDip);
                    var anchors = new PopupAnchorContext(cursorDip, selectionDip, actionBarBoundsDip);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var tooltip = new ResultTooltipWindow(result, anchors);
                        tooltip.Show();
                    });
                    break;
            }
            SoundHelper.PlayActionSound();
        }

        private static Rect? TryGetSelectionBoundsAtCursorDip(Point cursorDip)
        {
            try
            {
                var element = AutomationElement.FromPoint(cursorDip);
                if (element == null)
                {
                    return null;
                }

                if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object patternObj) || patternObj is not TextPattern textPattern)
                {
                    return null;
                }

                TextPatternRange[] ranges = textPattern.GetSelection();
                if (ranges.Length == 0)
                {
                    return null;
                }

                Rect[] rects = ranges[0].GetBoundingRectangles();
                if (rects.Length == 0)
                {
                    return null;
                }

                double minX = double.MaxValue;
                double minY = double.MaxValue;
                double maxX = double.MinValue;
                double maxY = double.MinValue;

                foreach (Rect rect in rects)
                {
                    double x = rect.X;
                    double y = rect.Y;
                    double w = rect.Width;
                    double h = rect.Height;
                    if (w <= 0 || h <= 0)
                    {
                        continue;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x + w);
                    maxY = Math.Max(maxY, y + h);
                }

                if (minX == double.MaxValue)
                {
                    return null;
                }

                return new Rect(new Point(minX, minY), new Point(maxX, maxY));
            }
            catch
            {
                return null;
            }
        }
    }
}
