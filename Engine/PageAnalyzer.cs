using Microsoft.Playwright;
using PlaywrightPrototype.models;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlaywrightPrototype.Engine
{
    /// <summary>
    /// Extracts structured page content and accessibility metadata for intelligent test generation.
    /// </summary>
    public static class PageAnalyzer
    {
        public static async Task<PageAnalysis> AnalyzeAsync(IPage page)
        {
            var analysis = new PageAnalysis
            {
                Url = page.Url,
                Title = await page.TitleAsync() ?? ""
            };

            analysis.MetaDescription = await page.EvaluateAsync<string>(
                @"() => document.querySelector('meta[name=""description""]')?.content ?? ''") ?? "";
            analysis.MetaViewport = await page.EvaluateAsync<string>(
                @"() => document.querySelector('meta[name=""viewport""]')?.content ?? ''") ?? "";
            analysis.HasMetaDescription = !string.IsNullOrWhiteSpace(analysis.MetaDescription);
            analysis.HasMetaViewport = !string.IsNullOrWhiteSpace(analysis.MetaViewport);

            await ExtractHeadingsAsync(page, analysis);
            await ExtractInteractiveElementsAsync(page, analysis);
            await ExtractImagesAsync(page, analysis);

            analysis.FormCount = (await page.QuerySelectorAllAsync("form")).Count;
            analysis.ParagraphCount = (await page.QuerySelectorAllAsync("p")).Count;
            analysis.InteractiveElementCount = analysis.InteractiveElements.Count;
            analysis.PageState = await StateAwareHelper.DetectStateAsync(page);

            return analysis;
        }

        private static async Task ExtractHeadingsAsync(IPage page, PageAnalysis analysis)
        {
            var headings = await page.EvaluateAsync<JsonElement>(@"() => {
                return Array.from(document.querySelectorAll('h1,h2,h3,h4,h5,h6')).map(el => ({
                    level: el.tagName.toLowerCase(),
                    text: (el.textContent || '').trim()
                }));
            }");

            foreach (var h in headings.EnumerateArray())
            {
                var level = h.GetProperty("level").GetString() ?? "h1";
                var text = h.GetProperty("text").GetString() ?? "";
                var info = new HeadingInfo { Level = level, Text = text };
                info.Target = SelectorEngine.BuildForHeading(info);
                analysis.Headings.Add(info);
            }
        }

        private static async Task ExtractInteractiveElementsAsync(IPage page, PageAnalysis analysis)
        {
            var elements = await page.EvaluateAsync<JsonElement>(@"() => {
                const isVisible = el => {
                    const s = window.getComputedStyle(el);
                    const r = el.getBoundingClientRect();
                    return s.display !== 'none' && s.visibility !== 'hidden' && r.width > 0 && r.height > 0;
                };
                const getLabel = el => {
                    if (el.labels && el.labels.length) return el.labels[0].textContent?.trim() || '';
                    if (el.id) {
                        const lbl = document.querySelector('label[for=""' + el.id + '""]');
                        if (lbl) return lbl.textContent?.trim() || '';
                    }
                    return '';
                };
                const results = [];
                document.querySelectorAll('a,button,input,select,textarea,[role=""button""],[role=""link""]').forEach(el => {
                    if (!isVisible(el)) return;
                    results.push({
                        tag: el.tagName,
                        role: el.getAttribute('role') || '',
                        text: (el.textContent || '').trim().slice(0, 100),
                        ariaLabel: el.getAttribute('aria-label') || '',
                        id: el.id || '',
                        testId: el.getAttribute('data-testid') || '',
                        label: getLabel(el),
                        placeholder: el.getAttribute('placeholder') || '',
                        name: el.getAttribute('name') || '',
                        href: el.getAttribute('href') || '',
                        inputType: el.getAttribute('type') || '',
                        enabled: !el.disabled
                    });
                });
                return results;
            }");

            foreach (var el in elements.EnumerateArray())
            {
                var target = SelectorEngine.BuildFromDomElement(el);
                var tag = el.GetProperty("tag").GetString() ?? "";
                var interactive = new InteractiveElement
                {
                    Target = target,
                    Tag = tag,
                    Text = el.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                    IsVisible = true,
                    IsEnabled = !el.TryGetProperty("enabled", out var en) || en.GetBoolean(),
                    Href = el.TryGetProperty("href", out var h) ? h.GetString() ?? "" : "",
                    InputType = el.TryGetProperty("inputType", out var it) ? it.GetString() ?? "" : ""
                };
                analysis.InteractiveElements.Add(interactive);

                if (tag.Equals("A", System.StringComparison.OrdinalIgnoreCase))
                {
                    var link = new LinkInfo
                    {
                        Text = interactive.Text,
                        Href = interactive.Href,
                        IsEmptyHref = string.IsNullOrWhiteSpace(interactive.Href),
                        IsHashOnly = interactive.Href.Trim() == "#" || interactive.Href.TrimStart().StartsWith("#"),
                        Target = target
                    };
                    analysis.Links.Add(link);
                }
                else if (tag.Equals("BUTTON", System.StringComparison.OrdinalIgnoreCase))
                {
                    analysis.Buttons.Add(new ButtonInfo
                    {
                        Text = interactive.Text,
                        Id = target.ElementId,
                        Target = target,
                        HasAccessibleName = !string.IsNullOrWhiteSpace(target.Name)
                    });
                }
                else if (tag.Equals("INPUT", System.StringComparison.OrdinalIgnoreCase))
                {
                    var hidden = interactive.InputType.Equals("hidden", System.StringComparison.OrdinalIgnoreCase);
                    analysis.Inputs.Add(new InputInfo
                    {
                        Type = interactive.InputType,
                        Id = target.ElementId,
                        Name = target.Name,
                        Target = target,
                        HasLabel = !string.IsNullOrWhiteSpace(target.Label),
                        HasAriaLabel = target.Name == interactive.Text && !string.IsNullOrWhiteSpace(interactive.Text),
                        HasPlaceholder = !string.IsNullOrWhiteSpace(target.Placeholder),
                        IsHidden = hidden
                    });
                }
            }
        }

        private static async Task ExtractImagesAsync(IPage page, PageAnalysis analysis)
        {
            var images = await page.QuerySelectorAllAsync("img");
            foreach (var img in images)
            {
                var src = await img.GetAttributeAsync("src") ?? "";
                var alt = await img.GetAttributeAsync("alt");
                var loaded = await img.EvaluateAsync<bool>("i => i.complete && i.naturalWidth > 0");
                var altText = alt ?? "";

                var target = SelectorEngine.BuildGeneric(
                    $"verify image \"{Truncate(altText, 30)}\"",
                    "img",
                    altText,
                    "img");

                analysis.Images.Add(new ImageInfo
                {
                    Src = src,
                    Alt = altText,
                    HasAlt = alt != null,
                    IsLoaded = loaded,
                    Target = target
                });
            }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
    }
}
