# Blazor SSR Forms — Patterns and Gotchas

Guidance for building forms in Blazor Static Server-Side Rendering (SSR) mode. Static SSR is the default rendering mode in .NET 8/9 Blazor. These patterns apply to any page that does **not** use `@rendermode InteractiveServer`.

---

## Gotcha: Conditional `<EditForm>` Causes "No Form Found" Error

### Symptom

Submitting a form raises:

```
Cannot submit the form 'X' because no form on the page currently has that name.
```

### Cause

Blazor SSR requires a form to exist in the render tree **at the moment the POST is processed**. When you use `@if` / `else` to toggle between two separate `<EditForm>` components, the server handles the POST by creating a **fresh component instance** with **default state**. The condition therefore evaluates the same way it did on the very first render — not the way it was when the user clicked submit — and the expected form is absent from the tree.

This is a **known framework limitation**, confirmed by the ASP.NET Core team as "by design."

References:
- [dotnet/aspnetcore#55808](https://github.com/dotnet/aspnetcore/issues/55808)
- [dotnet/aspnetcore#54854](https://github.com/dotnet/aspnetcore/issues/54854)
- [dotnet/aspnetcore#52360](https://github.com/dotnet/aspnetcore/issues/52360)

### Where We Hit This

The `/Account/Register` page had a two-step invite-code wizard:

- Step 1 — validate invite code (`FormName="validate-code"`)
- Step 2 — fill in registration details (`FormName="register"`)

Using `@if (!_codeValidated)` / `else` to switch between two separate `<EditForm>` components triggered the error on step 2 submission.

---

## Solution: Single `<EditForm>` with Conditional Content

Keep **one** `<EditForm>` permanently in the render tree. Toggle the *content inside* the form, not the form wrapper itself. Use a hidden `Step` field to carry the current step across the POST round-trip.

```razor
<EditForm Model="Input" method="post" OnValidSubmit="OnSubmit" FormName="register">
    <DataAnnotationsValidator />

    {{!-- Hidden field preserves step across the POST round-trip --}}
    <input type="hidden" name="Input.Step" value="@Input.Step" />

    @if (Input.Step == 1)
    {
        <FormInput @bind-Value="Input.InviteCode" Label="Invite Code" />
        <Button type="submit">Verify Code</Button>
    }
    else
    {
        <FormInput @bind-Value="Input.Email" Label="Email" />
        <FormInput @bind-Value="Input.Password" Label="Password" InputType="password" />
        <Button type="submit">Create Account</Button>
    }
</EditForm>
```

Route in the handler based on `Input.Step`:

```csharp
[SupplyParameterFromForm]
private InputModel Input { get; set; } = new();

public async Task<IActionResult> OnSubmit(EditContext editContext)
{
    if (Input.Step == 1)
        return await HandleStep1Async();

    return await HandleStep2Async();
}
```

The model carries the step:

```csharp
public class InputModel
{
    public int Step { get; set; } = 1;

    // Step 1
    public string InviteCode { get; set; } = string.Empty;

    // Step 2
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
```

---

## Official Workarounds from the ASP.NET Core Team

Three patterns are recognized. Choose based on complexity and accessibility requirements.

| Approach | When to Use |
|----------|-------------|
| **Single form with conditional content** (recommended) | Multi-step wizards, toggling between form modes |
| **CSS hiding** | Simple show/hide with no logic branching |
| **Structural rearrangement** | When form wrappers must stay separate for other reasons |

### Option 1 — Single Form with Conditional Content (Recommended)

Described above. One `<EditForm>`, always rendered. Hidden `Step` field drives branching.

### Option 2 — CSS Hiding

Render all forms simultaneously, hide inactive ones with `display:none`. Both forms are always in the DOM and the render tree.

```razor
<EditForm Model="Step1Input" method="post" OnValidSubmit="OnStep1Submit" FormName="step1"
          style="@(CurrentStep == 1 ? "" : "display:none")">
    ...
</EditForm>

<EditForm Model="Step2Input" method="post" OnValidSubmit="OnStep2Submit" FormName="step2"
          style="@(CurrentStep == 2 ? "" : "display:none")">
    ...
</EditForm>
```

Drawback: hidden form fields are still submitted if the browser serializes the wrong form. Use only when forms have distinct `FormName` values and the payload is unambiguous.

### Option 3 — Structural Rearrangement

Move the `<EditForm>` tags outside the conditional block; put only the field content inside. Works when the surrounding markup allows it.

```razor
<EditForm Model="Input" method="post" OnValidSubmit="OnSubmit" FormName="wizard">
    @if (Input.Step == 1)
    {
        <!-- Step 1 content -->
    }
    @if (Input.Step == 2)
    {
        <!-- Step 2 content -->
    }
</EditForm>
```

This is functionally equivalent to Option 1; the distinction is stylistic.

---

## General Blazor SSR Form Rules

- Every interactive form handler must use `[SupplyParameterFromForm]` on the bound model property.
- `FormName` must be unique per page if multiple `<EditForm>` components are rendered simultaneously.
- State is **not** preserved between requests in SSR mode — any field not submitted as a form value is lost. Use hidden fields or the `Step` pattern to thread state across POST round-trips.
- `@rendermode InteractiveServer` bypasses all of these constraints, but adds a SignalR circuit. Prefer SSR for auth flows and public-facing forms.
