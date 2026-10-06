namespace MoneySpend.Data;

public static class GoogleDriveConstants
{
    public const string AndroidClientId =
        "22988509444-u2v9eh3h1hrbptk6eft3pqpt23ali1g0.apps.googleusercontent.com";

    // Android intent-filter scheme
    public const string RedirectScheme =
        "in.moneyspend.app";

    public const string RedirectUri =
        RedirectScheme + ":/oauth2redirect";

    public const string Scopes =
        "openid email profile https://www.googleapis.com/auth/drive.appdata";

    public const string BackupFileName =
        "moneyspend_backup.msbk";
}