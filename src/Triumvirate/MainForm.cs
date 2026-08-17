namespace Triumvirate;

/// <summary>
/// One window: sidebar navigation on the left, a page per entry on the right, all in
/// the family palette with the hand-painted controls from <see cref="Ui"/>. The Tools
/// page enables, disables, and updates; each tool page edits that tool's own
/// config.json and restarts it to apply. Closing hides to the tray.
///
/// Layout rule for the whole window: nothing is positioned by hand. Every page is a
/// docked <see cref="TableLayoutPanel"/> stack, so rows share one rhythm, editors share
/// one right-hand column, and a resize (or a scrollbar appearing) reflows instead of
/// overlapping. Sizes are authored at 96 DPI and scaled through <see cref="Ui.Dp"/>.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly SuiteConfig config;
    private readonly Dictionary<string, Label> statusLabels = [];
    private readonly Dictionary<string, Toggle> toggles = [];
    private readonly List<Control> pages = [];
    private readonly Panel content;
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 3000 };

    public MainForm(SuiteConfig config)
    {
        this.config = config;

        Text = "Triumvirate";
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui(9.5f);
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;

        // Sized so the longest settings page (DejaVu, thirteen rows) fits without a
        // scrollbar: WinForms scrollbars are stock light and there is no theming them.
        ClientSize = new Size(this.Dp(780), this.Dp(600));
        MinimumSize = new Size(this.Dp(700), this.Dp(520));

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        }
        catch
        {
            // Default icon; cosmetic only.
        }

        content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(this.Dp(24), this.Dp(22), this.Dp(24), this.Dp(22)),
            BackColor = Theme.Background,
        };

        pages.Add(BuildToolsPage());
        foreach (var tool in Tool.All)
        {
            pages.Add(BuildSettingsPage(tool));
        }

        var sidebar = new Sidebar(["Tools", .. Tool.All.Select(t => t.Name)]);
        sidebar.Selected += ShowPage;

        Controls.Add(content);
        Controls.Add(sidebar);
        ShowPage(0);

        refresh.Tick += (_, _) => RefreshStatus();
        refresh.Start();
        RefreshStatus();
    }

    private void ShowPage(int index)
    {
        content.Controls.Clear();
        content.Controls.Add(pages[index]);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    /// <summary>A page body: one column, docked to the top of a scrolling host so the
    /// stack keeps the host's width and a scrollbar (when one is genuinely needed) just
    /// narrows it instead of hiding content behind itself.</summary>
    private static TableLayoutPanel Stack() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Theme.Background,
        ColumnCount = 1,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        ColumnStyles = { new ColumnStyle(SizeType.Percent, 100f) },
    };

    /// <summary>The grid that lives inside a <see cref="Card"/>. The card's own padding
    /// keeps the grid's square corners tucked inside the rounded fill, which is cheaper
    /// and steadier than nesting transparent containers.</summary>
    private Card ContentCard(out TableLayoutPanel grid, int columns)
    {
        var card = new Card
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(this.Dp(8)),
        };

        grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Field,
            ColumnCount = columns,
            Margin = Padding.Empty,
            Padding = new Padding(this.Dp(10), this.Dp(6), this.Dp(10), this.Dp(6)),
        };

        card.Controls.Add(grid);
        return card;
    }

    /// <summary>Page title. Every page opens with one, so the content's top edge stays
    /// put as you move through the sidebar.</summary>
    private Label Heading(string text) => new()
    {
        Text = text,
        Font = Theme.Ui(13.5f, FontStyle.Bold),
        ForeColor = Theme.Text,
        AutoSize = true,
        UseMnemonic = false,
        Margin = new Padding(0, 0, 0, this.Dp(14)),
    };

    private Label Line(string text, float points, Color color, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text,
        Font = Theme.Ui(points, style),
        ForeColor = color,
        BackColor = Theme.Field,
        AutoSize = false,
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = Padding.Empty,
        UseMnemonic = false,
    };

    private Control BuildToolsPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };
        var stack = Stack();

        int gap = this.Dp(12);
        int cardHeight = (this.Dp(8) * 2) + (this.Dp(6) * 2) + this.Dp(26) + this.Dp(20)
            + this.Dp(22) + this.Dp(8) + this.Dp(32);
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.Controls.Add(Heading("Tools"), 0, 0);

        int row = 1;
        foreach (var tool in Tool.All)
        {
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, cardHeight + gap));
            stack.Controls.Add(BuildToolCard(tool, gap), 0, row++);
        }

        var updateAll = new Pill("Update everything") { Primary = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        updateAll.Click += async (_, _) =>
        {
            updateAll.Enabled = false;
            updateAll.Text = "Checking…";
            int updated = 0, current = 0, failed = 0;
            foreach (var tool in Tool.All)
            {
                var outcome = await tool.UpdateAsync();
                if (outcome.StartsWith("Updated") || outcome.StartsWith("Installed"))
                {
                    updated++;
                }
                else if (outcome.StartsWith("Up to date"))
                {
                    current++;
                }
                else
                {
                    failed++;
                }
            }

            RefreshStatus();
            updateAll.Text = failed > 0
                ? $"{updated} updated, {failed} failed"
                : updated > 0
                    ? $"Updated {updated}, {current} already current"
                    : "Everything is up to date";
            await Task.Delay(2500);
            updateAll.Text = "Update everything";
            updateAll.Enabled = true;
        };

        // Docked into the same single column as the cards, so its edges line up with
        // theirs at every width.
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, this.Dp(38)));
        stack.Controls.Add(updateAll, 0, row);

        page.Controls.Add(stack);
        return page;
    }

    private Card BuildToolCard(Tool tool, int gap)
    {
        var card = ContentCard(out var grid, columns: 2);
        card.Margin = new Padding(0, 0, 0, gap);

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowCount = 5;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, this.Dp(26)));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, this.Dp(20)));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, this.Dp(22)));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, this.Dp(8)));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, this.Dp(32)));

        var name = Line(tool.Name, 11f, Theme.Text, FontStyle.Bold);
        var blurb = Line(tool.Blurb, 9f, Theme.Dim);
        var status = Line("…", 9f, Theme.Dim);
        statusLabels[tool.Name] = status;

        var toggle = new Toggle
        {
            On = config.Enabled.Contains(tool.Name),
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
        };
        toggle.Toggled += async on => await ToggleAsync(tool, on);
        toggles[tool.Name] = toggle;

        // Wide enough that the outcome text ("Up to date (v1.2.10)") fits in the pill.
        var update = new Pill("Install / update", 210) { Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        update.Click += async (_, _) =>
        {
            update.Enabled = false;
            update.Text = "Checking…";
            update.Text = await tool.UpdateAsync();
            RefreshStatus();
            await Task.Delay(2500);
            update.Text = "Install / update";
            update.Enabled = true;
        };

        grid.Controls.Add(name, 0, 0);
        grid.Controls.Add(toggle, 1, 0);
        grid.Controls.Add(blurb, 0, 1);
        grid.Controls.Add(status, 0, 2);
        grid.Controls.Add(update, 0, 4);
        grid.SetColumnSpan(blurb, 2);
        grid.SetColumnSpan(status, 2);
        grid.SetColumnSpan(update, 2);
        return card;
    }

    private async Task ToggleAsync(Tool tool, bool on)
    {
        if (on)
        {
            config.Enabled.Add(tool.Name);
            config.Save();
            if (tool.InstalledExe() is null)
            {
                await tool.DownloadLatestAsync();
            }

            tool.Start();
        }
        else
        {
            config.Enabled.Remove(tool.Name);
            config.Save();
            await tool.StopAsync();
        }

        RefreshStatus();
    }

    private Control BuildSettingsPage(Tool tool)
    {
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };
        var stack = Stack();

        int rowHeight = this.Dp(32);
        int editorWidth = this.Dp(190);

        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.Controls.Add(Heading(tool.Name + " settings"), 0, 0);

        var card = ContentCard(out var grid, columns: 2);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, editorWidth));

        var editors = new List<(Setting Setting, Func<string> Value, string? Original)>();
        int row = 0;

        foreach (var setting in tool.Settings)
        {
            var current = SuiteConfig.GetToolValue(tool.ConfigPath, setting);
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));

            var label = Line(setting.Label, 9.5f, Theme.Text);
            label.Margin = new Padding(0, 0, this.Dp(12), 0);
            grid.Controls.Add(label, 0, row);

            Func<string> value;
            switch (setting.Kind)
            {
                case "bool":
                    // Right-anchored, so its right edge meets the combo and text-box edges.
                    var toggle = new Toggle
                    {
                        On = current is "true" or "True",
                        Anchor = AnchorStyles.Right,
                        Margin = Padding.Empty,
                    };
                    grid.Controls.Add(toggle, 1, row);
                    value = () => toggle.On.ToString();
                    break;

                case "choice":
                case "choiceInt":
                    var combo = new ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Theme.FieldHover,
                        ForeColor = Theme.Text,
                        Anchor = AnchorStyles.Left | AnchorStyles.Right,
                        Margin = Padding.Empty,
                    };
                    combo.Items.AddRange(setting.Choices!.Cast<object>().ToArray());
                    combo.SelectedItem = setting.Choices!.Contains(current) ? current : null;
                    grid.Controls.Add(combo, 1, row);
                    value = () => combo.SelectedItem?.ToString() ?? "";
                    break;

                default:
                    var text = new TextBox
                    {
                        Text = current ?? "",
                        BackColor = Theme.FieldHover,
                        ForeColor = Theme.Text,
                        BorderStyle = BorderStyle.FixedSingle,
                        Anchor = AnchorStyles.Left | AnchorStyles.Right,
                        Margin = Padding.Empty,
                    };
                    grid.Controls.Add(text, 1, row);
                    value = () => text.Text;
                    break;
            }

            editors.Add((setting, value, current));
            row++;
        }

        stack.RowStyles.Add(new RowStyle(
            SizeType.Absolute, (this.Dp(8) * 2) + (this.Dp(6) * 2) + (row * rowHeight)));
        stack.Controls.Add(card, 0, 1);

        var apply = new Pill("Apply", 120) { Primary = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        apply.Click += async (_, _) =>
        {
            apply.Enabled = false;
            bool changed = false;
            foreach (var (setting, value, original) in editors)
            {
                var raw = value();
                if (raw.Length > 0 && raw != original)
                {
                    SuiteConfig.SetToolValue(tool.ConfigPath, setting, raw);
                    changed = true;
                }
            }

            // The tools read config at startup, so applying means a restart. They come
            // back in under a second, and back at the privilege they were found at — a
            // tool restarted down from administrator keeps its hotkey dead in games.
            if (changed && tool.IsRunning())
            {
                tool.Start(await tool.StopAsync());
            }

            apply.Text = changed ? "Applied" : "No changes";
            await Task.Delay(1200);
            apply.Text = "Apply";
            apply.Enabled = true;
        };

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, this.Dp(16), 0, 0),
            Padding = Padding.Empty,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.AutoSize),
                new ColumnStyle(SizeType.Percent, 100f),
            },
        };
        actions.Controls.Add(apply, 0, 0);

        if (tool.RestartWarning is { } warning)
        {
            var note = new Label
            {
                Text = warning,
                ForeColor = Theme.Dim,
                BackColor = Theme.Background,
                Font = Theme.Ui(9f),
                AutoSize = false,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false,
                Margin = new Padding(this.Dp(14), 0, 0, 0),
            };
            actions.Controls.Add(note, 1, 0);
        }

        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, this.Dp(32) + this.Dp(16)));
        stack.Controls.Add(actions, 0, 2);

        page.Controls.Add(stack);
        return page;
    }

    private void RefreshStatus()
    {
        foreach (var tool in Tool.All)
        {
            if (!statusLabels.TryGetValue(tool.Name, out var label))
            {
                continue;
            }

            var version = tool.InstalledVersion();
            label.Text = version is null
                ? "Not installed. Flip the toggle to install and start it."
                : tool.IsRunning() ? $"Running · v{version}" : $"Stopped · v{version}";
            label.ForeColor = version is null
                ? Theme.Dim
                : tool.IsRunning() ? Theme.Accent : Theme.Dim;
        }
    }
}
