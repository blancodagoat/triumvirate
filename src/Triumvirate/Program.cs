namespace Triumvirate;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\Triumvirate.SingleInstance", out bool first);
        if (!first)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        var config = SuiteConfig.Load();
        var form = new MainForm(config);

        Icon trayIcon;
        try
        {
            trayIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        }
        catch
        {
            trayIcon = SystemIcons.Application;
        }

        using var tray = new NotifyIcon
        {
            Icon = trayIcon,
            Text = "Triumvirate",
            Visible = true,
        };

        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer() };
        menu.Items.Add("Open", null, (_, _) => ShowWindow(form));
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        tray.ContextMenuStrip = menu;
        tray.Click += (_, e) =>
        {
            if (e is MouseEventArgs { Button: MouseButtons.Left })
            {
                ShowWindow(form);
            }
        };

        // The suite starts what the user keeps enabled, then shows the window: this app
        // exists for people who WANT a window, so it opens visible rather than hiding.
        foreach (var tool in Tool.All)
        {
            if (config.Enabled.Contains(tool.Name) && tool.InstalledExe() is not null)
            {
                tool.Start();
            }
        }

        form.Show();
        Application.Run();
    }

    private static void ShowWindow(MainForm form)
    {
        form.Show();
        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }

        form.Activate();
    }
}
