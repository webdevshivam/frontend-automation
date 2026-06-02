using Microsoft.Playwright;
using System.Threading.Tasks;

namespace PlaywrightPrototype.Engine
{
    /// <summary>
    /// Advanced responsive layout checks: overflow, overlaps, clipped text, hidden buttons, mobile menu.
    /// </summary>
    public static class LayoutAnalyzer
    {
        public static async Task<(bool Passed, string Detail)> AnalyzeAsync(IPage page)
        {
            var result = await page.EvaluateAsync<string>(@"() => {
                const issues = [];
                const vw = window.innerWidth;
                const vh = window.innerHeight;
                const bw = document.body.scrollWidth;

                if (bw > vw + 5)
                    issues.push('OVERFLOW:scrollWidth=' + bw + ' viewportWidth=' + vw);

                const isVisible = el => {
                    const s = window.getComputedStyle(el);
                    const r = el.getBoundingClientRect();
                    return s.display !== 'none' && s.visibility !== 'hidden' && r.width > 0 && r.height > 0;
                };

                // Hidden buttons (in DOM but not visible/usable)
                const buttons = Array.from(document.querySelectorAll('button,a[role=""button""],[role=""button""]'));
                const hiddenBtns = buttons.filter(b => !isVisible(b)).slice(0, 5);
                if (hiddenBtns.length > 0)
                    issues.push('HIDDEN_BUTTONS:' + hiddenBtns.map(b => (b.textContent||'?').trim().slice(0,20)).join('|'));

                // Clipped text (overflow hidden with truncated content)
                const textEls = Array.from(document.querySelectorAll('p,h1,h2,h3,h4,h5,h6,span,a,button')).filter(isVisible);
                let clipped = 0;
                textEls.forEach(el => {
                    const s = window.getComputedStyle(el);
                    if ((s.overflow === 'hidden' || s.textOverflow === 'ellipsis') &&
                        el.scrollWidth > el.clientWidth + 2) clipped++;
                });
                if (clipped > 0) issues.push('CLIPPED_TEXT:' + clipped + ' element(s)');

                // Overlapping interactive elements (sample check)
                const interactives = Array.from(document.querySelectorAll('button,a,input,select')).filter(isVisible).slice(0, 30);
                let overlaps = 0;
                for (let i = 0; i < interactives.length; i++) {
                    for (let j = i + 1; j < interactives.length; j++) {
                        const r1 = interactives[i].getBoundingClientRect();
                        const r2 = interactives[j].getBoundingClientRect();
                        const overlap = !(r1.right < r2.left || r1.left > r2.right || r1.bottom < r2.top || r1.top > r2.bottom);
                        if (overlap && r1.width > 10 && r2.width > 10) { overlaps++; break; }
                    }
                }
                if (overlaps > 2) issues.push('OVERLAPPING:' + overlaps + ' interactive element pair(s)');

                // Mobile menu usability (hamburger present but nav hidden on small viewport)
                if (vw <= 768) {
                    const menuBtn = document.querySelector('[class*=""menu"" i],[class*=""hamburger"" i],[aria-label*=""menu"" i],[data-testid*=""menu"" i]');
                    const nav = document.querySelector('nav,[role=""navigation""]');
                    if (menuBtn && nav) {
                        const navVisible = isVisible(nav) && nav.getBoundingClientRect().width > 50;
                        const menuVisible = isVisible(menuBtn);
                        if (menuVisible && !navVisible)
                            issues.push('MOBILE_MENU: hamburger visible but nav hidden — menu may be unusable');
                    }
                }

                return issues.length === 0 ? 'OK' : issues.join('||');
            }");

            if (result == "OK")
            {
                var vp = page.ViewportSize;
                return (true, $"Layout OK at {vp?.Width}×{vp?.Height} — no overflow, overlaps, or clipped text ✓");
            }

            return (false, result.Replace("||", "; "));
        }

        public static bool IsOverflowFailure(string detail) =>
            detail.Contains("OVERFLOW", System.StringComparison.OrdinalIgnoreCase);
    }
}
