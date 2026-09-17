using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace TriviUpE2ETests;

[Parallelizable(ParallelScope.Self)]
public class NavigationTests : PageTest
{
    private readonly string _frontendUrl = "https://triviup.up.railway.app";

    [Test]
    public async Task HomePage_Loads_ShowsQuizzes()
    {
        await Page.GotoAsync(_frontendUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Check for main elements
        var title = Page.GetByText(new Regex("TriviUp|Trivia|Quiz", RegexOptions.IgnoreCase));
        Assert.That(await title.CountAsync() > 0, "Should show app title or quiz content");

        // Check for navigation or quiz list
        var body = await Page.ContentAsync();
        Assert.That(body.Length > 0, "Page should have content");
    }

    [Test]
    public async Task Navigation_ToLogin_ShowsLoginForm()
    {
        await Page.GotoAsync(_frontendUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click login button - it's an anchor tag with "Iniciar Sesión"
        var loginButton = Page.GetByText("Iniciar Sesión").First;
        await loginButton.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should show login form - use generic selector for username input
        var usernameInput = Page.Locator("input[placeholder*='nombre']").First;
        Assert.That(await usernameInput.IsVisibleAsync(), "Should show username input");
    }

    [Test]
    public async Task Navigation_ToSignup_ShowsSignupForm()
    {
        await Page.GotoAsync(_frontendUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click login to get to auth page first
        var loginButton = Page.GetByText("Iniciar Sesión").First;
        await loginButton.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Now click on "Crear Cuenta" tab to switch to register form
        var signupButton = Page.GetByText("Crear Cuenta").First;
        await signupButton.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should show signup form - look for email input
        var emailInput = Page.Locator("input[type='email']").First;
        Assert.That(await emailInput.IsVisibleAsync(), "Should show email input");
    }

    [Test]
    public async Task Footer_ContainsCopyright()
    {
        await Page.GotoAsync(_frontendUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Scroll to bottom if needed and check for footer
        await Page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
        await Page.WaitForTimeoutAsync(500);

        var footer = Page.GetByText(new Regex("©|Copyright|2024|2025|2026", RegexOptions.IgnoreCase));
        Assert.That(await footer.CountAsync() > 0, "Should contain copyright text");
    }
}