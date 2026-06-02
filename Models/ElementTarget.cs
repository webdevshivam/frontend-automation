using System.Collections.Generic;

namespace PlaywrightPrototype.models
{
    /// <summary>
    /// Intent-driven element locator with self-healing fallback chain.
    /// Prefers GetByRole / GetByLabel / data-testid / id — never nth-of-type.
    /// </summary>
    public class ElementTarget
    {
        public string Intent { get; set; } = "";
        public string Role { get; set; } = "";
        public string Name { get; set; } = "";
        public string TestId { get; set; } = "";
        public string ElementId { get; set; } = "";
        public string Label { get; set; } = "";
        public string Placeholder { get; set; } = "";
        public string Tag { get; set; } = "";

        /// <summary>Ordered fallback descriptors: role:button:Submit, testid:foo, id:bar, label:Baz, css:h1</summary>
        public List<string> FallbackChain { get; set; } = new();

        public string PrimaryStrategy => FallbackChain.Count > 0 ? FallbackChain[0] : "(none)";

        public string Describe()
        {
            if (!string.IsNullOrEmpty(Role) && !string.IsNullOrEmpty(Name))
                return $"GetByRole({Role}, Name=\"{Name}\")";
            if (!string.IsNullOrEmpty(TestId))
                return $"GetByTestId(\"{TestId}\")";
            if (!string.IsNullOrEmpty(ElementId))
                return $"#{ElementId}";
            if (!string.IsNullOrEmpty(Label))
                return $"GetByLabel(\"{Label}\")";
            if (!string.IsNullOrEmpty(Placeholder))
                return $"GetByPlaceholder(\"{Placeholder}\")";
            return Intent;
        }
    }

    public class InteractiveElement
    {
        public ElementTarget Target { get; set; } = new();
        public string Tag { get; set; } = "";
        public string Text { get; set; } = "";
        public bool IsVisible { get; set; }
        public bool IsEnabled { get; set; }
        public string Href { get; set; } = "";
        public string InputType { get; set; } = "";
    }

    public class PageStateInfo
    {
        public bool HasCookieBanner { get; set; }
        public bool HasModal { get; set; }
        public bool HasLoader { get; set; }
        public bool HasDropdown { get; set; }
        public List<ElementTarget> OverlayDismissTargets { get; set; } = new();
    }

    public class FailureInsight
    {
        public string TestName { get; set; } = "";
        public string Category { get; set; } = "";
        public string Reason { get; set; } = "";
        public string GeneratedSelector { get; set; } = "";
        public string SuggestedFix { get; set; } = "";
        public string Intent { get; set; } = "";
    }
}
