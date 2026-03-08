namespace Orbit
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
        Paste
    }

    /// <summary>
    /// Helper methods for ActionType enum.
    /// </summary>
    public static class ActionTypeExtensions
    {
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
        public static ActionType FromString(string value)
        {
            return value switch
            {
                "Replace" => ActionType.Replace,
                "Copy" => ActionType.Copy,
                "Popup" => ActionType.Popup,
                "Browser" => ActionType.Browser,
                "DirectCopy" => ActionType.DirectCopy,
                "Cut" => ActionType.Cut,
                "Paste" => ActionType.Paste,
                _ => ActionType.Popup // Default fallback
            };
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
