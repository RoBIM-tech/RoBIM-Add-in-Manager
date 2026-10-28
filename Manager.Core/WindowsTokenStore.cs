using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace RoBIM.Manager.Core;

public static class WindowsTokenStore
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist, AttributeCount;
        public nint Attributes;
        public string TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Write(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Read(string target, uint type, uint flags, out nint credential);
    [DllImport("advapi32.dll")] private static extern void CredFree(nint credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Delete(string target, uint type, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Enumerate(string filter, uint flags, out uint count, out nint credentials);
    public static IReadOnlyList<string> Accounts()
    {
        if (!Enumerate("RoBIM.Manager/GitHub/*", 0, out var count, out var list))
        {
            if (Marshal.GetLastWin32Error() == 1168) return [];
            throw new IOException("저장 계정 목록을 확인하지 못했습니다.");
        }
        try
        {
            var accounts = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var credential = Marshal.PtrToStructure<Credential>(Marshal.ReadIntPtr(list, i * IntPtr.Size));
                if (credential.Type == 1 && credential.TargetName.StartsWith("RoBIM.Manager/GitHub/", StringComparison.Ordinal) &&
                    System.Text.RegularExpressions.Regex.IsMatch(credential.UserName ?? "", "^[A-Za-z0-9-]{1,39}$")) accounts.Add(credential.UserName!);
            }
            return accounts.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        }
        finally { CredFree(list); }
    }
    public static void RemoveAllManagerTokens()
    {
        if (!Enumerate("RoBIM.Manager/GitHub/*", 0, out var count, out var list))
        {
            if (Marshal.GetLastWin32Error() == 1168) return;
            throw new IOException("Manager 전용 자격 증명 목록 확인 실패");
        }
        try
        {
            for (var i = 0; i < count; i++)
            {
                var credential = Marshal.PtrToStructure<Credential>(Marshal.ReadIntPtr(list, i * IntPtr.Size));
                if (credential.Type != 1 || !credential.TargetName.StartsWith("RoBIM.Manager/GitHub/", StringComparison.Ordinal)) continue;
                if (!Delete(credential.TargetName, 1, 0) && Marshal.GetLastWin32Error() != 1168)
                    throw new IOException("Manager 전용 자격 증명 삭제 실패");
            }
        }
        finally { CredFree(list); }
    }
    private static string Target(string login)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(login, "^[A-Za-z0-9-]{1,39}$")) throw new ArgumentException("GitHub 계정명 오류");
        return "RoBIM.Manager/GitHub/" + login.ToLowerInvariant();
    }
    public static void Save(string login, string token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2000 || token.Any(char.IsWhiteSpace)) throw new ArgumentException("토큰 형식 오류");
        var bytes = Encoding.Unicode.GetBytes(token);
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var credential = new Credential { Type = 1, TargetName = Target(login), Comment = "RoBIM GitHub authentication",
                UserName = login, CredentialBlobSize = (uint)bytes.Length, CredentialBlob = pointer, Persist = 2, TargetAlias = "" };
            if (!Write(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 자격 증명 저장 실패");
        }
        finally { Array.Clear(bytes); for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(pointer, i, 0); Marshal.FreeHGlobal(pointer); }
    }
    public static string Get(string login)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!Read(Target(login), 1, 0, out var pointer)) throw new IOException("저장된 토큰이 없습니다. GitHub 계정 화면에서 다시 연결하십시오.");
        try { var credential = Marshal.PtrToStructure<Credential>(pointer); return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2) ?? ""; }
        finally { CredFree(pointer); }
    }
    public static void Remove(string login)
    {
        if (!Delete(Target(login), 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new IOException("Windows 자격 증명 삭제 실패");
    }
}
