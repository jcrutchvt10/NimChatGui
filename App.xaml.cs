using System;
using System.Windows;

namespace NimChatGui
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Console.WriteLine($"Unhandled exception: {args.ExceptionObject}");
            };

            DispatcherUnhandledException += (s, args) =>
            {
                Console.WriteLine($"Dispatcher unhandled exception: {args.Exception}");
                args.Handled = true;
            };

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
    }
}