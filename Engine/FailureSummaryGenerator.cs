using PlaywrightPrototype.models;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PlaywrightPrototype.Engine
{
    /// <summary>
    /// Converts raw test failures into concise, human-readable debugging insights.
    /// </summary>
    public static class FailureSummaryGenerator
    {
        public static string Generate(List<TestResult> results)
        {
            var failed = results.Where(r => r.Status == TestStatus.Failed).ToList();
            if (failed.Count == 0)
                return "All tests passed — no failures to report.";

            var sb = new StringBuilder();
            sb.AppendLine("Failed Tests Summary");
            sb.AppendLine(new string('─', 50));
            sb.AppendLine();

            int n = 1;
            foreach (var f in failed)
            {
                var insight = f.FailureInsight ?? BuildInsight(f);
                sb.AppendLine($"{n}. {insight.TestName}");
                sb.AppendLine($"   Category: {insight.Category}");
                sb.AppendLine($"   Reason:");
                sb.AppendLine($"   {Wrap(insight.Reason, 6)}");
                if (!string.IsNullOrEmpty(insight.Intent))
                    sb.AppendLine($"   Intent: {insight.Intent}");
                if (!string.IsNullOrEmpty(insight.GeneratedSelector))
                {
                    sb.AppendLine($"   Generated selector:");
                    sb.AppendLine($"   {insight.GeneratedSelector}");
                }
                sb.AppendLine($"   Suggested Fix:");
                sb.AppendLine($"   {Wrap(insight.SuggestedFix, 6)}");
                sb.AppendLine();
                n++;
            }

            sb.AppendLine(new string('─', 50));
            sb.AppendLine($"Total: {failed.Count}/{results.Count} failed");
            return sb.ToString();
        }

        public static FailureInsight BuildInsight(TestResult result)
        {
            var insight = new FailureInsight
            {
                TestName = result.TestName,
                Category = result.Category,
                Reason = CleanReason(result.ErrorMessage),
                GeneratedSelector = result.UsedSelector ?? "",
                Intent = result.Intent ?? ""
            };

            insight.SuggestedFix = SuggestFix(result.ErrorMessage, result.UsedSelector, result.Intent, result.Category);
            return insight;
        }

        private static string SuggestFix(string? error, string? selector, string? intent, string? category)
        {
            error ??= "";
            selector ??= "";

            if (error.Contains("matched") && error.Contains("elements"))
            {
                var name = ExtractQuotedName(intent) ?? "ElementName";
                return $"Use GetByRole() with a unique accessible name.\n   Example: GetByRole(Button, Name=\"{name}\")";
            }

            if (error.Contains("Brittle selector"))
                return "Replace nth-of-type / deep CSS chains with GetByRole, GetByLabel, data-testid, or id.";

            if (error.Contains("OVERFLOW") || category == "responsive")
                return "Responsive layout breaking on mobile screen. Check fixed widths, images, and viewport meta tag.";

            if (error.Contains("alt") || error.Contains("MISSING"))
                return "Add alt attributes to all images or use aria-label on interactive elements.";

            if (error.Contains("href") || error.Contains("link"))
                return "Ensure all anchor tags have valid, non-empty href attributes.";

            if (error.Contains("heading") || error.Contains("H1"))
                return "Add exactly one H1 and maintain proper heading hierarchy (h1 → h2 → h3).";

            if (error.Contains("timeout") || error.Contains("matched 0"))
            {
                if (!string.IsNullOrEmpty(intent))
                    return $"Element for \"{intent}\" not found. Verify it exists and is visible; try GetByRole with exact name.";
                return "Element not found. Re-run analysis — the repair engine will regenerate selectors from live DOM.";
            }

            if (error.Contains("OVERLAPPING"))
                return "Fix z-index or positioning so interactive elements don't overlap on this viewport.";

            if (error.Contains("CLIPPED_TEXT"))
                return "Review overflow:hidden and text-overflow on mobile — content may be unreadable.";

            if (error.Contains("MOBILE_MENU"))
                return "Ensure hamburger menu opens navigation on mobile viewports.";

            if (selector.Contains("nth-of-type") || selector.Contains("nth-child"))
                return $"Replace \"{selector}\" with GetByRole(Button, Name=\"...\") or GetByTestId(\"...\").";

            return "Re-analyze page DOM and use accessibility-based selectors (role, label, testid, id).";
        }

        private static string CleanReason(string? msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return "Unknown failure";
            msg = msg.Replace("Step [", "").Replace("] — ", ": ");
            if (msg.StartsWith("Assertion failed — "))
                msg = msg["Assertion failed — ".Length..];
            return msg.Trim();
        }

        private static string? ExtractQuotedName(string? intent)
        {
            if (string.IsNullOrEmpty(intent)) return null;
            var start = intent.IndexOf('"');
            var end = intent.LastIndexOf('"');
            if (start >= 0 && end > start)
                return intent[(start + 1)..end];
            return null;
        }

        private static string Wrap(string text, int indent)
        {
            var pad = new string(' ', indent);
            return text.Replace("\n", "\n" + pad);
        }
    }
}
