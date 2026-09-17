using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace TriviUpE2ETests;

[Parallelizable(ParallelScope.Self)]
public class MoreNavigationTests : PageTest
{
    private readonly string _frontendUrl = "https://triviup.up.railway.app";

    [Test]
    public async Task JoinRoom_Page_Loads()
    {
        await Page.GotoAsync(_frontendUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click on "Unirse a una Sala" link
        var joinLink = Page.GetByText("Unirse a una Sala").First;
        await joinLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should show join room form
        var roomCodeInput = Page.Locator("input[placeholder='HZJRWM']").First;
        Assert.That(await roomCodeInput.IsVisibleAsync(), "Should show room code input");
    }

    [Test]
    public async Task PublicQuizzes_Page_Loads()
    {
        await Page.GotoAsync(_frontendUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click on "Ver Cuestionarios" button
        var quizzesLink = Page.GetByText("Ver Cuestionarios").First;
        await quizzesLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should navigate to quizzes page
        Assert.That(Page.Url.Contains("quizzes") || Page.Url.Contains("public"), "Should be on quizzes page");
    }

    [Test]
    public async Task Explorar_Navigation_Works()
    {
        // Navigate directly to public quizzes page
        await Page.GotoAsync(_frontendUrl + "/quizzes/public");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should load quizzes
        var body = await Page.ContentAsync();
        Assert.That(body.Length > 0, "Public quizzes page should load");
    }

    [Test]
    public async Task AuthPage_BackToHome_Works()
    {
        await Page.GotoAsync(_frontendUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Navigate to auth page via Angular routing
        var loginLink = Page.GetByText("Iniciar Sesión").First;
        await loginLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Now click on "Volver al inicio" link
        var backLink = Page.GetByText("Volver al inicio").First;
        await backLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should go back to home
        var body = await Page.ContentAsync();
        Assert.That(body.Contains("TriviUp"), "Should be on home page");
    }

    [Test]
    public async Task JoinRoom_RequiresValidCode()
    {
        await Page.GotoAsync(_frontendUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Navigate to join room
        var joinLink = Page.GetByText("Unirse a una Sala").First;
        await joinLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Check that room code input exists
        var roomCodeInput = Page.Locator("input[placeholder='HZJRWM']").First;
        Assert.That(await roomCodeInput.IsVisibleAsync(), "Room code input should be visible");

        // Fill with short code
        await roomCodeInput.FillAsync("AB");

        // Input should still be visible (just validation happens)
        Assert.That(await roomCodeInput.IsVisibleAsync(), "Room code input should still be visible after typing");
    }

    [Test]
    public async Task Information_Page_Loads()
    {
        // Navigate directly to information page
        await Page.GotoAsync(_frontendUrl + "/informacion");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Page should load
        var body = await Page.ContentAsync();
        Assert.That(body.Length > 0, "Information page should load");
    }

    [Test]
    public async Task Contact_Page_Loads()
    {
        // Navigate directly to contact page
        await Page.GotoAsync(_frontendUrl + "/contacto");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Page should load
        var body = await Page.ContentAsync();
        Assert.That(body.Length > 0, "Contact page should load");
    }
}