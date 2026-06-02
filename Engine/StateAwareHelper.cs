using Microsoft.Playwright;
using PlaywrightPrototype.models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlaywrightPrototype.Engine
{
    /// <summary>
    /// Detects and dismisses overlays (cookie banners, modals, loaders) before interactions.
    /// </summary>
    public static class StateAwareHelper
    {
        public static async Task<PageStateInfo> DetectStateAsync(IPage page)
        {
            var json = await page.EvaluateAsync<JsonElement>(@"() => {
                const isVisible = el => {
                    if (!el) return false;
                    const s = window.getComputedStyle(el);
                    const r = el.getBoundingClientRect();
                    return s.display !== 'none' && s.visibility !== 'hidden' && s.opacity !== '0'
                        && r.width > 0 && r.height > 0;
                };
                const q = sel => Array.from(document.querySelectorAll(sel)).filter(isVisible);

                const cookieEls = q('[class*=""cookie"" i],[id*=""cookie"" i],[class*=""consent"" i],[id*=""consent"" i],[aria-label*=""cookie"" i],[data-testid*=""cookie"" i]');
                const modalEls = q('[role=""dialog""],[class*=""modal"" i],[class*=""popup"" i],[aria-modal=""true""]');
                const loaderEls = q('[class*=""loader"" i],[class*=""spinner"" i],[class*=""loading"" i],[role=""progressbar""]');
                const dropdownEls = q('select,[role=""listbox""],[role=""combobox""],[class*=""dropdown"" i]');

                const dismissCandidates = [];
                const dismissPatterns = /accept|agree|decline|close|dismiss|ok|got it|continue/i;
                q('button,a,[role=""button""]').forEach(el => {
                    const t = (el.textContent || el.getAttribute('aria-label') || '').trim();
                    if (t && dismissPatterns.test(t) && (cookieEls.length || modalEls.length))
                        dismissCandidates.push({
                            tag: el.tagName, role: el.getAttribute('role') || 'button',
                            text: t.slice(0,80), ariaLabel: el.getAttribute('aria-label') || '',
                            id: el.id || '', testId: el.getAttribute('data-testid') || '',
                            label: '', placeholder: '', name: el.getAttribute('name') || '',
                            href: '', inputType: ''
                        });
                });

                return {
                    hasCookieBanner: cookieEls.length > 0,
                    hasModal: modalEls.length > 0,
                    hasLoader: loaderEls.length > 0,
                    hasDropdown: dropdownEls.length > 0,
                    dismissCandidates
                };
            }");

            var state = new PageStateInfo
            {
                HasCookieBanner = json.GetProperty("hasCookieBanner").GetBoolean(),
                HasModal = json.GetProperty("hasModal").GetBoolean(),
                HasLoader = json.GetProperty("hasLoader").GetBoolean(),
                HasDropdown = json.GetProperty("hasDropdown").GetBoolean()
            };

            if (json.TryGetProperty("dismissCandidates", out var candidates))
            {
                foreach (var c in candidates.EnumerateArray())
                    state.OverlayDismissTargets.Add(SelectorEngine.BuildFromDomElement(c));
            }

            return state;
        }

        public static async Task<string> PreparePageForInteractionAsync(IPage page)
        {
            var state = await DetectStateAsync(page);
            var actions = new List<string>();

            if (state.HasLoader)
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.WaitForTimeoutAsync(500);
                actions.Add("waited for loader/network idle");
            }

            if (state.HasCookieBanner || state.HasModal)
            {
                foreach (var target in state.OverlayDismissTargets)
                {
                    var resolved = await SmartLocatorResolver.ResolveAsync(page, target);
                    if (resolved.Locator == null) continue;

                    try
                    {
                        await resolved.Locator.ClickAsync(new() { Timeout = 3000 });
                        await page.WaitForTimeoutAsync(400);
                        actions.Add($"dismissed overlay via {target.Intent} ({resolved.UsedStrategy})");
                        break;
                    }
                    catch { /* try next dismiss target */ }
                }
            }

            return actions.Count > 0 ? string.Join("; ", actions) : "no overlays detected";
        }
    }
}
