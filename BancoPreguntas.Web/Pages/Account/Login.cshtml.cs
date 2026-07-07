using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BancoPreguntas.Web.Pages.Account;

public class LoginModel(SignInManager<IdentityUser> signInManager) : PageModel
{
    public string ErrorMessage { get; private set; } = string.Empty;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string email, string password, string? returnUrl = null)
    {
        var result = await signInManager.PasswordSignInAsync(email, password, isPersistent: true, lockoutOnFailure: false);

        if (result.Succeeded)
            return LocalRedirect(returnUrl ?? "/admin");

        ErrorMessage = "Correo o contraseña incorrectos.";
        return Page();
    }
}
