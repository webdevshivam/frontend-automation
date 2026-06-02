using System.Collections.Generic;

namespace PlaywrightPrototype.models
{
    public class TestStep
    {
        public string action { get; set; } = "";
        public string selector { get; set; } = "";
        public string value { get; set; } = "";
        public int width { get; set; }
        public int height { get; set; }
        public string attribute { get; set; } = "";
        public string expected { get; set; } = "";
        public string key { get; set; } = "";
        public int count { get; set; }

        /// <summary>Human intent: "click decline cookie button", "type into email field"</summary>
        public string intent { get; set; } = "";

        /// <summary>Smart locator with self-healing fallback chain</summary>
        public ElementTarget? target { get; set; }

        /// <summary>Post-action verification: navigation, visible, modal_closed, page_changed, none</summary>
        public string verifyAfter { get; set; } = "";

        /// <summary>Dismiss cookie banners / modals before this step</summary>
        public bool dismissOverlays { get; set; }
    }
}
