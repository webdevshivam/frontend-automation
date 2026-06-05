using System.Collections.Generic;

namespace PlaywrightPrototype.models
{
    public class PageAnalysis
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public string MetaDescription { get; set; } = "";
        public string MetaViewport { get; set; } = "";
        public bool HasMetaDescription { get; set; }
        public bool HasMetaViewport { get; set; }

        public List<HeadingInfo> Headings { get; set; } = new();
        public List<ImageInfo> Images { get; set; } = new();
        public List<LinkInfo> Links { get; set; } = new();
        public List<InputInfo> Inputs { get; set; } = new();
        public List<ButtonInfo> Buttons { get; set; } = new();
        public List<InteractiveElement> InteractiveElements { get; set; } = new();
        public PageStateInfo PageState { get; set; } = new();
        public int FormCount { get; set; }
        public int ParagraphCount { get; set; }
        public int InteractiveElementCount { get; set; }

        public List<DetectedEdgeCase> EdgeCases { get; set; } = new();
        public PageFeatureVector Features { get; set; } = new();
    }

    public class HeadingInfo
    {
        public string Level { get; set; } = "";
        public string Text { get; set; } = "";
        public ElementTarget Target { get; set; } = new();
    }

    


    public class ImageInfo
    {
        public string Src { get; set; } = "";
        public bool HasAlt { get; set; }
        public string Alt { get; set; } = "";
        public bool IsLoaded { get; set; }
        public ElementTarget Target { get; set; } = new();
    }




    public class LinkInfo
    {
        public string Text { get; set; } = "";
        public string Href { get; set; } = "";
        public bool IsEmptyHref { get; set; }
        public bool IsHashOnly { get; set; }
        public bool IsExternal { get; set; }
        public ElementTarget Target { get; set; } = new();
    }

    public class InputInfo
    {
        public string Type { get; set; } = "text";
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ElementTarget Target { get; set; } = new();
        public bool HasLabel { get; set; }
        public bool HasAriaLabel { get; set; }
        public bool HasPlaceholder { get; set; }
        public bool IsHidden { get; set; }
    }

    public class ButtonInfo
    {
        public string Text { get; set; } = "";
        public string Id { get; set; } = "";
        public ElementTarget Target { get; set; } = new();
        public bool HasAccessibleName { get; set; }
    }

    public class DetectedEdgeCase
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public double Severity { get; set; }
    }

    /// <summary>
    /// Normalized feature vector (0.0–1.0) extracted from HTML — used by the decision engine.
    /// </summary>
    public class PageFeatureVector
    {
        public double ContentDensity { get; set; }
        public double HeadingStructureQuality { get; set; }
        public double SeoCompleteness { get; set; }
        public double AccessibilityRisk { get; set; }
        public double ResponsiveRisk { get; set; }
        public double ImageIntegrity { get; set; }
        public double LinkQuality { get; set; }
        public double FormComplexity { get; set; }
        public double InteractivityLevel { get; set; }
    }
}
