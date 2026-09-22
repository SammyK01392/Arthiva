using Android.App;
using Android.Runtime;
using Arthiva.Services;

namespace Arthiva
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {

            AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) =>
            {
                CrashLogger.Log(args.Exception, "AndroidEnvironment.UnhandledExceptionRaiser");
                args.Handled = true; // let the write finish before the process dies
            };
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
