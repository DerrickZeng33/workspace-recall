using System.Security.AccessControl;
using System.Security.Principal;

namespace WorkspaceRecall.App.Services;

public static class PrivateDataDirectory
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkspaceRecall");

    public static void EnsureSecure(string path)
    {
        Directory.CreateDirectory(path);

        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows account has no SID.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddFullControl(security, currentUser);
        AddFullControl(
            security,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        AddFullControl(
            security,
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void AddFullControl(
        DirectorySecurity security,
        SecurityIdentifier identity)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }
}
