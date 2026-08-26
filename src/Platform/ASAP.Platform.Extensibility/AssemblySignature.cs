using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ASAP.Platform.Extensibility;

/// <summary>
/// Checks that an extension assembly carries an Authenticode signature.
/// </summary>
/// <remarks>
/// <para>
/// This answers one question only: is the file signed, and does the signing certificate chain to
/// a trusted root and remain valid. It does not decide <em>whose</em> signature is acceptable.
/// Publisher allow-listing is a separate decision, and one a customer makes about their own
/// installation rather than one ASAP makes for them.
/// </para>
/// <para>
/// Authenticode is a Windows facility. On another platform the check cannot be performed, and it
/// reports that plainly instead of silently passing -- a security check that quietly succeeds
/// when it cannot run is worse than no check at all, because it is trusted.
/// </para>
/// </remarks>
public static class AssemblySignature
{
    /// <summary>
    /// Whether an assembly carries a valid Authenticode signature.
    /// </summary>
    /// <param name="assemblyPath">Full path to the assembly.</param>
    /// <param name="problem">Why the check failed, when it did.</param>
    public static bool IsSigned(string assemblyPath, [NotNullWhen(false)] out string? problem)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            problem = "Authenticode signatures can only be verified on Windows. "
                    + "Turn off Asap:Extensions:RequireSignedAssemblies to load extensions on this platform, "
                    + "and restrict write access to the extension folder instead.";
            return false;
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(assemblyPath);
            using var chain = new X509Chain
            {
                ChainPolicy =
                {
                    RevocationMode = X509RevocationMode.Online,

                    // A revocation server being unreachable must not fail the check outright.
                    // A branch server behind a restrictive firewall is a normal deployment, and
                    // refusing every extension there would push the customer to disable signing
                    // altogether -- a worse outcome than accepting an unknown revocation status.
                    RevocationFlag = X509RevocationFlag.ExcludeRoot,
                    VerificationFlags = X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown
                                        | X509VerificationFlags.IgnoreEndRevocationUnknown,
                },
            };

            if (chain.Build(certificate))
            {
                problem = null;
                return true;
            }

            var reasons = chain.ChainStatus
                .Where(static s => s.Status != X509ChainStatusFlags.NoError)
                .Select(static s => s.StatusInformation.Trim())
                .Distinct();

            problem = $"its signing certificate did not validate: {string.Join("; ", reasons)}";
            return false;
        }
        catch (CryptographicException)
        {
            // Thrown when the file carries no signature at all, which is the ordinary case for
            // an unsigned build rather than anything exceptional.
            problem = "it carries no Authenticode signature";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"it could not be read: {ex.Message}";
            return false;
        }
    }
}
