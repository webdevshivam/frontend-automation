using Microsoft.Playwright;
using PlaywrightPrototype.models;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlaywrightPrototype.Engine
{
  
    public static class TestRepairEngine
    {
        public static async Task<(ElementTarget? Repaired, string Detail)> TryRepairAsync(
            IPage page, TestStep step)
        {
            var intent = step.intent ?? step.target?.Intent ?? "";
            var searchTerms = ExtractSearchTerms(step);

            if (searchTerms.Count == 0)
                return (null, "No search terms available for repair");

            var candidates = await page.EvaluateAsync<JsonElement>(@"(terms) => {
                const isVisible = el => {
                    const s = window.getComputedStyle(el);
                    const r = el.getBoundingClientRect();
                    return s.display !== 'none' && s.visibility !== 'hidden' && r.width > 0 && r.height > 0;
                };
                const results = [];
                const els = document.querySelectorAll('button,a,input,select,textarea,[role],[data-testid]');
                els.forEach(el => {
                    if (!isVisible(el)) return;
                    const text = (el.textContent || '').trim();
                    const aria = el.getAttribute('aria-label') || '';
                    const id = el.id || '';
                    const testId = el.getAttribute('data-testid') || '';
                    const label = el.getAttribute('aria-labelledby') ? '' : (el.labels?.[0]?.textContent || '');
                    const combined = (text + ' ' + aria + ' ' + id + ' ' + testId + ' ' + label).toLowerCase();
                    const score = terms.reduce((s, t) => combined.includes(t.toLowerCase()) ? s + 1 : s, 0);
                    if (score > 0) {
                        results.push({
                            tag: el.tagName,
                            role: el.getAttribute('role') || '',
                            text: text.slice(0,80),
                            ariaLabel: aria,
                            id, testId,
                            label: label.trim(),
                            placeholder: el.getAttribute('placeholder') || '',
                            name: el.getAttribute('name') || '',
                            href: el.getAttribute('href') || '',
                            inputType: el.getAttribute('type') || '',
                            score
                        });
                    }
                });
                return results.sort((a,b) => b.score - a.score).slice(0, 5);
            }", searchTerms);

            foreach (var candidate in candidates.EnumerateArray())
            {
                var repaired = SelectorEngine.BuildFromDomElement(candidate);
                var resolved = await SmartLocatorResolver.ResolveAsync(page, repaired);
                if (resolved.Locator != null && resolved.IsUnique)
                {
                    repaired.Intent = string.IsNullOrEmpty(intent) ? repaired.Intent : intent;
                    return (repaired, $"Repaired via live DOM scan → {resolved.UsedStrategy}");
                }
            }

            return (null, $"Repair failed — no unique match for terms: {string.Join(", ", searchTerms)}");
        }

        private static List<string> ExtractSearchTerms(TestStep step)
        {
            var terms = new List<string>();
            if (!string.IsNullOrWhiteSpace(step.target?.Name)) terms.Add(step.target.Name);
            if (!string.IsNullOrWhiteSpace(step.target?.Label)) terms.Add(step.target.Label);
            if (!string.IsNullOrWhiteSpace(step.intent))
            {
                foreach (var word in step.intent.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                {
                    if (word.Length > 3 && word != "click" && word != "button" && word != "with")
                        terms.Add(word.Trim('"', '\''));
                }
            }
            return terms;
        }
    }
}
