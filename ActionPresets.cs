using System.Collections.Generic;

namespace Orbital
{
    /// <summary>
    /// Built-in action templates users can load as a starting point when creating new actions.
    /// These are never auto-added to the user's action list — they exist only as a library.
    /// </summary>
    public static class ActionPresets
    {
        public static IReadOnlyList<ActionProfile> All { get; } = new List<ActionProfile>
        {
            // ── Utility (no LLM) ─────────────────────────────────────────────────
            new ActionProfile { Name = "Copy",        Icon = "\uE8C8", PromptFormat = "", ResultAction = "DirectCopy", RequiresSelection = true  },
            new ActionProfile { Name = "Cut",         Icon = "\uE8C6", PromptFormat = "", ResultAction = "Cut",        RequiresSelection = true  },
            new ActionProfile { Name = "Paste",       Icon = "\uE77F", PromptFormat = "", ResultAction = "Paste",      RequiresSelection = false },
            new ActionProfile { Name = "Search",      Icon = "\uE721", PromptFormat = "", ResultAction = "Browser",    RequiresSelection = true  },

            // ── Translation ───────────────────────────────────────────────────────
            new ActionProfile { Name = "Translate to Korean",  Icon = "\uE8C1", PromptFormat = "Translate the following to Korean organically: {text}",  ResultAction = "Replace", RequiresSelection = true, CleanOutput = true },
            new ActionProfile { Name = "Translate to English", Icon = "\uE8C1", PromptFormat = "Translate the following to English naturally: {text}",    ResultAction = "Replace", RequiresSelection = true, CleanOutput = true },
            new ActionProfile { Name = "Translate to Japanese",Icon = "\uE8C1", PromptFormat = "Translate the following to Japanese naturally: {text}",   ResultAction = "Replace", RequiresSelection = true, CleanOutput = true },

            // ── Writing ───────────────────────────────────────────────────────────
            new ActionProfile { Name = "Polish",      Icon = "\uE70F", PromptFormat = "Correct grammar and make this sound professional: {text}",          ResultAction = "Replace", RequiresSelection = true, CleanOutput = true },
            new ActionProfile { Name = "Formal",      Icon = "\uE8D4", PromptFormat = "Rewrite the following in a formal, professional tone: {text}",       ResultAction = "Replace", RequiresSelection = true, CleanOutput = true },
            new ActionProfile { Name = "Casual",      Icon = "\uE76E", PromptFormat = "Rewrite the following in a casual, friendly tone: {text}",           ResultAction = "Replace", RequiresSelection = true, CleanOutput = true },
            new ActionProfile { Name = "Shorter",     Icon = "\uE8A4", PromptFormat = "Make the following more concise without losing meaning: {text}",     ResultAction = "Replace", RequiresSelection = true, CleanOutput = true },
            new ActionProfile { Name = "Bullet Points",Icon = "\uE8FD",PromptFormat = "Convert the following into a clear bullet-point list: {text}",      ResultAction = "Replace", RequiresSelection = true, CleanOutput = true },

            // ── Analysis (Popup) ──────────────────────────────────────────────────
            new ActionProfile { Name = "Summarize",   Icon = "\uE7C3", PromptFormat = "Summarize the following in 3 lines: {text}",                        ResultAction = "Popup", RequiresSelection = true },
            new ActionProfile { Name = "Explain",     Icon = "\uE82D", PromptFormat = "Explain the following in simple terms: {text}",                     ResultAction = "Popup", RequiresSelection = true },
            new ActionProfile { Name = "ELI5",        Icon = "\uE899", PromptFormat = "Explain the following as if I'm 5 years old: {text}",               ResultAction = "Popup", RequiresSelection = true },
            new ActionProfile { Name = "Fix Code",    Icon = "\uE943", PromptFormat = "Find and fix bugs in the following code, then return only the corrected code: {text}", ResultAction = "Replace", RequiresSelection = true, CleanOutput = true },
        };
    }
}
