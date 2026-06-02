using PlaywrightPrototype.models;
using PlaywrightPrototype.Engine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlaywrightPrototype.AI
{
    /// <summary>
    /// Local AI test generator: extracts HTML features, detects edge cases via a weighted
    /// decision model, and produces Playwright test cases without external APIs.
    /// </summary>
    public class IntelligentTestGenerator
    {
        private readonly PageAnalysis _page;
        private readonly List<TestCase> _tests = new();

        public IntelligentTestGenerator(PageAnalysis page)
        {
            _page = page ?? throw new ArgumentNullException(nameof(page));
        }

        public List<TestCase> Generate()
        {
            ExtractFeatures();
            DetectEdgeCases();
            GenerateCoreTests();
            GenerateEdgeCaseTests();
            GenerateAdaptiveTests();

            return _tests;
        }

        public string BuildAnalysisReport()
        {
            var f = _page.Features;
            var lines = new List<string>
            {
                $"URL: {_page.Url}",
                $"Title: {_page.Title}",
                "",
                "── Feature Vector (ML extraction) ──",
                $"  ContentDensity          : {f.ContentDensity:F2}",
                $"  HeadingStructureQuality : {f.HeadingStructureQuality:F2}",
                $"  SeoCompleteness         : {f.SeoCompleteness:F2}",
                $"  AccessibilityRisk       : {f.AccessibilityRisk:F2}",
                $"  ResponsiveRisk          : {f.ResponsiveRisk:F2}",
                $"  ImageIntegrity          : {f.ImageIntegrity:F2}",
                $"  LinkQuality             : {f.LinkQuality:F2}",
                $"  FormComplexity          : {f.FormComplexity:F2}",
                $"  InteractivityLevel      : {f.InteractivityLevel:F2}",
                "",
                $"── Detected Edge Cases ({_page.EdgeCases.Count}) ──"
            };

            foreach (var ec in _page.EdgeCases.OrderByDescending(e => e.Severity))
                lines.Add($"  [{ec.Category}] {ec.Description} (severity={ec.Severity:F2})");

            lines.Add("");
            lines.Add($"── Generated Tests ({_tests.Count}) ──");
            foreach (var t in _tests)
                lines.Add($"  [{t.category}] {t.name}");

            return string.Join("\n", lines);
        }

        // ── Feature extraction (ML-style normalization) ────────────────────────

        private void ExtractFeatures()
        {
            var p = _page;
            var f = p.Features;

            int contentElements = p.Headings.Count + p.ParagraphCount + p.Images.Count + p.Links.Count;
            f.ContentDensity = Clamp(contentElements / 50.0);

            int h1Count = p.Headings.Count(h => h.Level == "h1");
            int totalHeadings = p.Headings.Count;
            if (totalHeadings == 0)
                f.HeadingStructureQuality = 0;
            else if (h1Count == 1)
                f.HeadingStructureQuality = 0.9 + (Math.Min(totalHeadings, 10) / 100.0);
            else if (h1Count == 0)
                f.HeadingStructureQuality = 0.3;
            else
                f.HeadingStructureQuality = 0.5;

            double seoScore = 0;
            if (!string.IsNullOrWhiteSpace(p.Title)) seoScore += 0.4;
            if (p.HasMetaDescription) seoScore += 0.3;
            if (p.HasMetaViewport) seoScore += 0.3;
            f.SeoCompleteness = seoScore;

            double a11yRisk = 0;
            if (p.Images.Any(i => !i.HasAlt)) a11yRisk += 0.25;
            if (p.Images.Any(i => !i.IsLoaded)) a11yRisk += 0.15;
            if (p.Inputs.Any(i => !i.IsHidden && !i.HasLabel && !i.HasAriaLabel && !i.HasPlaceholder)) a11yRisk += 0.25;
            if (p.Buttons.Any(b => !b.HasAccessibleName)) a11yRisk += 0.15;
            if (h1Count != 1) a11yRisk += 0.2;
            f.AccessibilityRisk = Clamp(a11yRisk);

            f.ResponsiveRisk = p.HasMetaViewport ? 0.2 : 0.85;

            if (p.Images.Count == 0)
                f.ImageIntegrity = 1.0;
            else
            {
                double loaded = p.Images.Count(i => i.IsLoaded) / (double)p.Images.Count;
                double altOk = p.Images.Count(i => i.HasAlt) / (double)p.Images.Count;
                f.ImageIntegrity = (loaded + altOk) / 2.0;
            }

            if (p.Links.Count == 0)
                f.LinkQuality = 1.0;
            else
            {
                double valid = p.Links.Count(l => !l.IsEmptyHref) / (double)p.Links.Count;
                f.LinkQuality = valid;
            }

            var visibleInputs = p.Inputs.Where(i => !i.IsHidden).ToList();
            f.FormComplexity = visibleInputs.Count == 0 ? 0 : Clamp(visibleInputs.Count / 8.0);
            f.InteractivityLevel = Clamp(p.InteractiveElementCount / 30.0);
        }

        // ── Edge-case detection (classification thresholds) ────────────────────

        private void DetectEdgeCases()
        {
            var p = _page;

            if (string.IsNullOrWhiteSpace(p.Title))
                AddEdge("no-title", "seo", "Page has empty or missing <title>", 0.9);

            if (p.Title.Length > 70)
                AddEdge("long-title", "seo", $"Page title exceeds 70 chars ({p.Title.Length}) — SEO truncation risk", 0.5);

            if (!p.HasMetaViewport)
                AddEdge("no-viewport", "responsive", "Missing meta viewport tag — mobile layout may break", 0.95);

            if (!p.HasMetaDescription)
                AddEdge("no-meta-desc", "seo", "Missing meta description tag", 0.7);

            int h1Count = p.Headings.Count(h => h.Level == "h1");
            if (h1Count == 0)
                AddEdge("no-h1", "accessibility", "No H1 heading — document outline failure", 0.85);
            else if (h1Count > 1)
                AddEdge("multiple-h1", "accessibility", $"Multiple H1 headings ({h1Count}) — SEO/a11y issue", 0.75);

            var missingAlt = p.Images.Where(i => !i.HasAlt).ToList();
            if (missingAlt.Count > 0)
                AddEdge("missing-alt", "image", $"{missingAlt.Count} image(s) missing alt attribute", 0.8);

            var broken = p.Images.Where(i => !i.IsLoaded).ToList();
            if (broken.Count > 0)
                AddEdge("broken-images", "image", $"{broken.Count} broken/unloaded image(s)", 0.85);

            var emptyLinks = p.Links.Where(l => l.IsEmptyHref).ToList();
            if (emptyLinks.Count > 0)
                AddEdge("empty-links", "functional", $"{emptyLinks.Count} link(s) with empty href", 0.8);

            var hashLinks = p.Links.Where(l => l.IsHashOnly && !l.IsEmptyHref).ToList();
            if (hashLinks.Count > 0)
                AddEdge("hash-links", "functional", $"{hashLinks.Count} link(s) with hash-only href (#)", 0.4);

            var unlabeled = p.Inputs.Where(i => !i.IsHidden && !i.HasLabel && !i.HasAriaLabel && !i.HasPlaceholder).ToList();
            if (unlabeled.Count > 0)
                AddEdge("unlabeled-inputs", "accessibility", $"{unlabeled.Count} form input(s) without label/aria/placeholder", 0.85);

            var unnamedButtons = p.Buttons.Where(b => !b.HasAccessibleName).ToList();
            if (unnamedButtons.Count > 0)
                AddEdge("unnamed-buttons", "accessibility", $"{unnamedButtons.Count} button(s) without accessible name", 0.8);

            if (p.ParagraphCount == 0 && p.Headings.Count == 0)
                AddEdge("empty-content", "functional", "Page appears to have minimal text content", 0.6);

            if (p.Links.Count == 0 && p.Buttons.Count == 0)
                AddEdge("no-navigation", "functional", "No links or buttons — limited user interaction", 0.5);
        }

        private void AddEdge(string id, string category, string description, double severity)
        {
            _page.EdgeCases.Add(new DetectedEdgeCase
            {
                Id = id,
                Category = category,
                Description = description,
                Severity = severity
            });
        }

        // ── Core test suite (always generated) ───────────────────────────────────

        private void GenerateCoreTests()
        {
            var urlFragment = ExtractUrlFragment(_page.Url);
            var titleFragment = ExtractTitleFragment(_page.Title);
            var primary = PrimaryTarget();
            var primaryAssertion = PrimaryAssertion(primary);

            // State-aware: dismiss overlays before functional tests
            AddTest("functional",
                "Functional — Dismiss overlays and verify page ready",
                "",
                Viewport(1280, 800),
                Step("dismissOverlays"),
                Step("waitForLoad"));

            AddTest("functional",
                "Functional — Page loads with core content visible",
                primaryAssertion,
                Viewport(1280, 800),
                Step("waitForLoad"),
                Step("checktitle", expected: titleFragment),
                Step("checkelementcount", target: primary, count: 1),
                Step("visualverify"),
                Step("screenshot", value: "functional_load"));

            // Functional — URL integrity
            if (!string.IsNullOrEmpty(urlFragment))
            {
                AddTest("functional",
                    "Functional — URL resolves to expected domain/path",
                    $"url={urlFragment}",
                    Viewport(1280, 800),
                    Step("waitForLoad"),
                    Step("screenshot", value: "url_check"));
            }

            // Responsive — standard breakpoints
            AddTest("responsive",
                "Responsive — Mobile layout (375×667) no horizontal overflow",
                primaryAssertion,
                Viewport(375, 667),
                Step("waitForLoad"),
                Step("checklayoutadvanced"),
                Step("checkheadings"),
                Step("screenshot", value: "mobile_375"));

            AddTest("responsive",
                "Responsive — Tablet layout (768×1024)",
                "",
                Viewport(768, 1024),
                Step("waitForLoad"),
                Step("checklayoutadvanced"),
                Step("screenshot", value: "tablet_768"));

            AddTest("responsive",
                "Responsive — Desktop layout (1280×800)",
                primaryAssertion,
                Viewport(1280, 800),
                Step("waitForLoad"),
                Step("checklayoutadvanced"),
                Step("scroll", selector: "body"),
                Step("screenshot", value: "desktop_1280"));

            // SEO
            AddTest("seo",
                "SEO — Title, meta description, and viewport tags",
                "",
                Viewport(1280, 800),
                Step("waitForLoad"),
                Step("checktitle", expected: titleFragment),
                Step("checkmetatags"),
                Step("screenshot", value: "seo_meta"));

            // Accessibility baseline
            AddTest("accessibility",
                "Accessibility — Heading structure, ARIA, and tab order",
                "",
                Viewport(1280, 800),
                Step("waitForLoad"),
                Step("checkheadings"),
                Step("checkaria"),
                Step("checktaborder"),
                Step("checkskiplink"),
                Step("screenshot", value: "a11y_baseline"));

            AddTest("accessibility",
                "Accessibility — Colour contrast heuristic (WCAG AA)",
                "",
                Viewport(1280, 800),
                Step("waitForLoad"),
                Step("checkcontrast"),
                Step("screenshot", value: "a11y_contrast"));

            // Images (if present)
            if (_page.Images.Count > 0)
            {
                AddTest("image",
                    "Image — All images load successfully",
                    "img",
                    Viewport(1280, 800),
                    Step("waitForLoad"),
                    Step("checkimage", selector: "img"),
                    Step("screenshot", value: "images_load"));

                AddTest("image",
                    "Image — Alt text present on all images",
                    "",
                    Viewport(1280, 800),
                    Step("waitForLoad"),
                    Step("checkimagealt"),
                    Step("screenshot", value: "images_alt"));
            }

            // Links (if present)
            if (_page.Links.Count > 0)
            {
                AddTest("functional",
                    "Functional — All links have valid href attributes",
                    "css:a",
                    Viewport(1280, 800),
                    Step("waitForLoad"),
                    Step("checklinks"),
                    Step("checkelementcount", target: SelectorEngine.BuildGeneric("verify links present", "link", "", "a"), count: 1),
                    Step("screenshot", value: "links_valid"));
            }

            // Forms (if present)
            if (_page.Inputs.Any(i => !i.IsHidden))
            {
                AddTest("accessibility",
                    "Accessibility — Form inputs have labels or ARIA",
                    "",
                    Viewport(1280, 800),
                    Step("waitForLoad"),
                    Step("checkformlabels"),
                    Step("screenshot", value: "form_labels"));
            }
        }

        // ── Edge-case targeted tests ─────────────────────────────────────────────

        private void GenerateEdgeCaseTests()
        {
            foreach (var edge in _page.EdgeCases.OrderByDescending(e => e.Severity))
            {
                switch (edge.Id)
                {
                    case "no-viewport":
                        AddTest("responsive",
                            "Edge — Extreme mobile width (320×568) without viewport meta",
                            "",
                            Viewport(320, 568),
                            Step("waitForLoad"),
                            Step("checklayoutadvanced"),
                            Step("checkheadings"),
                            Step("screenshot", value: "edge_mobile_320"));
                        AddTest("responsive",
                            "Edge — Large desktop (1920×1080) stress test",
                            "",
                            Viewport(1920, 1080),
                            Step("waitForLoad"),
                            Step("checklayoutadvanced"),
                            Step("scroll", selector: "body"),
                            Step("screenshot", value: "edge_desktop_1920"));
                        break;

                    case "broken-images":
                        AddTest("image",
                            "Edge — Detect and report broken image sources",
                            "img",
                            Viewport(1280, 800),
                            Step("waitForLoad"),
                            Step("checkimage", selector: "img"),
                            Step("screenshot", value: "edge_broken_images"));
                        break;

                    case "missing-alt":
                        AddTest("image",
                            "Edge — Images missing alt attribute (a11y violation)",
                            "",
                            Viewport(1280, 800),
                            Step("waitForLoad"),
                            Step("checkimagealt"),
                            Step("checkaria"),
                            Step("screenshot", value: "edge_missing_alt"));
                        break;

                    case "no-h1":
                    case "multiple-h1":
                        AddTest("accessibility",
                            $"Edge — Heading hierarchy anomaly ({edge.Description})",
                            "",
                            Viewport(1280, 800),
                            Step("waitForLoad"),
                            Step("checkheadings"),
                            Step("checkelementcount", selector: "h1", count: 1),
                            Step("screenshot", value: "edge_headings"));
                        break;

                    case "empty-links":
                        AddTest("functional",
                            "Edge — Links with empty or missing href",
                            "",
                            Viewport(1280, 800),
                            Step("waitForLoad"),
                            Step("checklinks"),
                            Step("screenshot", value: "edge_empty_links"));
                        break;

                    case "unlabeled-inputs":
                        AddTest("accessibility",
                            "Edge — Unlabeled form inputs (WCAG 1.3.1)",
                            "",
                            Viewport(1280, 800),
                            Step("waitForLoad"),
                            Step("checkformlabels"),
                            Step("checkaria"),
                            Step("screenshot", value: "edge_unlabeled_inputs"));
                        break;

                    case "unnamed-buttons":
                        AddTest("accessibility",
                            "Edge — Buttons without accessible names",
                            "",
                            Viewport(1280, 800),
                            Step("waitForLoad"),
                            Step("checkaria"),
                            Step("checkelementcount", selector: "button", count: 1),
                            Step("screenshot", value: "edge_unnamed_buttons"));
                        break;

                    case "no-title":
                    case "no-meta-desc":
                        AddTest("seo",
                            "Edge — Missing critical SEO metadata",
                            "",
                            Viewport(1280, 800),
                            Step("waitForLoad"),
                            Step("checktitle", expected: ""),
                            Step("checkmetatags"),
                            Step("screenshot", value: "edge_seo_missing"));
                        break;

                    case "empty-content":
                        AddTest("functional",
                            "Edge — Minimal content page still renders without errors",
                            "body",
                            Viewport(1280, 800),
                            Step("waitForLoad"),
                            Step("wait", value: "2000"),
                            Step("screenshot", value: "edge_minimal_content"));
                        break;
                }
            }
        }

        // ── Adaptive tests based on feature vector (decision tree) ───────────────

        private void GenerateAdaptiveTests()
        {
            var f = _page.Features;

            // High accessibility risk → deeper a11y pass
            if (f.AccessibilityRisk >= 0.5)
            {
                AddTest("accessibility",
                    "Adaptive — High a11y risk: combined structure + form + contrast audit",
                    "",
                    Viewport(1280, 800),
                    Step("waitForLoad"),
                    Step("checkheadings"),
                    Step("checkaria"),
                    Step("checkformlabels"),
                    Step("checkcontrast"),
                    Step("checktaborder"),
                    Step("screenshot", value: "adaptive_a11y_audit"));
            }

            // High responsive risk → extra narrow viewport
            if (f.ResponsiveRisk >= 0.7)
            {
                AddTest("responsive",
                    "Adaptive — High responsive risk: narrow phone (360×640)",
                    "",
                    Viewport(360, 640),
                    Step("waitForLoad"),
                    Step("checklayoutadvanced"),
                    Step("scroll", selector: "body"),
                    Step("screenshot", value: "adaptive_narrow_360"));
            }

            // Interactive page → functional interaction tests
            if (f.InteractivityLevel >= 0.2)
            {
                var firstLink = _page.Links.FirstOrDefault(l => !l.IsEmptyHref && !l.IsHashOnly);
                if (firstLink != null)
                {
                    AddTest("functional",
                        "Adaptive — First navigable link is present and page remains stable",
                        "a",
                        Viewport(1280, 800),
                        Step("waitForLoad"),
                        Step("waitforselector", selector: "a[href]:not([href='']):not([href='#'])"),
                        Step("checkelementcount", selector: "a", count: 1),
                        Step("screenshot", value: "adaptive_link_present"));
                }

                var textInput = _page.Inputs.FirstOrDefault(i =>
                    !i.IsHidden &&
                    (i.Type == "text" || i.Type == "email" || i.Type == "search" || i.Type == "password"));

                if (textInput != null)
                {
                    AddTest("functional",
                        $"Adaptive — {textInput.Target.Intent}",
                        PrimaryAssertion(textInput.Target),
                        Viewport(1280, 800),
                        Step("dismissOverlays"),
                        Step("waitForLoad"),
                        Step("click", target: textInput.Target, verifyAfter: "visible"),
                        Step("type", target: textInput.Target, value: "test-input-edge", verifyAfter: "visible"),
                        Step("screenshot", value: "adaptive_input_type"));
                }

                var firstButton = _page.Buttons.FirstOrDefault();
                if (firstButton != null)
                {
                    AddTest("functional",
                        $"Adaptive — {firstButton.Target.Intent}",
                        PrimaryAssertion(firstButton.Target),
                        Viewport(1280, 800),
                        Step("dismissOverlays"),
                        Step("waitForLoad"),
                        Step("hover", target: firstButton.Target, verifyAfter: "visible"),
                        Step("keypress", key: "Tab"),
                        Step("screenshot", value: "adaptive_button_hover"));
                }
            }

            // Low image integrity → dedicated re-check after scroll
            if (f.ImageIntegrity < 1.0 && _page.Images.Count > 0)
            {
                AddTest("image",
                    "Adaptive — Lazy-loaded images after scroll",
                    "img",
                    Viewport(1280, 800),
                    Step("waitForLoad"),
                    Step("scroll", selector: "body"),
                    Step("wait", value: "1500"),
                    Step("checkimage", selector: "img"),
                    Step("screenshot", value: "adaptive_lazy_images"));
            }

            // Long content → scroll + layout at mobile
            if (f.ContentDensity >= 0.4)
            {
                AddTest("responsive",
                    "Adaptive — Long content scroll on mobile without layout break",
                    "",
                    Viewport(375, 667),
                    Step("waitForLoad"),
                    Step("scroll", selector: "body"),
                    Step("wait", value: "1000"),
                    Step("checklayoutadvanced"),
                    Step("screenshot", value: "adaptive_mobile_scroll"));
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private ElementTarget PrimaryTarget()
        {
            var h1 = _page.Headings.FirstOrDefault(h => h.Level == "h1");
            if (h1 != null) return h1.Target;
            if (_page.Headings.Count > 0) return _page.Headings[0].Target;
            if (_page.Images.Count > 0) return _page.Images[0].Target;
            if (_page.Links.Count > 0) return _page.Links[0].Target;
            return SelectorEngine.BuildGeneric("verify page body", "", "", "body");
        }

        private static string PrimaryAssertion(ElementTarget target) =>
            target.FallbackChain.FirstOrDefault() ?? "css:body";

        private static string ExtractUrlFragment(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath.Trim('/');
                if (!string.IsNullOrEmpty(path))
                {
                    var segment = path.Split('/').LastOrDefault(s => s.Length > 3);
                    if (!string.IsNullOrEmpty(segment)) return segment;
                }
                return uri.Host.Replace("www.", "");
            }
            catch { return ""; }
        }

        private static string ExtractTitleFragment(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";
            var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2) return string.Join(" ", words.Take(2));
            return words.Length > 0 ? words[0] : title;
        }

        private static TestStep Viewport(int w, int h) =>
            new() { action = "setViewport", width = w, height = h };

        private static TestStep Step(string action, string? selector = null, string? value = null,
            string? expected = null, int count = 0, string? key = null,
            ElementTarget? target = null, string? verifyAfter = null, bool dismissOverlays = false)
        {
            var s = new TestStep
            {
                action = action,
                target = target,
                verifyAfter = verifyAfter ?? "",
                dismissOverlays = dismissOverlays
            };
            if (target != null)
                s.intent = target.Intent;
            if (selector != null) s.selector = selector;
            if (value != null) s.value = value;
            if (expected != null) s.expected = expected;
            if (count > 0) s.count = count;
            if (key != null) s.key = key;
            return s;
        }

        private void AddTest(string category, string name, string assertion, params TestStep[] steps)
        {
            _tests.Add(new TestCase
            {
                name = name,
                category = category,
                steps = steps.ToList(),
                assertion = assertion ?? ""
            });
        }

        private static double Clamp(double v) => Math.Max(0, Math.Min(1, v));
    }
}
