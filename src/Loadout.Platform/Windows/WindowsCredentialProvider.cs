using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Windows;

/// <summary>
/// Stores secrets in the Windows Credential Manager, the native provider on
/// this platform (spec section 54).
/// <para>
/// Credentials are written as generic credentials persisted to the local
/// machine rather than to the roaming store. Roaming them would push a
/// developer's API keys onto every machine they sign into, which is the
/// opposite of what section 52 asks for.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialProvider : ISecretProvider
{
    private const string TargetPrefix = "Loadout:";

    /// <inheritdoc />
    public string Name => "credential-manager";

    /// <inheritdoc />
    public Task<OperationResult> IsAvailableAsync(CancellationToken ct = default)
    {
        // The Credential Manager is part of Windows itself, so availability is
        // decided by the platform rather than by anything installable.
        return Task.FromResult(OperationResult.Ok());
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> GetAsync(string reference, CancellationToken ct = default)
    {
        var target = TargetPrefix + reference;
        var credentialPtr = nint.Zero;

        try
        {
            if (!NativeCredentials.CredRead(target, NativeCredentials.CredTypeGeneric, 0, out credentialPtr))
            {
                var error = Marshal.GetLastWin32Error();

                return Task.FromResult(error == NativeCredentials.ErrorNotFound
                    ? OperationResult<string>.Fail(
                        $"No stored credential for '{reference}'.", ExitCode.AuthenticationRequired)
                    : OperationResult<string>.Fail(
                        $"Could not read '{reference}' from the Credential Manager (error {error}).",
                        ExitCode.AuthenticationRequired));
            }

            var credential = Marshal.PtrToStructure<NativeCredentials.Credential>(credentialPtr);

            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == nint.Zero)
            {
                return Task.FromResult(OperationResult<string>.Ok(string.Empty));
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);

            return Task.FromResult(OperationResult<string>.Ok(Encoding.Unicode.GetString(bytes)));
        }
        finally
        {
            if (credentialPtr != nint.Zero)
            {
                NativeCredentials.CredFree(credentialPtr);
            }
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> SetAsync(string reference, string value, CancellationToken ct = default)
    {
        var target = TargetPrefix + reference;
        var blob = Encoding.Unicode.GetBytes(value);

        var targetPtr = nint.Zero;
        var userPtr = nint.Zero;
        var blobPtr = nint.Zero;

        try
        {
            targetPtr = Marshal.StringToCoTaskMemUni(target);
            userPtr = Marshal.StringToCoTaskMemUni(reference);
            blobPtr = Marshal.AllocCoTaskMem(blob.Length);
            Marshal.Copy(blob, 0, blobPtr, blob.Length);

            var credential = new NativeCredentials.Credential
            {
                Type = NativeCredentials.CredTypeGeneric,
                TargetName = targetPtr,
                UserName = userPtr,
                CredentialBlob = blobPtr,
                CredentialBlobSize = (uint)blob.Length,
                Persist = NativeCredentials.CredPersistLocalMachine,
            };

            if (!NativeCredentials.CredWrite(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                return Task.FromResult(OperationResult.Fail(
                    $"Could not store '{reference}' in the Credential Manager (error {error})."));
            }

            return Task.FromResult(OperationResult.Ok());
        }
        finally
        {
            // The plaintext is zeroed before the buffer is released so it does
            // not linger in freed unmanaged memory.
            if (blobPtr != nint.Zero)
            {
                for (var i = 0; i < blob.Length; i++)
                {
                    Marshal.WriteByte(blobPtr, i, 0);
                }

                Marshal.FreeCoTaskMem(blobPtr);
            }

            Array.Clear(blob);

            if (targetPtr != nint.Zero)
            {
                Marshal.FreeCoTaskMem(targetPtr);
            }

            if (userPtr != nint.Zero)
            {
                Marshal.FreeCoTaskMem(userPtr);
            }
        }
    }

    /// <inheritdoc />
    public Task<OperationResult> RemoveAsync(string reference, CancellationToken ct = default)
    {
        var target = TargetPrefix + reference;

        if (NativeCredentials.CredDelete(target, NativeCredentials.CredTypeGeneric, 0))
        {
            return Task.FromResult(OperationResult.Ok());
        }

        var error = Marshal.GetLastWin32Error();

        return Task.FromResult(error == NativeCredentials.ErrorNotFound
            ? OperationResult.Fail($"No stored credential for '{reference}'.")
            : OperationResult.Fail(
                $"Could not remove '{reference}' from the Credential Manager (error {error})."));
    }

    /// <inheritdoc />
    public async Task<OperationResult> TestAsync(string reference, CancellationToken ct = default)
    {
        var result = await GetAsync(reference, ct).ConfigureAwait(false);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error ?? "Unresolved.", ExitCode.AuthenticationRequired);
    }
}
