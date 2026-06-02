using Microsoft.Playwright;
using PlaywrightPrototype.models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlaywrightPrototype.Engine
{
    public class TestExecutor
    {
        private readonly string _screenshotDir;
        private readonly string _baselineDir;

        public TestExecutor(string screenshotDir = "screenshots", string baselineDir = "baselines")
        {
            _screenshotDir = screenshotDir;
            _baselineDir = baselineDir;
            Directory.CreateDirectory(_screenshotDir);
            Directory.CreateDirectory(_baselineDir);
        }

        public async Task<List<TestResult>> ExecuteAsync(IPage page, List<TestCase> testCases)
        {
            var allResults = new List<TestResult>();

            foreach (var test in testCases)
            {
                var result = await RunSingleTestAsync(page, test);
                allResults.Add(result);

                var icon = result.Status == TestStatus.Passed ? "✅" : "❌";
                Console.WriteLine($"\n{icon} [{result.Status.ToString().ToUpper()}] {result.TestName}  ({result.Duration.TotalSeconds:F1}s)");

                if (result.Status == TestStatus.Failed)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"   ↳ {result.ErrorMessage}");
                    if (!string.IsNullOrEmpty(result.ScreenshotPath))
                        Console.WriteLine($"   ↳ Screenshot → {result.ScreenshotPath}");
                    Console.ResetColor();
                }
            }

            PrintSummary(allResults);
            PrintFailureInsights(allResults);
            return allResults;
        }

        private async Task<TestResult> RunSingleTestAsync(IPage page, TestCase test)
        {
            var result = new TestResult
            {
                TestName = test.name,
                Category = test.category ?? "general",
                Status = TestStatus.Passed
            };

            var started = DateTime.UtcNow;
            Console.WriteLine($"\n▶  [{test.category?.ToUpper() ?? "TEST"}] {test.name}");

            try
            {
                foreach (var step in test.steps ?? new List<TestStep>())
                {
                    var stepResult = await ExecuteStepAsync(page, step, test.name);
                    result.StepResults.Add(stepResult);

                    if (!string.IsNullOrEmpty(stepResult.UsedSelector))
                        result.UsedSelector = stepResult.UsedSelector;
                    if (!string.IsNullOrEmpty(stepResult.Intent))
                        result.Intent = stepResult.Intent;

                    if (stepResult.Status == TestStatus.Failed)
                    {
                        result.ScreenshotPath = await CaptureScreenshotAsync(page, test.name, "step_fail");
                        result.Status = TestStatus.Failed;
                        result.ErrorMessage = $"Step [{step.action}] — {stepResult.ErrorMessage}";
                        result.FailureInsight = FailureSummaryGenerator.BuildInsight(result);

                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"   ✗ Step failed [{step.action}]: {stepResult.ErrorMessage}");
                        if (stepResult.Repaired)
                            Console.WriteLine($"   ↳ Self-heal attempted");
                        Console.ResetColor();

                        result.Duration = DateTime.UtcNow - started;
                        return result;
                    }
                }

                if (!string.IsNullOrWhiteSpace(test.assertion))
                {
                    var (passed, msg, used) = await EvaluateAssertionAsync(page, test.assertion);
                    result.AssertionResult = msg;
                    result.UsedSelector = used ?? result.UsedSelector;

                    if (!passed)
                    {
                        result.Status = TestStatus.Failed;
                        result.ErrorMessage = $"Assertion failed — {msg}";
                        result.ScreenshotPath = await CaptureScreenshotAsync(page, test.name, "assert_fail");
                        result.FailureInsight = FailureSummaryGenerator.BuildInsight(result);

                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"   ✗ Assertion: {msg}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine($"   ✓ Assertion: {msg}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Status = TestStatus.Failed;
                result.ErrorMessage = ex.Message;
                result.ScreenshotPath = await CaptureScreenshotAsync(page, test.name, "exception");
                result.FailureInsight = FailureSummaryGenerator.BuildInsight(result);

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"   ✗ Exception: {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                result.Duration = DateTime.UtcNow - started;
                try
                {
                    await page.ReloadAsync();
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                }
                catch { /* safe between tests */ }
            }

            return result;
        }

        private async Task<StepResult> ExecuteStepAsync(IPage page, TestStep step, string testName)
        {
            var sr = new StepResult
            {
                Action = step.action ?? "unknown",
                Intent = step.intent ?? step.target?.Intent ?? "",
                Status = TestStatus.Passed
            };

            try
            {
                if (step.dismissOverlays)
                {
                    var prep = await StateAwareHelper.PreparePageForInteractionAsync(page);
                    sr.Detail = $"Overlays handled: {prep}";
                    Console.WriteLine($"   ✓ {sr.Detail}");
                }

                switch (step.action?.ToLower())
                {
                    case "dismissoverlays":
                        var dismissed = await StateAwareHelper.PreparePageForInteractionAsync(page);
                        sr.Detail = $"State-aware prep: {dismissed}";
                        break;

                    case "navigate":
                        if (string.IsNullOrEmpty(step.value))
                            return Fail(sr, "navigate: 'value' (URL) is required");
                        var beforeUrl = page.Url;
                        await page.GotoAsync(step.value);
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                        var (navOk, navMsg) = await ActionVerifier.VerifyAsync(page, "navigation", beforeUrl, null);
                        if (!navOk) return Fail(sr, navMsg);
                        sr.Detail = $"Navigated → {step.value} ({navMsg})";
                        break;

                    case "setviewport":
                        if (step.width <= 0 || step.height <= 0)
                            return Fail(sr, "setViewport: width and height must be > 0");
                        await page.SetViewportSizeAsync(step.width, step.height);
                        sr.Detail = $"Viewport → {step.width}×{step.height}";
                        break;

                    case "click":
                        return await RunInteractionAsync(page, step, sr, async loc =>
                        {
                            var beforeUrl = page.Url;
                            await loc.ClickAsync(new() { Timeout = 8000 });
                            var verify = step.verifyAfter ?? "page_changed";
                            var (ok, msg) = await ActionVerifier.VerifyAsync(page, verify, beforeUrl, null);
                            if (!ok) throw new Exception(msg);
                            return msg;
                        });

                    case "type":
                        return await RunInteractionAsync(page, step, sr, async loc =>
                        {
                            await loc.FillAsync(step.value ?? "");
                            var (ok, msg) = await ActionVerifier.VerifyAsync(page, step.verifyAfter ?? "visible", null, null);
                            if (!ok) throw new Exception(msg);
                            return $"Typed '{step.value}' — {msg}";
                        });

                    case "hover":
                        return await RunInteractionAsync(page, step, sr, async loc =>
                        {
                            await loc.HoverAsync(new() { Timeout = 8000 });
                            return $"Hovered — {step.target?.Describe() ?? step.selector}";
                        });

                    case "wait":
                        var ms = int.TryParse(step.value, out int w) ? w : 1500;
                        await page.WaitForTimeoutAsync(ms);
                        sr.Detail = $"Waited {ms}ms";
                        break;

                    case "waitforselector":
                        return await RunInteractionAsync(page, step, sr, async loc =>
                        {
                            await loc.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8000 });
                            return $"Element visible — {step.target?.Describe() ?? step.selector}";
                        });

                    case "waitforload":
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                        sr.Detail = "Page fully loaded (NetworkIdle)";
                        break;

                    case "scroll":
                        if (string.IsNullOrEmpty(step.selector) || step.selector is "body" or "window")
                            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
                        else
                        {
                            var scrollLoc = await ResolveLocatorAsync(page, step);
                            if (scrollLoc.fail != null) return scrollLoc.fail;
                            await scrollLoc.resolved!.Locator!.ScrollIntoViewIfNeededAsync();
                        }
                        sr.Detail = $"Scrolled to: {step.selector ?? "bottom"}";
                        break;

                    case "keypress":
                        var key = string.IsNullOrEmpty(step.key) ? "Tab" : step.key;
                        await page.Keyboard.PressAsync(key);
                        sr.Detail = $"Key pressed: {key}";
                        break;

                    case "screenshot":
                        var shotPath = await CaptureScreenshotAsync(page, testName, step.value ?? "manual");
                        sr.Detail = $"Screenshot → {shotPath}";
                        break;

                    case "visualverify":
                        sr = await RunVisualVerifyAsync(page, testName, sr);
                        break;

                    case "checkimage":
                        var imgSel = step.target != null
                            ? (await ResolveLocatorAsync(page, step)).resolved?.UsedStrategy ?? "css:img"
                            : step.selector ?? "css:img";
                        var imgCheck = await page.EvaluateAsync<string>(@"() => {
                            const imgs = document.querySelectorAll('img');
                            if (imgs.length === 0) return 'NO_IMAGES';
                            const broken = Array.from(imgs)
                                .filter(i => !(i.complete && i.naturalWidth > 0))
                                .map(i => i.src || '?');
                            return broken.length === 0 ? 'OK' : 'BROKEN:' + broken.join('||');
                        }");
                        if (imgCheck == "NO_IMAGES") return Fail(sr, "No images found on page");
                        if (imgCheck.StartsWith("BROKEN:")) return Fail(sr, $"Broken image(s): {imgCheck[7..]}");
                        sr.Detail = $"All images loaded OK ({imgSel})";
                        sr.UsedSelector = imgSel;
                        break;

                    case "checkimagealt":
                        var altCheck = await page.EvaluateAsync<string>(@"() => {
                            const imgs = document.querySelectorAll('img');
                            const bad = Array.from(imgs).filter(i => i.getAttribute('alt') === null).map(i => i.src || '?');
                            return bad.length === 0 ? 'OK' : 'MISSING:' + bad.join('||');
                        }");
                        if (altCheck.StartsWith("MISSING:")) return Fail(sr, $"Images without alt text: {altCheck[8..]}");
                        sr.Detail = "All images have alt attributes ✓";
                        break;

                    case "checklinks":
                        var linkCheck = await page.EvaluateAsync<string>(@"() => {
                            const all = document.querySelectorAll('a');
                            const empty = Array.from(all)
                                .filter(a => !a.getAttribute('href') || a.getAttribute('href').trim() === '')
                                .map(a => a.textContent?.trim() || '?');
                            return empty.length === 0 ? 'OK' : 'EMPTY:' + empty.slice(0,10).join('||');
                        }");
                        if (linkCheck.StartsWith("EMPTY:")) return Fail(sr, $"Links with empty/missing href: {linkCheck[6..]}");
                        sr.Detail = "All <a> elements have non-empty href ✓";
                        break;

                    case "checkaria":
                        var ariaCheck = await page.EvaluateAsync<string>(@"() => {
                            const els = document.querySelectorAll('button,input,select,textarea,[role=""button""],[role=""link""]');
                            const missing = Array.from(els).filter(el =>
                                !el.getAttribute('aria-label') && !el.getAttribute('aria-labelledby') &&
                                !el.textContent?.trim() && !el.getAttribute('title') && !el.getAttribute('placeholder')
                            ).map(el => el.tagName + (el.id ? '#'+el.id : ''));
                            return missing.length === 0 ? 'OK' : 'MISSING:' + missing.slice(0,5).join('||');
                        }");
                        if (ariaCheck.StartsWith("MISSING:")) return Fail(sr, $"Interactive elements missing accessible name: {ariaCheck[8..]}");
                        sr.Detail = "All interactive elements have accessible names ✓";
                        break;

                    case "checkheadings":
                        var hCheck = await page.EvaluateAsync<string>(@"() => {
                            const hs = document.querySelectorAll('h1,h2,h3,h4,h5,h6');
                            if (hs.length === 0) return 'NONE';
                            const h1 = document.querySelectorAll('h1').length;
                            if (h1 === 0) return 'NO_H1';
                            return 'OK:' + hs.length + ' headings, ' + h1 + ' H1';
                        }");
                        if (hCheck == "NONE") return Fail(sr, "No heading elements found — accessibility failure");
                        if (hCheck == "NO_H1") return Fail(sr, "No <h1> found — document structure issue");
                        sr.Detail = $"Heading structure OK ({hCheck[3..]}) ✓";
                        break;

                    case "checktitle":
                        var pageTitle = await page.TitleAsync();
                        if (string.IsNullOrWhiteSpace(pageTitle)) return Fail(sr, "Page has no <title> tag");
                        if (!string.IsNullOrEmpty(step.expected) &&
                            !pageTitle.Contains(step.expected, StringComparison.OrdinalIgnoreCase))
                            return Fail(sr, $"Title '{pageTitle}' doesn't contain '{step.expected}'");
                        sr.Detail = $"Page title = '{pageTitle}' ✓";
                        break;

                    case "checkmetatags":
                        var metaCheck = await page.EvaluateAsync<string>(@"() => {
                            const desc = document.querySelector('meta[name=""description""]');
                            const vp = document.querySelector('meta[name=""viewport""]');
                            const miss = [];
                            if (!desc) miss.push('description');
                            if (!vp) miss.push('viewport');
                            return miss.length === 0 ? 'OK' : 'MISSING:' + miss.join(',');
                        }");
                        if (metaCheck.StartsWith("MISSING:")) return Fail(sr, $"Missing meta tags: {metaCheck[8..]}");
                        sr.Detail = "Required meta tags present (description, viewport) ✓";
                        break;

                    case "checklayout":
                        var layoutCheck = await page.EvaluateAsync<string>(@"() => {
                            const bw = document.body.scrollWidth;
                            const vw = window.innerWidth;
                            return bw > vw + 5 ? 'OVERFLOW:scrollWidth=' + bw + ' viewportWidth=' + vw : 'OK';
                        }");
                        if (layoutCheck.StartsWith("OVERFLOW")) return Fail(sr, $"Horizontal overflow: {layoutCheck}");
                        var vp = page.ViewportSize;
                        sr.Detail = $"No horizontal overflow at {vp?.Width}×{vp?.Height} ✓";
                        break;

                    case "checklayoutadvanced":
                        var (layoutOk, layoutDetail) = await LayoutAnalyzer.AnalyzeAsync(page);
                        if (!layoutOk) return Fail(sr, layoutDetail);
                        sr.Detail = layoutDetail;
                        break;

                    case "checkformlabels":
                        var formCheck = await page.EvaluateAsync<string>(@"() => {
                            const inputs = document.querySelectorAll(
                                'input:not([type=hidden]):not([type=submit]):not([type=button]):not([type=image])');
                            const unlabeled = Array.from(inputs).filter(inp => {
                                const lbl = inp.id && document.querySelector('label[for=""' + inp.id + '""]');
                                const aria = inp.getAttribute('aria-label') || inp.getAttribute('aria-labelledby');
                                const ph = inp.getAttribute('placeholder');
                                return !lbl && !aria && !ph;
                            }).map(inp => (inp.name || inp.id || inp.type || '?'));
                            return unlabeled.length === 0 ? 'OK' : 'UNLABELED:' + unlabeled.join('||');
                        }");
                        if (formCheck.StartsWith("UNLABELED:")) return Fail(sr, $"Inputs without label/aria/placeholder: {formCheck[10..]}");
                        sr.Detail = "All form inputs are labeled ✓";
                        break;

                    case "checkelementcount":
                        var countResolve = await ResolveLocatorAsync(page, step);
                        if (countResolve.fail != null) return countResolve.fail;
                        var actual = await countResolve.resolved!.Locator!.CountAsync();
                        var minCount = step.count > 0 ? step.count : 1;
                        sr.UsedSelector = countResolve.resolved.UsedStrategy;
                        if (actual < minCount)
                            return Fail(sr, $"Expected ≥{minCount} element(s), found {actual} using {sr.UsedSelector}");
                        sr.Detail = $"Found {actual} element(s) via {sr.UsedSelector} ✓";
                        break;

                    case "checkcontrast":
                        var contrastCheck = await page.EvaluateAsync<string>(@"() => {
                            function lum(r,g,b){return [r,g,b].reduce((s,v,i)=>{v/=255;v=v<=0.03928?v/12.92:Math.pow((v+0.055)/1.055,2.4);return s+v*[0.2126,0.7152,0.0722][i];},0);}
                            function ratio(c1,c2){const [l1,l2]=[lum(...c1),lum(...c2)].sort((a,b)=>b-a);return (l1+0.05)/(l2+0.05);}
                            function parse(s){const m=s&&s.match(/\d+/g);return m?[+m[0],+m[1],+m[2]]:null;}
                            const els=document.querySelectorAll('p,h1,h2,h3,h4,h5,h6,a,button,label');
                            let fails=0;
                            els.forEach(el=>{const st=window.getComputedStyle(el);const fg=parse(st.color),bg=parse(st.backgroundColor);if(fg&&bg&&ratio(fg,bg)<4.5)fails++;});
                            return fails===0?'OK':'WARN:'+fails+' element(s) may fail WCAG AA (4.5:1)';
                        }");
                        sr.Detail = contrastCheck == "OK" ? "Colour contrast passes WCAG AA heuristic ✓" : $"⚠ {contrastCheck}";
                        break;

                    case "checktaborder":
                        var tabCheck = await page.EvaluateAsync<int>(@"() => document.querySelectorAll('[tabindex=""-1""]').length");
                        sr.Detail = tabCheck > 0 ? $"⚠ {tabCheck} element(s) have tabindex=-1" : "Tab order: no elements artificially excluded ✓";
                        break;

                    case "checkskiplink":
                        var skipCheck = await page.EvaluateAsync<bool>(@"() => !!document.querySelector(
                            'a[href=""#main""],a[href=""#content""],a[href=""#maincontent""],.skip-link,[class*=""skip""]')");
                        sr.Detail = skipCheck ? "Skip navigation link present ✓" : "⚠ No skip-to-content link found (WCAG 2.4.1)";
                        break;

                    default:
                        sr.Status = TestStatus.Skipped;
                        sr.Detail = $"Unknown action '{step.action}' — skipped";
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"   ⚠ Unknown action skipped: {step.action}");
                        Console.ResetColor();
                        return sr;
                }

                if (step.action is not "dismissoverlays" and not "screenshot" and not "visualverify")
                {
                    var intentLabel = !string.IsNullOrEmpty(sr.Intent) ? $" [{sr.Intent}]" : "";
                    Console.WriteLine($"   ✓ {sr.Detail}{intentLabel}");
                }
                else if (step.action == "dismissoverlays" || step.action == "screenshot")
                {
                    Console.WriteLine($"   ✓ {sr.Detail}");
                }

                await page.WaitForTimeoutAsync(200);
            }
            catch (Exception ex)
            {
                return Fail(sr, ex.Message);
            }

            return sr;
        }

        private async Task<StepResult> RunInteractionAsync(
            IPage page, TestStep step, StepResult sr, Func<ILocator, Task<string>> action)
        {
            var resolve = await ResolveLocatorAsync(page, step);
            if (resolve.fail != null) return resolve.fail;

            try
            {
                var detail = await action(resolve.resolved!.Locator!);
                sr.UsedSelector = resolve.resolved.UsedStrategy;
                sr.Repaired = resolve.repaired;
                sr.Detail = detail;
                return sr;
            }
            catch (Exception ex)
            {
                return Fail(sr, ex.Message);
            }
        }

        private async Task<(LocatorResolveResult? resolved, bool repaired, StepResult? fail)> ResolveLocatorAsync(
            IPage page, TestStep step)
        {
            if (step.dismissOverlays)
                await StateAwareHelper.PreparePageForInteractionAsync(page);

            var resolved = await SmartLocatorResolver.ResolveAsync(page, step.target, step.selector);
            if (resolved.Locator != null)
                return (resolved, false, null);

            var (repaired, repairDetail) = await TestRepairEngine.TryRepairAsync(page, step);
            if (repaired != null)
            {
                step.target = repaired;
                resolved = await SmartLocatorResolver.ResolveAsync(page, repaired);
                if (resolved.Locator != null)
                    return (resolved, true, null);
            }

            var err = resolved.Error ?? repairDetail;
            var failSr = new StepResult
            {
                Action = step.action ?? "",
                Intent = step.intent ?? step.target?.Intent ?? "",
                Status = TestStatus.Failed
            };
            return (null, false, Fail(failSr, err));
        }

        private async Task<StepResult> RunVisualVerifyAsync(IPage page, string testName, StepResult sr)
        {
            var safe = string.Concat(testName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            var baseline = Path.Combine(_baselineDir, $"{safe}_baseline.png");
            var current = Path.Combine(_screenshotDir, $"{safe}_current_{DateTime.UtcNow:HHmmss}.png");
            await page.ScreenshotAsync(new() { Path = current, FullPage = true });

            if (!File.Exists(baseline))
            {
                File.Copy(current, baseline);
                sr.Detail = $"Visual baseline created → {baseline}";
                return sr;
            }

            var baselineSize = new FileInfo(baseline).Length;
            var currentSize = new FileInfo(current).Length;
            var sizeDiff = Math.Abs(baselineSize - currentSize) / (double)Math.Max(baselineSize, 1);

            if (sizeDiff > 0.35)
                return Fail(sr, $"Visual regression suspected — screenshot size changed {sizeDiff:P0}. Current: {current}");

            sr.Detail = $"Visual check passed (DOM + screenshot hybrid) → {current}";
            return sr;
        }

        private async Task<(bool, string, string?)> EvaluateAssertionAsync(IPage page, string assertion)
        {
            try
            {
                if (assertion.StartsWith("url=", StringComparison.OrdinalIgnoreCase))
                {
                    var expected = assertion[4..];
                    return page.Url.Contains(expected)
                        ? (true, $"URL contains '{expected}' ✓", assertion)
                        : (false, $"URL '{page.Url}' doesn't contain '{expected}'", assertion);
                }
                if (assertion.StartsWith("title=", StringComparison.OrdinalIgnoreCase))
                {
                    var expected = assertion[6..];
                    var actual = await page.TitleAsync();
                    return actual.Contains(expected, StringComparison.OrdinalIgnoreCase)
                        ? (true, $"Title contains '{expected}' ✓", assertion)
                        : (false, $"Title '{actual}' doesn't contain '{expected}'", assertion);
                }

                if (assertion.Contains(':') && !assertion.StartsWith("css:"))
                {
                    var target = new ElementTarget { FallbackChain = new List<string> { assertion } };
                    var resolved = await SmartLocatorResolver.ResolveAsync(page, target);
                    if (resolved.Locator == null)
                        return (false, resolved.Error, assertion);
                    await Assertions.Expect(resolved.Locator).ToBeVisibleAsync(new() { Timeout = 6000 });
                    return (true, $"'{resolved.UsedStrategy}' is visible ✓", resolved.UsedStrategy);
                }

                var css = assertion.StartsWith("css:") ? assertion[4..] : assertion;
                if (SelectorEngine.IsBrittle(css))
                    return (false, $"Brittle assertion selector rejected: '{css}'", css);

                var loc = page.Locator(css);
                var count = await loc.CountAsync();
                if (count != 1)
                    return (false, $"Assertion selector matched {count} elements (expected 1): '{css}'", css);

                await Assertions.Expect(loc).ToBeVisibleAsync(new() { Timeout = 6000 });
                return (true, $"'{css}' is visible ✓", css);
            }
            catch (Exception ex)
            {
                return (false, $"'{assertion}' — {ex.Message}", assertion);
            }
        }

        private async Task<string> CaptureScreenshotAsync(IPage page, string testName, string suffix)
        {
            try
            {
                var safe = string.Concat(testName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
                var path = Path.Combine(_screenshotDir, $"{safe}_{suffix}_{DateTime.UtcNow:HHmmss}.png");
                await page.ScreenshotAsync(new() { Path = path, FullPage = true });
                return path;
            }
            catch { return "(screenshot unavailable)"; }
        }

        private static StepResult Fail(StepResult sr, string message)
        {
            sr.Status = TestStatus.Failed;
            sr.ErrorMessage = message;
            sr.Detail = message;
            return sr;
        }

        private void PrintSummary(List<TestResult> results)
        {
            int passed = results.Count(r => r.Status == TestStatus.Passed);
            int failed = results.Count(r => r.Status == TestStatus.Failed);
            int skipped = results.Count(r => r.Status == TestStatus.Skipped);

            Console.WriteLine("\n" + new string('═', 62));
            Console.WriteLine("  TEST RUN COMPLETE");
            Console.WriteLine(new string('═', 62));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✅  Passed  : {passed}");
            Console.ResetColor();
            Console.ForegroundColor = failed > 0 ? ConsoleColor.Red : ConsoleColor.Gray;
            Console.WriteLine($"  ❌  Failed  : {failed}");
            Console.ResetColor();
            Console.WriteLine($"  ⏭   Skipped : {skipped}");
            Console.WriteLine($"  📋  Total   : {results.Count}");
            Console.WriteLine(new string('═', 62));
        }

        private void PrintFailureInsights(List<TestResult> results)
        {
            var failed = results.Where(r => r.Status == TestStatus.Failed).ToList();
            if (failed.Count == 0) return;

            foreach (var r in failed)
                r.FailureInsight ??= FailureSummaryGenerator.BuildInsight(r);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n" + FailureSummaryGenerator.Generate(results));
            Console.ResetColor();
        }
    }
}
