using Microsoft.Playwright;
using PlaywrightPrototype.models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PlaywrightPrototype.Engine
{
    public class LocatorResolveResult
    {
        public ILocator Locator { get; set; } = null!;
        public string UsedStrategy { get; set; } = "";
        public bool IsUnique { get; set; }
        public int MatchCount { get; set; }
        public string Error { get; set; } = "";
    }

    /// <summary>
    /// Resolves ElementTarget to Playwright locators with fallback chain and uniqueness validation.
    /// </summary>
    public static class SmartLocatorResolver
    {
        public static async Task<LocatorResolveResult> ResolveAsync(IPage page, ElementTarget? target, string? legacySelector = null)
        {
            if (target?.FallbackChain?.Count > 0)
            {
                foreach (var strategy in target.FallbackChain)
                {
                    if (SelectorEngine.IsBrittle(strategy))
                        continue;

                    var result = await TryStrategyAsync(page, strategy);
                    if (result.Locator != null)
                    {
                        result.UsedStrategy = strategy;
                        return result;
                    }
                }

                return new LocatorResolveResult
                {
                    Error = $"All fallbacks exhausted for intent \"{target.Intent}\". Tried: {string.Join(", ", target.FallbackChain)}"
                };
            }

            if (!string.IsNullOrWhiteSpace(legacySelector))
            {
                if (SelectorEngine.IsBrittle(legacySelector))
                {
                    return new LocatorResolveResult
                    {
                        Error = $"Brittle selector rejected: \"{legacySelector}\". Use GetByRole/GetByLabel/data-testid/id instead."
                    };
                }

                var loc = page.Locator(legacySelector);
                var count = await loc.CountAsync();
                if (count == 1)
                    return new LocatorResolveResult { Locator = loc, UsedStrategy = $"css:{legacySelector}", IsUnique = true, MatchCount = 1 };
                if (count > 1)
                    return new LocatorResolveResult { Error = $"Selector matched {count} elements (expected 1): \"{legacySelector}\"" };
                return new LocatorResolveResult { Error = $"Selector matched 0 elements: \"{legacySelector}\"" };
            }

            return new LocatorResolveResult { Error = "No target or selector provided." };
        }

        public static ILocator ToPlaywrightLocator(IPage page, string strategy)
        {
            var parts = strategy.Split(':', 3);
            var kind = parts[0].ToLowerInvariant();

            return kind switch
            {
                "role" when parts.Length >= 3 =>
                    page.GetByRole(ParseRole(parts[1]), new() { Name = parts[2], Exact = false }),
                "testid" when parts.Length >= 2 =>
                    page.GetByTestId(parts[1]),
                "id" when parts.Length >= 2 =>
                    page.Locator($"#{EscapeCss(parts[1])}"),
                "label" when parts.Length >= 2 =>
                    page.GetByLabel(parts[1], new() { Exact = false }),
                "placeholder" when parts.Length >= 2 =>
                    page.GetByPlaceholder(parts[1], new() { Exact = false }),
                "text" when parts.Length >= 2 =>
                    page.GetByText(parts[1], new() { Exact = false }),
                "css" when parts.Length >= 2 =>
                    page.Locator(parts[1]),
                _ => page.Locator(strategy)
            };
        }

        private static async Task<LocatorResolveResult> TryStrategyAsync(IPage page, string strategy)
        {
            try
            {
                var locator = ToPlaywrightLocator(page, strategy);
                var count = await locator.CountAsync();

                if (count == 1)
                    return new LocatorResolveResult { Locator = locator, IsUnique = true, MatchCount = 1 };

                if (count > 1)
                    return new LocatorResolveResult { Error = $"Strategy \"{strategy}\" matched {count} elements (expected 1)" };

                return new LocatorResolveResult { Error = $"Strategy \"{strategy}\" matched 0 elements" };
            }
            catch (Exception ex)
            {
                return new LocatorResolveResult { Error = ex.Message };
            }
        }

        private static AriaRole ParseRole(string role) =>
            Enum.TryParse<AriaRole>(role, true, out var r) ? r : AriaRole.Button;

        private static string EscapeCss(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
