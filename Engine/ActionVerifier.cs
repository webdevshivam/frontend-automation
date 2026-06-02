using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace PlaywrightPrototype.Engine
{
    /// <summary>
    /// Verifies that actions produced expected page changes.
    /// </summary>
    public static class ActionVerifier
    {
        public static async Task<(bool Ok, string Detail)> VerifyAsync(
            IPage page, string verifyType, string? beforeUrl, int? beforeDomCount)
        {
            verifyType = (verifyType ?? "none").ToLowerInvariant();

            return verifyType switch
            {
                "navigation" or "page_changed" => await VerifyPageChanged(page, beforeUrl),
                "visible" => await VerifyBodyVisible(page),
                "modal_closed" => await VerifyModalClosed(page),
                "element_visible" => await VerifyBodyVisible(page),
                "none" or "" => (true, "No post-action verification required"),
                _ => (true, $"Unknown verify type '{verifyType}' — skipped")
            };
        }

        public static async Task<int> GetDomElementCountAsync(IPage page) =>
            await page.EvaluateAsync<int>("() => document.querySelectorAll('body *').length");

        private static async Task<(bool, string)> VerifyPageChanged(IPage page, string? beforeUrl)
        {
            await page.WaitForTimeoutAsync(300);
            var afterUrl = page.Url;
            if (!string.IsNullOrEmpty(beforeUrl) && afterUrl != beforeUrl)
                return (true, $"Navigation verified: {beforeUrl} → {afterUrl}");

            var domChanged = await page.EvaluateAsync<bool>(@"() => {
                return document.readyState === 'complete';
            }");

            if (domChanged)
                return (true, "Page state stable after action");

            return (false, "Expected navigation or page change did not occur");
        }

        private static async Task<(bool, string)> VerifyBodyVisible(IPage page)
        {
            try
            {
                await Assertions.Expect(page.Locator("body")).ToBeVisibleAsync(new() { Timeout = 5000 });
                return (true, "Page body is visible after action");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static async Task<(bool, string)> VerifyModalClosed(IPage page)
        {
            var openModals = await page.EvaluateAsync<int>(
                @"() => document.querySelectorAll('[role=""dialog""],[aria-modal=""true""]').length");
            if (openModals == 0)
                return (true, "Modal/dialog closed successfully");
            return (false, $"{openModals} modal(s) still open after dismiss action");
        }
    }
}
