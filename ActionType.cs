namespace Orbital
{
    /// <summary>
    /// Defines the type of action to perform with the result.
    /// </summary>
    public enum ActionType
    {
        /// <summary>Replace selected text with the result</summary>
        Replace,
        
        /// <summary>Copy result to clipboard</summary>
        Copy,
        
        /// <summary>Show result in a popup window</summary>
        Popup,
        
        /// <summary>Open browser with selected text (no LLM)</summary>
        Browser,
        
        /// <summary>Copy selected text to clipboard directly (no LLM)</summary>
        DirectCopy,
        
        /// <summary>Cut selected text (no LLM)</summary>
        Cut,
        
        /// <summary>Paste clipboard content at cursor (no LLM)</summary>
        Paste,

        /// <summary>Simulate a key press; key name is stored in PromptFormat (no LLM)</summary>
        SimulateKey,

        /// <summary>Select all text in the focused control (Ctrl+A), then re-show the action bar (no LLM)</summary>
        SelectAll
    }

    /// <summary>
    /// Helper methods for ActionType enum.
    /// </summary>
    public static class ActionTypeExtensions
    {
        /// <summary>
        /// Attempts to convert a serialized string into an ActionType.
        /// </summary>
        public static bool TryFromString(string? value, out ActionType actionType)
        {
            switch (value?.Trim())
            {
                case "Replace":
                    actionType = ActionType.Replace;
                    return true;
                case "Copy":
                    actionType = ActionType.Copy;
                    return true;
                case "Popup":
                    actionType = ActionType.Popup;
                    return true;
                case "Browser":
                    actionType = ActionType.Browser;
                    return true;
                case "DirectCopy":
                    actionType = ActionType.DirectCopy;
                    return true;
                case "Cut":
                    actionType = ActionType.Cut;
                    return true;
                case "Paste":
                    actionType = ActionType.Paste;
                    return true;
                case "SimulateKey":
                    actionType = ActionType.SimulateKey;
                    return true;
                case "SelectAll":
                    actionType = ActionType.SelectAll;
                    return true;
                default:
                    actionType = ActionType.Popup;
                    return false;
            }
        }

        /// <summary>
        /// Checks if the action requires LLM processing.
        /// </summary>
        public static bool RequiresLlm(this ActionType actionType)
        {
            return actionType switch
            {
                ActionType.Replace => true,
                ActionType.Copy => true,
                ActionType.Popup => true,
                _ => false
            };
        }

        /// <summary>
        /// Converts a string to ActionType, maintaining backward compatibility.
        /// </summary>
        public static ActionType FromString(string? value)
        {
            return TryFromString(value, out ActionType actionType)
                ? actionType
                : ActionType.Popup;
        }

        /// <summary>
        /// Converts ActionType to string for serialization.
        /// </summary>
        public static string ToSerializedString(this ActionType actionType)
        {
            return actionType.ToString();
        }
    }
}
