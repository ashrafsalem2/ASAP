using ASAP.Platform.Kernel.Results;

namespace ASAP.Platform.Kernel.Setup;

/// <summary>
/// Reads and writes ASAP setup values, resolving each one from the narrowest scope that has
/// been set: user, then branch, then company, then tenant, then the declared default.
/// </summary>
public interface ISetupService
{
    /// <summary>
    /// Reads a setting for the current tenant, company, branch and user.
    /// </summary>
    /// <typeparam name="TValue">
    /// What to read the value as. Must match the declared <see cref="SetupValueType"/>.
    /// </typeparam>
    /// <param name="key">The setting key, for example <c>Inventory.Costing.AllowNegativeStock</c>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The resolved value, or the declared default when nobody has set one.</returns>
    /// <exception cref="KeyNotFoundException">
    /// The key was never declared by any module. Reading an undeclared setting is a bug, so it
    /// fails rather than quietly returning a default.
    /// </exception>
    /// <exception cref="InvalidCastException">
    /// <typeparamref name="TValue"/> does not match the declared value type.
    /// </exception>
    ValueTask<TValue> GetAsync<TValue>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a setting at an explicit scope, ignoring narrower overrides. Used by the setup
    /// screen, which has to show what this company actually set as distinct from what it inherits.
    /// </summary>
    /// <typeparam name="TValue">What to read the value as.</typeparam>
    /// <param name="key">The setting key.</param>
    /// <param name="scope">The scope to read at.</param>
    /// <param name="scopeId">
    /// Which company, branch or user to read for. Ignored for <see cref="SetupScope.Tenant"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The value set at that exact scope, or null when nothing is set there.</returns>
    ValueTask<TValue?> GetAtScopeAsync<TValue>(
        string key,
        SetupScope scope,
        Guid? scopeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a setting.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">
    /// The new value in its string form, or null to clear the override and fall back to the
    /// wider scope.
    /// </param>
    /// <param name="scope">Scope to set it at.</param>
    /// <param name="scopeId">Which company, branch or user. Ignored for tenant scope.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// A failure when the caller lacks the required permission, when the value does not fit the
    /// declared type or bounds, when the scope is narrower than the setting permits, or when the
    /// setting is locked because entries have already been posted against it.
    /// </returns>
    Task<Result> SetAsync(
        string key,
        string? value,
        SetupScope scope = SetupScope.Company,
        Guid? scopeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every declared setting, whether or not a value has been set. Powers the setup screen and
    /// the generated documentation.
    /// </summary>
    IReadOnlyCollection<SetupDescriptor> Declared { get; }

    /// <summary>Looks up one declaration.</summary>
    /// <param name="key">The setting key.</param>
    /// <returns>The declaration, or null when the key is unknown.</returns>
    SetupDescriptor? Describe(string key);
}
