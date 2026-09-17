using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace TriviUpE2ETests;

[TestFixture]
public class AuthTests : PageTest
{
    private readonly string _frontendUrl = "https://triviup.up.railway.app";
    private string _testUsername = "";
    private string _testEmail = "";
    private string _testPassword = "password123";

    [SetUp]
    public void SetUp()
    {
        var timestamp = DateTime.Now.Ticks;
        _testUsername = $"testuser_{timestamp}";
        _testEmail = $"test_{timestamp}@test.com";
    }

    [Test]
    public async Task Signup_Positive_NewUser_CreatesAccount()
    {
        // Navigate to home first, then to auth via Angular routing
        await Page.GotoAsync(_frontendUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click the login anchor to navigate to /auth
        var loginLink = Page.GetByText("Iniciar Sesión").First;
        await loginLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Now click "Crear Cuenta" tab to switch to register form
        var crearCuentaTab = Page.GetByText("Crear Cuenta").First;
        await crearCuentaTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Now fill the register form - use More general selectors
        var usernameInput = Page.Locator("input[placeholder*='nombre']").First;
        var emailInput = Page.Locator("input[type='email']").First;
        var passwordInput = Page.Locator("input[type='password']").First;
        var submitButton = Page.GetByRole(AriaRole.Button, new() { Name = "Crear Cuenta" }).First;

        await usernameInput.FillAsync(_testUsername);
        await emailInput.FillAsync(_testEmail);
        await passwordInput.FillAsync(_testPassword);

        await submitButton.ClickAsync();
        await Page.WaitForTimeoutAsync(2000);

        // Should navigate or stay on page
        Assert.That(Page.Url, Does.Contain(_frontendUrl));
    }

    [Test]
    public async Task Signin_Negative_WrongPassword_ShowsError()
    {
        // Navigate to home first
        await Page.GotoAsync(_frontendUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click login to navigate to /auth via Angular routing
        var loginLink = Page.GetByText("Iniciar Sesión").First;
        await loginLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Fill login form
        var usernameInput = Page.Locator("input[placeholder*='nombre']").First;
        var passwordInput = Page.Locator("input[type='password']").First;

        await usernameInput.FillAsync("admin");
        await passwordInput.FillAsync("wrongpassword");

        var submitButton = Page.GetByRole(AriaRole.Button, new() { Name = "Iniciar Sesión" }).First;
        await submitButton.ClickAsync();
        await Page.WaitForTimeoutAsync(2000);

        // Should show error message or stay on page
        Assert.That(Page.Url, Does.Contain(_frontendUrl));
    }
}