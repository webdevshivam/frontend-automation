using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlaywrightPrototype.Tests;

[NonParallelizable]
[TestFixture]
public class UnitTest1
{

    
    private const string BillingUrl =
        "https://patients-billing-env-staging-clarity-rcm.vercel.app/lone%20pine%20derm/019e8947-5c82-74e8-a1c0-419801cc8538";

    private const int DefaultTimeoutMs = 45_000;
    private const int StripeReadyTimeoutMs = 90_000;
    private const int ImageLoadTimeoutMs = 8_000;

    private static readonly Regex CurrencyRegex = new(@"\$[\d,.]+");
    private static readonly Regex GreetingRegex = new(@"Hi .+,");
    private static readonly Regex DueDateRegex = new(@"Due .+");
    private static readonly Regex PayButtonAmountRegex = new(@"Pay [\d,.]+ dollars");

    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IBrowserContext _context = null!;
    private IPage _page = null!;

    private string _currentStep = "Setup";
    private readonly List<(string Step, string Check, bool Passed, string Detail)> _checkResults = new();
    private readonly List<string> _imageFailures = new();

    // ── Locators (stable selectors) ───────────────────────────────────────────

    private ILocator PayNowButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "Pay bill now" })
            .Or(_page.GetByRole(AriaRole.Button, new() { Name = "Pay now" }));

    // Landing CTA: <button aria-label="View your medical bill details"> with receipt icon + label text inside.
    private ILocator ViewBillButton =>
        _page.GetByRole(AriaRole.Button, new() { Name = "View your medical bill details" });

    private ILocator ViewBillButtonTitle =>
        ViewBillButton.Locator("span.text-sm.font-semibold");

    private ILocator ViewBillButtonSubtitle =>
        ViewBillButton.GetByText("Verify patient identity to view statement");

    private ILocator ViewBillReceiptIcon =>
        ViewBillButton.Locator(".tabler-icon-receipt");

    private ILocator BrandLogo => _page.GetByRole(AriaRole.Img, new() { Name = "Brand logo" });

    private ILocator BackArrowIcon => _page.Locator(".tabler-icon-arrow-left").First;

    private ILocator LandingHeading =>
        _page.GetByRole(AriaRole.Heading, new() { Name = "Please review and pay your balance" });

    private ILocator MakePaymentHeading =>
        _page.GetByRole(AriaRole.Heading, new() { Name = "Make a Payment" });

    private ILocator VerifyIdentityHeading =>
        _page.GetByRole(AriaRole.Heading, new() { Name = "Verify your identity" });

    [SetUp]
    public async Task SetUp()
    {
        _checkResults.Clear();
        _imageFailures.Clear();
        _currentStep = "Setup";

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 200
        });

        await CreateFreshContextAndPageAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        PrintCheckSummary();

        try { await _context?.CloseAsync()!; } catch { /* ignore */ }
        try { await _browser?.CloseAsync()!; } catch { /* ignore */ }
        _playwright?.Dispose();
    }

    private async Task CreateFreshContextAndPageAsync()
    {
        if (_context != null)
        {
            try { await _context.CloseAsync(); } catch { /* ignore */ }
        }

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1536, Height = 730 }
        });
        _context.SetDefaultTimeout(DefaultTimeoutMs);
        _context.SetDefaultNavigationTimeout(60_000);
        _page = await _context.NewPageAsync();
    }

    [Test]
    public async Task MainFlow_VerifiesElementsValidationAndNavigation()
    {
        _currentStep = "Step 1 — Landing page";
        LogStep(_currentStep);
        await OpenLandingPageAsync();
        var landingAmount = await AssertLandingPageEssentialsAsync();

        _currentStep = "Step 2 — Make a Payment";
        LogStep(_currentStep);
        // #region agent log
        DbgLog("C", "MainFlow:104", "Step2 starting", new { landingAmount, runId = "post-fix" });
        // #endregion
        await ClickAndWaitAsync(PayNowButton, MakePaymentHeading, "Pay now", urlFragment: "make-a-payment");
        // #region agent log
        DbgLog("C", "MainFlow:108", "After Pay now click — entering WaitForMakePaymentFormReadyAsync", new { url = _page.Url });
        // #endregion
        await WaitForMakePaymentFormReadyAsync();
        await AssertMakePaymentPageAsync(landingAmount);
        await AssertPaymentFormValidationAsync();
        await ValidatePageImagesAsync("Make a Payment");

        _currentStep = "Step 3 — Back to landing";
        LogStep(_currentStep);
        await ClickArrowBackAsync(LandingHeading, "Back to landing from payment");
        await AssertLandingPageEssentialsAsync();
        await AssertViewBillButtonOnLandingAsync();

        _currentStep = "Step 4 — Verify identity";
        LogStep(_currentStep);
        // #region agent log
        DbgLog("C", "MainFlow:Step4", "Step4 starting — clicking View bill button", new { url = _page.Url, runId = "post-fix4" });
        // #endregion
        await ClickAndWaitAsync(ViewBillButton, VerifyIdentityHeading, "View bill details");
        // #region agent log
        DbgLog("C", "MainFlow:Step4Done", "Step4 navigation complete", new { url = _page.Url, runId = "post-fix4" });
        // #endregion
        await AssertVerifyIdentityPageAsync();
        await AssertVerifyIdentityValidationAsync();
        await ValidatePageImagesAsync("Verify identity");

        _currentStep = "Step 5 — Landing image validation";
        LogStep(_currentStep);
        await OpenLandingPageForImageValidationAsync();
        await AssertLandingPageImagesAsync();

        // Report broken images across ALL pages at once (so every page is covered, not just the first failure).
        if (_imageFailures.Count > 0)
        {
            // #region agent log
            DbgLog("D", "MainFlow:imageFailures", "Broken images detected across pages", new { failures = _imageFailures, runId = "post-fix6" });
            // #endregion
            Assert.Fail("Image load failures detected:\n  - " + string.Join("\n  - ", _imageFailures));
        }

        LogStep("All steps completed successfully.");
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private async Task OpenLandingPageAsync()
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (attempt > 1)
                    await CreateFreshContextAndPageAsync();

                LogInfo($"Opening billing URL (attempt {attempt}/3)...");
                await _page.GotoAsync(BillingUrl, new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60_000
                });

                LogInfo($"Loaded: {_page.Url}");
                await WaitForAppLoadingToFinishAsync();
                await LandingHeading.WaitForAsync(new()
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = DefaultTimeoutMs
                });

                await PayNowButton.WaitForAsync(new()
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = DefaultTimeoutMs
                });

                RecordPass("Navigation", "Landing page opened and Pay now is visible");
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                RecordFail("Navigation", $"Open landing attempt {attempt}", ex.Message);
                await Task.Delay(2000);
            }
        }

        throw new InvalidOperationException("Could not open landing page after 3 attempts.", lastError);
    }

    private async Task WaitForAppLoadingToFinishAsync()
    {
        // #region agent log
        var spinnerCounts = new Dictionary<string, int>
        {
            ["loadingPaymentForm"] = await _page.GetByText("Loading payment form...").CountAsync(),
            ["loadingExact"] = await _page.GetByText("Loading", new() { Exact = true }).CountAsync(),
            ["animateSpin"] = await _page.Locator(".animate-spin").CountAsync()
        };
        DbgLog("B", "WaitForAppLoadingToFinishAsync:entry", "Spinner counts before wait", spinnerCounts);
        // #endregion

        // App shows "Loading" or "Loading payment form..." while React/Stripe boot
        var spinners = new[]
        {
            _page.GetByText("Loading payment form..."),
            _page.GetByText("Loading", new() { Exact = true }),
            _page.Locator(".animate-spin")
        };

        foreach (var spinner in spinners)
        {
            if (await spinner.CountAsync() == 0)
                continue;

            LogInfo("Waiting for loading indicator to disappear...");
            try
            {
                await spinner.First.WaitForAsync(new()
                {
                    State = WaitForSelectorState.Hidden,
                    Timeout = 60_000
                });
            }
            catch (TimeoutException)
            {
                // Spinner may not hide on slow networks — continue once main content appears
                LogInfo("Loading indicator still visible — continuing when main content appears.");
                // #region agent log
                DbgLog("B", "WaitForAppLoadingToFinishAsync:timeout", "Spinner wait timed out — continuing", spinnerCounts);
                // #endregion
            }
        }

        // #region agent log
        DbgLog("B", "WaitForAppLoadingToFinishAsync:exit", "Loading wait finished", new { url = _page.Url });
        // #endregion
    }

    private async Task ClickAndWaitAsync(
        ILocator clickTarget,
        ILocator pageReadyMarker,
        string actionName,
        string? urlFragment = null,
        bool urlShouldContain = true)
    {
        await clickTarget.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = DefaultTimeoutMs });
        await clickTarget.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(clickTarget).ToBeEnabledAsync(new() { Timeout = DefaultTimeoutMs });

        LogInfo($"Clicking: {actionName}");
        var urlBefore = _page.Url;
        // #region agent log
        DbgLog("C", "ClickAndWaitAsync:preClick", $"About to click {actionName}", new { url = urlBefore, urlFragment, urlShouldContain, runId = "post-fix2" });
        // #endregion

        if (!string.IsNullOrEmpty(urlFragment))
        {
            if (urlShouldContain)
            {
                await Task.WhenAll(
                    _page.WaitForURLAsync($"**/*{urlFragment}*", new() { Timeout = DefaultTimeoutMs }),
                    clickTarget.ClickAsync(new() { Timeout = DefaultTimeoutMs }));
            }
            else
            {
                await clickTarget.ClickAsync(new() { Timeout = DefaultTimeoutMs });
                try
                {
                    await _page.WaitForURLAsync(
                        url => !url.Contains(urlFragment, StringComparison.OrdinalIgnoreCase),
                        new() { Timeout = DefaultTimeoutMs });
                }
                catch (TimeoutException)
                {
                    // #region agent log
                    DbgLog("C", "ClickAndWaitAsync:urlWaitTimeout", $"URL did not leave '{urlFragment}' — falling back to marker", new { url = _page.Url, runId = "post-fix2" });
                    // #endregion
                }
            }
        }
        else
        {
            await clickTarget.ClickAsync(new() { Timeout = DefaultTimeoutMs });
            if (_page.Url == urlBefore)
            {
                try
                {
                    await _page.WaitForURLAsync(url => url != urlBefore, new() { Timeout = 10_000 });
                }
                catch (TimeoutException) { /* SPA may update in place */ }
            }
        }

        await WaitForAppLoadingToFinishAsync();
        // #region agent log
        DbgLog("C", "ClickAndWaitAsync:postLoading", $"After loading wait, waiting marker for {actionName}", new { url = _page.Url, runId = "post-fix2" });
        // #endregion
        await pageReadyMarker.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = DefaultTimeoutMs
        });

        RecordPass(_currentStep, $"{actionName} — navigated successfully");
        // #region agent log
        DbgLog("C", "ClickAndWaitAsync:done", $"{actionName} navigation complete", new { url = _page.Url, runId = "post-fix2" });
        // #endregion
    }

    /// <summary>Clicks the back arrow on payment/identity pages (icon may not be wrapped in &lt;a&gt;).</summary>
    private async Task ClickArrowBackAsync(ILocator pageReadyMarker, string actionName)
    {
        await BackArrowIcon.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = DefaultTimeoutMs });
        await BackArrowIcon.ScrollIntoViewIfNeededAsync();

        // #region agent log
        var backInfo = await _page.EvaluateAsync<string>(@"() => {
            const icon = document.querySelector('.tabler-icon-arrow-left');
            if (!icon) return 'missing';
            const chain = [];
            let n = icon;
            for (let i = 0; i < 4 && n; i++) { chain.push(n.tagName); n = n.parentElement; }
            return chain.join('>');
        }");
        DbgLog("C", "ClickArrowBackAsync:pre", actionName, new { backInfo, url = _page.Url, runId = "post-fix6" });
        // #endregion

        LogInfo($"Clicking back: {actionName}");
        var urlBefore = _page.Url;

        // Back control is a non-semantic clickable wrapper (div, not a/button), so use Playwright's
        // real mouse click which hit-tests the topmost element and bubbles to the React handler.
        await BackArrowIcon.ClickAsync(new() { Timeout = DefaultTimeoutMs });

        await WaitForAppLoadingToFinishAsync();

        if (urlBefore.Contains("make-a-payment", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _page.WaitForURLAsync(
                    url => !url.Contains("make-a-payment", StringComparison.OrdinalIgnoreCase),
                    new() { Timeout = DefaultTimeoutMs });
            }
            catch (TimeoutException)
            {
                // #region agent log
                DbgLog("C", "ClickArrowBackAsync:urlTimeout", "URL still on make-a-payment — waiting for landing marker", new { url = _page.Url, runId = "post-fix5" });
                // #endregion
            }
        }

        await pageReadyMarker.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = DefaultTimeoutMs });
        RecordPass(_currentStep, $"{actionName} — navigated successfully");
        // #region agent log
        DbgLog("C", "ClickArrowBackAsync:done", actionName, new { url = _page.Url, runId = "post-fix5" });
        // #endregion
    }

    private async Task OpenLandingPageForImageValidationAsync()
    {
        LogInfo("Reloading landing page for image validation...");
        // #region agent log
        DbgLog("D", "OpenLandingPageForImageValidationAsync", "Navigating to billing URL for Step 5", new { url = _page.Url, runId = "post-fix5" });
        // #endregion

        await _page.GotoAsync(BillingUrl, new()
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await WaitForAppLoadingToFinishAsync();
        await LandingHeading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = DefaultTimeoutMs });
        await BrandLogo.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = DefaultTimeoutMs });
        RecordPass(_currentStep, "Landing page reloaded for image checks");
    }

    private async Task WaitForMakePaymentFormReadyAsync()
    {
        LogInfo("Waiting for Make a Payment form to fully render...");
        // #region agent log
        DbgLog("A", "WaitForMakePaymentFormReadyAsync:entry", "Starting payment form ready wait", new { url = _page.Url });
        // #endregion

        await MakePaymentHeading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = DefaultTimeoutMs });
        // #region agent log
        DbgLog("A", "WaitForMakePaymentFormReadyAsync:heading", "Make a Payment heading visible", new { url = _page.Url, runId = "post-fix2" });
        // #endregion
        await _page.GetByText("Order Summary").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = DefaultTimeoutMs });
        // #region agent log
        DbgLog("A", "WaitForMakePaymentFormReadyAsync:orderSummary", "Order Summary visible", new { runId = "post-fix2" });
        // #endregion
        await _page.GetByText("Add payment details").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = DefaultTimeoutMs });
        // #region agent log
        DbgLog("A", "WaitForMakePaymentFormReadyAsync:addPayment", "Add payment details visible", new { runId = "post-fix2" });
        // #endregion

        await _page.Locator("div.flex.justify-between")
            .Filter(new() { HasText = "Total amount due" })
            .GetByText(CurrencyRegex)
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = DefaultTimeoutMs });
        // #region agent log
        DbgLog("A", "WaitForMakePaymentFormReadyAsync:amount", "Order summary amount visible", new { runId = "post-fix2" });
        // #endregion

        LogInfo("Waiting for payment form fields (cardholder + pay button)...");
        // #region agent log
        var iframeSnapshot = await _page.EvaluateAsync<string>(@"() => JSON.stringify(
            [...document.querySelectorAll('iframe')].map(f => ({
                src: (f.getAttribute('src')||'').substring(0,100),
                w: Math.round(f.getBoundingClientRect().width),
                h: Math.round(f.getBoundingClientRect().height)
            }))
        )");
        DbgLog("A", "WaitForMakePaymentFormReadyAsync:preFields", "Iframe snapshot before field wait", new { iframes = iframeSnapshot, runId = "post-fix" });
        // #endregion

        var cardholder = _page.Locator("#cardholder-name");
        await cardholder.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = StripeReadyTimeoutMs });
        // #region agent log
        DbgLog("E", "WaitForMakePaymentFormReadyAsync:cardholder", "Cardholder field visible", new { runId = "post-fix" });
        // #endregion
        await Assertions.Expect(cardholder).ToBeEditableAsync(new() { Timeout = DefaultTimeoutMs });

        var payButton = _page.GetByRole(AriaRole.Button, new() { NameRegex = PayButtonAmountRegex });
        await payButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = DefaultTimeoutMs });
        // #region agent log
        DbgLog("E", "WaitForMakePaymentFormReadyAsync:payButton", "Pay button visible", new { count = await payButton.CountAsync(), runId = "post-fix" });
        // #endregion
        await Assertions.Expect(payButton).ToBeEnabledAsync(new() { Timeout = DefaultTimeoutMs });

        LogInfo("Checking Stripe iframe (soft wait, max 30s)...");
        try
        {
            await _page.WaitForFunctionAsync(
                @"() => {
                    const iframe = document.querySelector('iframe[src*=""elements-inner-payment""]');
                    return !!iframe && iframe.getBoundingClientRect().height > 0;
                }",
                null,
                new PageWaitForFunctionOptions { Timeout = 30_000 });
            // #region agent log
            DbgLog("A", "WaitForMakePaymentFormReadyAsync:postStripe", "Stripe iframe detected", new { runId = "post-fix" });
            // #endregion
        }
        catch (TimeoutException ex)
        {
            // #region agent log
            DbgLog("A", "WaitForMakePaymentFormReadyAsync:stripeTimeout", "Stripe iframe soft wait timed out — continuing", new { ex.Message, runId = "post-fix" });
            // #endregion
            LogInfo("Stripe iframe still loading — continuing because cardholder and pay button are ready.");
        }

        RecordPass(_currentStep, "Make a Payment form fully ready");
    }

    // ── Assertions ────────────────────────────────────────────────────────────

    /// <summary>Fast landing checks before clicking Pay now — does not block on CDN image load.</summary>
    private async Task<string> AssertLandingPageEssentialsAsync()
    {
        await AssertPageTitleAsync();
        await AssertVisible("Skip to main content", _page.GetByRole(AriaRole.Link, new() { Name = "Skip to main content" }));
        await AssertVisible("Brand logo (header) — element present (load checked in Step 5)", BrandLogo);

        await AssertVisible("Patient greeting (dynamic name)", _page.GetByRole(AriaRole.Heading, new() { NameRegex = GreetingRegex }));
        await AssertVisible("Please review and pay your balance", LandingHeading);
        await AssertVisible("Pay now button", PayNowButton);

        var amountElement = _page.GetByLabel(new Regex(@"Total amount due:"));
        await AssertVisible("Dynamic amount", amountElement);
        var amountText = await amountElement.GetAttributeAsync("aria-label")
                         ?? await amountElement.TextContentAsync() ?? "";
        var landingAmount = ExtractCurrency(amountText);
        RecordPass(_currentStep, $"Dynamic amount = {landingAmount}");

        // #region agent log
        DbgLog("D", "AssertLandingPageEssentialsAsync:done", "Essentials done — ready to click Pay now", new { landingAmount, runId = "post-fix" });
        // #endregion
        return landingAmount;
    }

    private async Task AssertViewBillButtonOnLandingAsync()
    {
        await AssertVisible("View bill details button", ViewBillButton);
        await AssertVisible("View bill — receipt icon", ViewBillReceiptIcon);
        await Assertions.Expect(ViewBillButtonTitle).ToHaveTextAsync(new Regex(@"Want to see what this bill is for\s*\?"));
        RecordPass(_currentStep, "View bill button label — Want to see what this bill is for?");
        await AssertVisible("View bill button subtitle", ViewBillButtonSubtitle);
        await Assertions.Expect(ViewBillButton).ToBeEnabledAsync(new() { Timeout = DefaultTimeoutMs });
        RecordPass(_currentStep, "View bill details button — enabled and ready to click");
    }

    /// <summary>Image load checks for landing page — run after full navigation flow completes.</summary>
    private async Task AssertLandingPageImagesAsync()
    {
        await AssertVisible("Lone Pine Dermatology", _page.GetByRole(AriaRole.Heading, new() { Name = "Lone Pine Dermatology" }));
        await AssertVisible("LP Derm LLC", _page.GetByText("LP Derm LLC"));
        await AssertVisible("Total Amount Due", _page.GetByText("Total Amount Due"));
        await AssertVisible("Due date", _page.GetByText(DueDateRegex));
        await AssertViewBillButtonOnLandingAsync();

        foreach (var card in new[] { "visa", "master-card", "american-express", "discover", "apple-pay" })
            await AssertVisible($"Card logo: {card}", _page.GetByRole(AriaRole.Img, new() { Name = card }));

        await AssertContactAndFooterVisibleAsync();
        await ValidatePageImagesAsync("Landing");
        RecordPass(_currentStep, "Landing page image checks completed");
    }

    private async Task AssertMakePaymentPageAsync(string expectedAmount)
    {
        await AssertVisible("Make a Payment heading", MakePaymentHeading);
        await AssertVisible("Back arrow", BackArrowIcon);
        await AssertVisible("Order Summary", _page.GetByText("Order Summary"));
        await AssertVisible("Add payment details", _page.GetByText("Add payment details"));

        var orderSummaryAmount = _page.Locator("div.flex.justify-between")
            .Filter(new() { HasText = "Total amount due" })
            .GetByText(CurrencyRegex);
        await AssertVisible("Order summary amount", orderSummaryAmount);

        var paymentAmount = ExtractCurrency(await orderSummaryAmount.TextContentAsync() ?? "");
        Assert.That(paymentAmount, Is.EqualTo(expectedAmount));
        RecordPass(_currentStep, $"Amount matches landing page ({paymentAmount})");

        await AssertVisible("Stripe iframe", _page.Locator("iframe[src*='elements-inner-payment']").First);
        await AssertVisible("Cardholder name", _page.Locator("#cardholder-name"));
        await AssertVisible("Pay button", _page.GetByRole(AriaRole.Button, new() { NameRegex = PayButtonAmountRegex }));
        await AssertContactAndFooterVisibleAsync();
    }

    private async Task AssertPaymentFormValidationAsync()
    {
        var cardholder = _page.Locator("#cardholder-name");
        var payButton = _page.GetByRole(AriaRole.Button, new() { NameRegex = PayButtonAmountRegex });

        await cardholder.FillAsync("");
        Assert.That(await cardholder.EvaluateAsync<bool>("el => !el.checkValidity()"), Is.True);
        RecordPass(_currentStep, "Validation: empty cardholder rejected");

        await cardholder.FillAsync("Test Patient");
        await Assertions.Expect(cardholder).ToHaveValueAsync("Test Patient");
        await Assertions.Expect(payButton).ToBeEnabledAsync();
        RecordPass(_currentStep, "Validation: valid cardholder accepted");
    }

    private async Task AssertVerifyIdentityPageAsync()
    {
        await AssertVisible("Verify your identity", VerifyIdentityHeading);
        await AssertVisible("DOB field", _page.Locator("#dob"));
        await AssertVisible("Last name field", _page.Locator("#lastName"));

        var continueButton = _page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).First;
        await AssertVisible("Continue button", continueButton);
        await AssertDisabled("Continue (initial)", continueButton);
        await AssertContactAndFooterVisibleAsync();
    }

    private async Task AssertVerifyIdentityValidationAsync()
    {
        var dob = _page.Locator("#dob");
        var lastName = _page.Locator("#lastName");
        var continueButton = _page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).First;

        await dob.FillAsync("01/15/1990");
        await lastName.FillAsync("Patient");
        await AssertEnabled("Continue (valid form)", continueButton);
        RecordPass(_currentStep, "Validation: valid DOB + last name enables Continue");

        await lastName.FillAsync("");
        await AssertDisabled("Continue (empty last name)", continueButton);
        RecordPass(_currentStep, "Validation: empty last name disables Continue");
    }

    private async Task AssertContactAndFooterVisibleAsync()
    {
        await AssertVisible("Contact section", _page.GetByRole(AriaRole.Region, new() { Name = "Contact Information" }));
        await AssertVisible("Footer", _page.Locator("footer[aria-label='Footer']"));
        await AssertVisible("Clarity logo (footer)", _page.GetByRole(AriaRole.Img, new() { Name = "Clarity logo" }));
    }

    private async Task AssertPageTitleAsync()
    {
        await Assertions.Expect(_page).ToHaveTitleAsync(new Regex("Clarity Patient Billing Portal"));
        RecordPass(_currentStep, "Page title OK");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates an image actually loaded (HTTP 200 + decoded with real pixels) but does NOT throw.
    /// Records pass/fail per page and accumulates failures so every page gets reported.
    /// </summary>
    private async Task ValidateImageSoftAsync(string pageLabel, string name, ILocator image, int minWidth = 24, int minHeight = 12)
    {
        var label = $"{pageLabel} · {name}";

        if (await image.CountAsync() == 0)
        {
            RecordPass(_currentStep, $"{label} — not present on this page (skipped)");
            return;
        }

        var (ok, detail) = await TryImageLoadedAsync(name, image, minWidth, minHeight);
        if (ok)
        {
            RecordPass(_currentStep, $"{label} — loaded ({detail})");
        }
        else
        {
            RecordFail(_currentStep, $"{label} — loaded", detail);
            _imageFailures.Add($"{label}: {detail}");
        }
    }

    /// <summary>Hard image assertion — fails the test immediately. Kept for single-image callers.</summary>
    private async Task AssertImageLoaded(string name, ILocator image, int minWidth = 24, int minHeight = 12)
    {
        var (ok, detail) = await TryImageLoadedAsync(name, image, minWidth, minHeight);
        if (!ok)
        {
            RecordFail(_currentStep, $"{name} — loaded", detail);
            Assert.Fail($"{name} did NOT load. {detail}");
        }

        RecordPass(_currentStep, $"{name} — loaded ({detail})");
    }

    /// <summary>
    /// Core load detection via the DOM only: an image that finished loading reports
    /// complete==true with naturalWidth&gt;0 and decodes successfully; a broken image
    /// (e.g. HTTP 403) reports complete==true with naturalWidth==0 and fails decode().
    /// Fast — no network waiting. Never throws.
    /// </summary>
    private async Task<(bool Ok, string Detail)> TryImageLoadedAsync(string name, ILocator image, int minWidth, int minHeight)
    {
        await image.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = DefaultTimeoutMs });
        await image.ScrollIntoViewIfNeededAsync();

        var src = await image.GetAttributeAsync("src") ?? "";
        if (string.IsNullOrWhiteSpace(src))
            return (false, "Image has no src attribute.");

        // #region agent log
        DbgLog("D", "TryImageLoadedAsync:pre", $"{name} check", new { src, url = _page.Url, runId = "post-fix7" });
        // #endregion

        var result = await image.EvaluateAsync<string>(
            @"async (img, limits) => {
                const { minWidth, minHeight, timeoutMs } = limits;

                // If still loading, wait briefly for load/error (no long network polling).
                if (!img.complete) {
                    await new Promise(res => {
                        img.addEventListener('load', res, { once: true });
                        img.addEventListener('error', res, { once: true });
                        setTimeout(res, timeoutMs);
                    });
                }

                const meetsSize = img.complete &&
                    img.naturalWidth >= minWidth &&
                    img.naturalHeight >= minHeight;

                let decoded = false;
                if (meetsSize) {
                    try { await img.decode(); decoded = true; } catch { decoded = false; }
                }

                return JSON.stringify({
                    complete: img.complete,
                    naturalWidth: img.naturalWidth,
                    naturalHeight: img.naturalHeight,
                    meetsSize,
                    decoded
                });
            }",
            new { minWidth, minHeight, timeoutMs = ImageLoadTimeoutMs });

        // #region agent log
        DbgLog("D", "TryImageLoadedAsync:post", $"{name} result", new { result, url = _page.Url, runId = "post-fix7" });
        // #endregion

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        var ok = root.GetProperty("meetsSize").GetBoolean() && root.GetProperty("decoded").GetBoolean();
        var w = root.GetProperty("naturalWidth").GetInt32();
        var h = root.GetProperty("naturalHeight").GetInt32();

        return ok
            ? (true, $"decoded {w}x{h}")
            : (false, $"broken image (naturalSize {w}x{h}, decode failed) src={src}");
    }

    /// <summary>Validates the key images on whichever page we are currently on (soft, non-throwing).</summary>
    private async Task ValidatePageImagesAsync(string pageLabel)
    {
        // #region agent log
        DbgLog("D", "ValidatePageImagesAsync", pageLabel, new { url = _page.Url, runId = "post-fix6" });
        // #endregion
        await ValidateImageSoftAsync(pageLabel, "Brand logo (header)", BrandLogo);
        await ValidateImageSoftAsync(pageLabel, "Clarity logo (footer)", _page.GetByRole(AriaRole.Img, new() { Name = "Clarity logo" }));
    }

    private async Task AssertVisible(string name, ILocator locator)
    {
        try
        {
            await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = DefaultTimeoutMs });
            RecordPass(_currentStep, $"{name} — visible");
        }
        catch (Exception ex)
        {
            RecordFail(_currentStep, $"{name} — visible", ex.Message);
            throw;
        }
    }

    private async Task AssertEnabled(string name, ILocator locator)
    {
        await Assertions.Expect(locator).ToBeEnabledAsync(new() { Timeout = DefaultTimeoutMs });
        RecordPass(_currentStep, $"{name} — enabled");
    }

    private async Task AssertDisabled(string name, ILocator locator)
    {
        await Assertions.Expect(locator).ToBeDisabledAsync(new() { Timeout = DefaultTimeoutMs });
        RecordPass(_currentStep, $"{name} — disabled");
    }

    private static string ExtractCurrency(string text)
    {
        var match = CurrencyRegex.Match(text);
        return match.Success ? match.Value : text.Trim();
    }

    // #region agent log
    private static void DbgLog(string hypothesisId, string location, string message, object? data)
    {
        try
        {
            var logPaths = new[]
            {
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "debug-b7eb3c.log")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "debug-b7eb3c.log"))
            };

            var line = JsonSerializer.Serialize(new
            {
                sessionId = "b7eb3c",
                hypothesisId,
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            foreach (var logFile in logPaths.Distinct())
            {
                try { File.AppendAllText(logFile, line + Environment.NewLine); break; }
                catch { /* try next path */ }
            }
        }
        catch { /* ignore logging failures */ }
    }
    // #endregion

    private void LogStep(string message)
    {
        Console.WriteLine();
        Console.WriteLine(new string('─', 70));
        Console.WriteLine($"▶  {message}");
        Console.WriteLine(new string('─', 70));
    }

    private static void LogInfo(string message) => Console.WriteLine($"   … {message}");

    private void RecordPass(string step, string check)
    {
        _checkResults.Add((step, check, true, "OK"));
        Console.WriteLine($"   ✓  {check}");
    }

    private void RecordFail(string step, string check, string detail)
    {
        _checkResults.Add((step, check, false, detail));
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"   ✗  {check} — {detail}");
        Console.ResetColor();
    }

    private void PrintCheckSummary()
    {
        if (_checkResults.Count == 0) return;

        var passed = _checkResults.Count(r => r.Passed);
        var failed = _checkResults.Count(r => !r.Passed);

        Console.WriteLine();
        Console.WriteLine(new string('═', 70));
        Console.WriteLine("  ELEMENT CHECK SUMMARY");
        Console.WriteLine(new string('═', 70));

        foreach (var group in _checkResults.GroupBy(r => r.Step))
        {
            Console.WriteLine($"\n  [{group.Key}]");
            foreach (var item in group)
            {
                Console.WriteLine($"    {(item.Passed ? "✓" : "✗")}  {item.Check}");
                if (!item.Passed) Console.WriteLine($"       ↳ {item.Detail}");
            }
        }

        Console.WriteLine();
        Console.ForegroundColor = failed > 0 ? ConsoleColor.Red : ConsoleColor.Green;
        Console.WriteLine($"  Passed : {passed}  |  Failed : {failed}  |  Total : {_checkResults.Count}");
        Console.ResetColor();
        Console.WriteLine(new string('═', 70));
    }
}
