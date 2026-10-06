using System;
using System.Collections.Generic;
using System.Text;

using Android.App;
using Android.Content;
using Android.Content.PM;
using MoneySpend.Data;

namespace MoneySpend;

[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = GoogleDriveConstants.RedirectScheme)]
public class WebAuthenticationCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity { }