namespace OrderedClicker.Models;

public sealed record ProfileSaveOptions(
    bool CreateBackup = false,
    bool AllowOverwrite = false,
    bool RenewIdentity = false,
    string? ExpectedExistingSha256 = null);
