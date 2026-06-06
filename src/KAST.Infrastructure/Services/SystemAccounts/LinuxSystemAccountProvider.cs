using System.Runtime.InteropServices;
using KAST.Core.Interfaces;
using KAST.Core.Models;

namespace KAST.Infrastructure.Services.SystemAccounts;

public sealed class LinuxSystemAccountProvider : ISystemAccountProvider
{
    private const string ProviderName = "Linux Local";
    private static readonly string[] PamServices = ["kast", "login"];

    public SystemAccountProviderStatus GetStatus()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? new(IsPamAvailable(), ProviderName, SupportsDomain: false, IsPamAvailable() ? null : "Linux PAM runtime is not available.")
            : new(false, ProviderName, SupportsDomain: false, "Linux account authentication requires Linux.");

    public Task<IReadOnlyList<SystemAccount>> SearchAccountsAsync(string? query, string? domain, CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Task.FromResult<IReadOnlyList<SystemAccount>>([]);

        return Task.Run(() =>
        {
            var normalizedQuery = query?.Trim();
            var accounts = ReadPasswd()
                .Where(account => string.IsNullOrWhiteSpace(normalizedQuery) ||
                                  account.Username.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                                  account.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .OrderBy(account => account.Username, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToArray();

            return (IReadOnlyList<SystemAccount>)accounts;
        }, ct);
    }

    public Task<SystemAccount?> ValidateCredentialsAsync(string username, string password, string? domain, CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            !string.IsNullOrWhiteSpace(domain) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            username.Contains('\\') ||
            username.Contains('@'))
        {
            return Task.FromResult<SystemAccount?>(null);
        }

        return Task.Run(() =>
        {
            var account = ReadPasswd().FirstOrDefault(a => string.Equals(a.Username, username.Trim(), StringComparison.Ordinal));
            if (account is null)
                return null;

            return ValidateWithPam(account.Username, password) ? account : null;
        }, ct);
    }

    private static IReadOnlyList<SystemAccount> ReadPasswd()
    {
        if (!File.Exists("/etc/passwd"))
            return [];

        var issuer = Environment.MachineName;
        var accounts = new List<SystemAccount>();
        foreach (var line in File.ReadLines("/etc/passwd"))
        {
            var parts = line.Split(':');
            if (parts.Length < 7 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[2]))
                continue;

            var username = parts[0];
            var uid = parts[2];
            var gecos = parts[4].Split(',', 2)[0];
            var displayName = string.IsNullOrWhiteSpace(gecos) ? username : gecos;
            accounts.Add(new SystemAccount(username, displayName, issuer, uid, ProviderName));
        }

        return accounts;
    }

    private static bool ValidateWithPam(string username, string password)
    {
        foreach (var service in PamServices)
        {
            var result = ValidateWithPamService(service, username, password);
            if (result == PamResult.Success)
                return true;

            if (result != PamResult.ServiceUnavailable)
                return false;
        }

        return false;
    }

    private static PamResult ValidateWithPamService(string service, string username, string password)
    {
        if (!IsPamAvailable())
            return PamResult.ServiceUnavailable;

        var passwordHandle = GCHandle.Alloc(password);
        var conversation = new PamConversation(ProvidePassword);
        var conv = new PamConv
        {
            Conversation = conversation,
            AppData = GCHandle.ToIntPtr(passwordHandle)
        };

        IntPtr pamHandle = IntPtr.Zero;
        var result = pam_start(service, username, ref conv, out pamHandle);
        if (result != PamSuccess)
        {
            passwordHandle.Free();
            return PamResult.ServiceUnavailable;
        }

        try
        {
            result = pam_authenticate(pamHandle, 0);
            if (result != PamSuccess)
                return PamResult.Failed;

            result = pam_acct_mgmt(pamHandle, 0);
            return result == PamSuccess ? PamResult.Success : PamResult.Failed;
        }
        finally
        {
            pam_end(pamHandle, result);
            passwordHandle.Free();
            GC.KeepAlive(conversation);
        }
    }

    private static bool IsPamAvailable()
        => NativeLibrary.TryLoad("libpam.so.0", out var handle) && FreeNativeLibrary(handle);

    private static bool FreeNativeLibrary(IntPtr handle)
    {
        NativeLibrary.Free(handle);
        return true;
    }

    private static int ProvidePassword(int messageCount, IntPtr messages, out IntPtr responses, IntPtr appData)
    {
        responses = IntPtr.Zero;
        if (messageCount <= 0)
            return PamConversationError;

        var responseSize = Marshal.SizeOf<PamResponse>();
        var responseArray = Marshal.AllocHGlobal(responseSize * messageCount);
        var allocatedResponses = new List<IntPtr>();

        try
        {
            for (var i = 0; i < messageCount; i++)
            {
                var messagePointer = Marshal.ReadIntPtr(messages, i * IntPtr.Size);
                var message = Marshal.PtrToStructure<PamMessage>(messagePointer);
                var response = new PamResponse();

                if (message.MessageStyle is PamPromptEchoOff or PamPromptEchoOn)
                {
                    var password = (string)GCHandle.FromIntPtr(appData).Target!;
                    response.Response = Marshal.StringToHGlobalAnsi(password);
                    allocatedResponses.Add(response.Response);
                }

                Marshal.StructureToPtr(response, responseArray + (i * responseSize), fDeleteOld: false);
            }

            responses = responseArray;
            return PamSuccess;
        }
        catch
        {
            foreach (var allocated in allocatedResponses)
                Marshal.FreeHGlobal(allocated);

            Marshal.FreeHGlobal(responseArray);
            responses = IntPtr.Zero;
            return PamConversationError;
        }
    }

    private enum PamResult
    {
        Success,
        Failed,
        ServiceUnavailable
    }

    private const int PamSuccess = 0;
    private const int PamAbort = 26;
    private const int PamConversationError = 19;
    private const int PamPromptEchoOff = 1;
    private const int PamPromptEchoOn = 2;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PamConversation(int messageCount, IntPtr messages, out IntPtr responses, IntPtr appData);

    [StructLayout(LayoutKind.Sequential)]
    private struct PamConv
    {
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public PamConversation Conversation;
        public IntPtr AppData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PamMessage
    {
        public int MessageStyle;
        public IntPtr Message;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PamResponse
    {
        public IntPtr Response;
        public int ResponseReturnCode;
    }

    [DllImport("libpam.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int pam_start(
        [MarshalAs(UnmanagedType.LPStr)] string serviceName,
        [MarshalAs(UnmanagedType.LPStr)] string user,
        ref PamConv conversation,
        out IntPtr pamHandle);

    [DllImport("libpam.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int pam_authenticate(IntPtr pamHandle, int flags);

    [DllImport("libpam.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int pam_acct_mgmt(IntPtr pamHandle, int flags);

    [DllImport("libpam.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int pam_end(IntPtr pamHandle, int status);
}
