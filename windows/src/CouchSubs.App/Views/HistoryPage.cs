// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CouchSubs.App.Theme;
using CouchSubs.Core;
using CouchSubs.Shell;
using Microsoft.Win32;

namespace CouchSubs.App.Views;

/// <summary>
/// Geçmiş ve sözlük sekmesi (taslak: design/cevirgen/arayuz-taslagi/History.dc.html). Solda seçili oturum altyazı dosyası gibi:
/// başlıkta tarih/saat, oyun, süre, satır ve düzeltme sayısı; arama (kelime ya da zaman), "Türkçe .srt kaydet" ve "İki dilli
/// .srt"; ipuçları sıra numarası, zaman aralığı, soluk İngilizce, Türkçe, okuma hızı uyarısı ve kaynağıyla. "Düzelt" satırın
/// içinde açılır (eski Türkçe üstü çizili, yeni metin, yazarken karakter ve okuma hızı; Kaydet → kullanıcı düzeltmesi).
/// Düzeltilen satır "Senin düzeltmen" ve "Kaldır". Önceki oturumlar seçilebilir. Sağda kullanıcı sözlüğü: İngilizce, Türkçe,
/// kural ("çevirme" / "hep böyle"); satıra tıklayınca düzenlenir, × siler, alttaki satırla eklenir.
/// </summary>
public sealed class HistoryPage : UserControl
{
    /// <summary>Ekran görüntüsü aracı: satır içi düzeltmeyi bu satırda bu metinle açık gösterir.</summary>
    internal static (int EntryId, string Text)? PreviewEdit { get; set; }

    private readonly AppController controller;
    private readonly TextBlock title = ScreenKit.Title("Geçmiş");
    private readonly TextBlock meta = ScreenKit.Small("", 13);
    private readonly ComboBox sessionChoice = new() { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly System.Windows.Controls.TextBox search = new() { Width = 200, MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly StackPanel cues = new();
    private readonly TextBlock status = ScreenKit.Small("", 12);
    private readonly Button turkishSrt;
    private readonly Button bilingualSrt;
    private readonly StackPanel terms = new();
    private readonly System.Windows.Controls.TextBox termEnglish = new() { MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly System.Windows.Controls.TextBox termTurkish = new() { MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button termSave;
    private readonly TextBlock termError = ScreenKit.Small("", 12, "liveTextBrush");
    private IReadOnlyList<HistorySession> sessions = [];
    private int sessionIndex;
    /// <summary>Satır içi düzeltmesi açık olan kayıt.</summary>
    private int? editing;
    private string editText = "";
    /// <summary>Sözlükte düzenlenen terim (yeni terimse null).</summary>
    private string? editingTerm;

    public HistoryPage(AppController controller)
    {
        this.controller = controller;
        DataContext = controller;
        controller.NoteScreen("history");
        turkishSrt = ScreenKit.Button("Türkçe .srt kaydet", "OutlineButton", (_, _) => Export(SrtExport.Kind.Turkish));
        bilingualSrt = ScreenKit.Button("İki dilli .srt", "OutlineButton", (_, _) => Export(SrtExport.Kind.Bilingual));
        termSave = ScreenKit.Button("Ekle", "PrimaryButton", (_, _) => SaveTerm());
        termSave.MinHeight = 36;
        termSave.Padding = Sp.T("m", "0", "m", "0");
        termSave.FontSize = 13;
        if (PreviewEdit is { } preview) (editing, editText) = (preview.EntryId, preview.Text);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 560 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
        grid.Children.Add(BuildSession());
        var rule = ScreenKit.Rule(vertical: true);
        Grid.SetColumn(rule, 1);
        grid.Children.Add(rule);
        var aside = BuildGlossary();
        Grid.SetColumn(aside, 2);
        grid.Children.Add(aside);
        Content = grid;

        controller.PropertyChanged += OnChanged;
        Unloaded += (_, _) => controller.PropertyChanged -= OnChanged;
        Loaded += (_, _) =>
        {
            controller.PropertyChanged -= OnChanged;
            controller.PropertyChanged += OnChanged;
        };
        LoadSessions();
        ShowTerms();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // Düzeltme yazılırken gelen yeni satır kutuyu kapatmasın: liste düzeltme bitince yenilenir.
            case nameof(AppController.History) or nameof(AppController.SessionStartedAt) when editing is null:
                Dispatcher.BeginInvoke(LoadSessions);
                break;
            case nameof(AppController.Corrections) when editing is null:
                Dispatcher.BeginInvoke(ShowCues);
                break;
            case nameof(AppController.GlossaryTerms):
                Dispatcher.BeginInvoke(ShowTerms);
                break;
        }
    }

    // ───── Oturum ─────

    private FrameworkElement BuildSession()
    {
        var root = new DockPanel { Margin = Sp.T("edge", "l", "edge", "0"), LastChildFill = true };
        var head = new StackPanel();
        DockPanel.SetDock(head, Dock.Top);
        head.Children.Add(title);
        meta.Margin = Sp.T("0", "xxs", "0", "0");
        head.Children.Add(meta);

        var bar = new DockPanel { Margin = Sp.T("0", "m", "0", "s"), LastChildFill = false };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        bilingualSrt.Margin = Sp.T("xs", "0", "0", "0");
        buttons.Children.Add(turkishSrt);
        buttons.Children.Add(bilingualSrt);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        var find = new StackPanel { Orientation = Orientation.Horizontal };
        var findLabel = ScreenKit.Label("Ara");
        findLabel.VerticalAlignment = VerticalAlignment.Center;
        findLabel.Margin = Sp.T("0", "0", "xs", "0");
        find.Children.Add(findLabel);
        AutomationProperties.SetName(search, "Ara: kelime ya da zaman");
        search.TextChanged += (_, _) => ShowCues();
        find.Children.Add(ScreenKit.Field(search, "kelime ya da zaman"));
        sessionChoice.Margin = Sp.T("s", "0", "0", "0");
        AutomationProperties.SetName(sessionChoice, "Oturum");
        sessionChoice.SelectionChanged += (_, _) =>
        {
            if (sessionChoice.SelectedIndex < 0 || sessionChoice.SelectedIndex == sessionIndex) return;
            sessionIndex = sessionChoice.SelectedIndex;
            editing = null;
            ShowSession();
        };
        find.Children.Add(sessionChoice);
        DockPanel.SetDock(find, Dock.Left);
        bar.Children.Add(find);
        head.Children.Add(bar);
        status.TextWrapping = TextWrapping.Wrap;
        status.Visibility = Visibility.Collapsed;
        status.Margin = Sp.T("0", "0", "0", "s");
        head.Children.Add(status);
        root.Children.Add(head);

        var list = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
            Content = cues, Padding = Sp.T("0", "0", "0", "l"),
        };
        AutomationProperties.SetName(cues, "Oturumun satırları");
        root.Children.Add(list);
        return root;
    }

    private void LoadSessions()
    {
        var previous = sessions.ElementAtOrDefault(sessionIndex)?.Origin;
        sessions = HistoryModel.Sessions(controller.History, controller.SessionStartedAt);
        // Seçili oturum korunur (yeni satır geldiğinde en yenisine atlamaz); yoksa en yeni oturum.
        var keep = previous is { } origin ? sessions.ToList().FindIndex(session => session.Origin == origin) : -1;
        sessionIndex = keep >= 0 ? keep : 0;
        sessionChoice.Items.Clear();
        var now = DateTime.UtcNow;
        foreach (var session in sessions)
            sessionChoice.Items.Add(new ComboBoxItem { Content = $"{HistoryModel.Title(session.Origin, now)} · {session.Entries.Count} satır" });
        sessionChoice.SelectedIndex = sessions.Count == 0 ? -1 : sessionIndex;
        sessionChoice.Visibility = sessions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ShowSession();
    }

    private HistorySession? Session => sessions.ElementAtOrDefault(sessionIndex);

    /// <summary>Bu çalışmadaki son çeviri oturumu (oyunun adı yalnız onun için bilinir).</summary>
    private bool IsCurrent(HistorySession session) => controller.SessionStartedAt is { } start && session.Origin == start.ToUniversalTime();

    private void ShowSession()
    {
        if (Session is not { } session)
        {
            title.Text = "Geçmiş";
            meta.Text = "Henüz satır yok";
        }
        else
        {
            title.Text = HistoryModel.Title(session.Origin, DateTime.UtcNow);
            meta.Text = HistoryModel.Meta(session, IsCurrent(session) ? controller.PlayedGame : null);
        }
        turkishSrt.IsEnabled = bilingualSrt.IsEnabled = Session is { Entries.Count: > 0 };
        ShowCues();
    }

    private void ShowCues()
    {
        cues.Children.Clear();
        if (Session is not { } session)
        {
            var empty = ScreenKit.Muted("Oynarken çevrilen satırlar burada birikir; yanlış çeviriyi buradan düzeltebilir, oturumu altyazı dosyası olarak kaydedebilirsin.", 15);
            empty.Margin = Sp.T("0", "m", "0", "0");
            empty.MaxWidth = 560;
            empty.HorizontalAlignment = HorizontalAlignment.Left;
            cues.Children.Add(empty);
            return;
        }
        var engine = LiveTranscript.EngineLabel(controller.Settings.Engine);
        var query = search.Text;
        var shown = 0;
        foreach (var cue in HistoryModel.Cues(session, engine))
        {
            var correction = controller.CorrectionFor(cue.Entry.Source);
            if (!HistoryModel.Matches(cue, query, correction?.Turkish) && editing != cue.Entry.Id) continue;
            cues.Children.Add(editing == cue.Entry.Id ? EditRow(cue, correction) : CueRow(cue, correction));
            shown++;
        }
        if (shown == 0)
        {
            var none = ScreenKit.Muted("Bu aramaya uyan satır yok.", 14);
            none.Margin = Sp.T("0", "m", "0", "0");
            cues.Children.Add(none);
        }
    }

    /// <summary>Bir ipucu: numara, zaman aralığı · kaynak, İngilizce, Türkçe (düzeltme varsa o), hız uyarısı; sağda Düzelt.</summary>
    private Border CueRow(HistoryCue cue, UserCorrections.Correction? correction)
    {
        var grid = Columns();
        var number = ScreenKit.Mono(cue.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), 12);
        grid.Children.Add(number);
        var body = new StackPanel();
        body.Children.Add(ScreenKit.Mono($"{cue.Range} · {cue.Source}", 12));
        var english = ScreenKit.Muted(cue.Entry.Source, 13);
        english.LineHeight = 19;
        english.Margin = Sp.T("0", "xxs", "0", "0");
        body.Children.Add(english);
        var turkish = ScreenKit.Text(correction?.Turkish ?? cue.Entry.Translation, 16);
        turkish.Margin = Sp.T("0", "line", "0", "0");
        body.Children.Add(turkish);
        if (correction is null && HistoryModel.SpeedWarning(cue) is { } warning)
        {
            var fast = ScreenKit.Small(warning, 12, "warnBrush");
            fast.Margin = Sp.T("0", "xxs", "0", "0");
            body.Children.Add(fast);
        }
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        FrameworkElement action;
        if (correction is not null)
        {
            var side = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            side.Children.Add(ScreenKit.Label("Senin düzeltmen", "textBrush"));
            var remove = ScreenKit.Button("Kaldır", "TextLink", (_, _) => controller.RemoveCorrection(cue.Entry.Source), "Düzeltmeyi kaldır");
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            remove.SetResourceReference(ForegroundProperty, "mutedBrush");
            side.Children.Add(remove);
            var change = ScreenKit.Button("Değiştir", "TextLink", (_, _) => Open(cue, correction.Turkish), "Düzeltmeyi değiştir");
            change.HorizontalAlignment = HorizontalAlignment.Right;
            change.SetResourceReference(ForegroundProperty, "mutedBrush");
            side.Children.Add(change);
            action = side;
        }
        else
        {
            var fix = ScreenKit.Button("Düzelt", "OutlineButton", (_, _) => Open(cue, cue.Entry.Translation), $"{cue.Number}. satırı düzelt");
            fix.VerticalAlignment = VerticalAlignment.Top;
            fix.HorizontalAlignment = HorizontalAlignment.Right;
            action = fix;
        }
        Grid.SetColumn(action, 2);
        grid.Children.Add(action);
        return ScreenKit.Row(grid, "0,item");
    }

    private static Grid Columns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 96 });
        return grid;
    }

    private void Open(HistoryCue cue, string text)
    {
        editing = cue.Entry.Id;
        editText = text;
        ShowCues();
    }

    /// <summary>
    /// Satır içi düzeltme: eski Türkçe üstü çizili, "Senin çevirin · bu replik hep böyle çıkar", metin kutusu (sarı çerçeve),
    /// altında karakter ve okuma hızı (satırın ekranda kaldığı süreye göre); sağda Kaydet ve Vazgeç. Enter kaydeder, Esc vazgeçer.
    /// </summary>
    private Border EditRow(HistoryCue cue, UserCorrections.Correction? correction)
    {
        var grid = Columns();
        var number = ScreenKit.Mono(cue.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), 12, "amberTextBrush");
        grid.Children.Add(number);
        var body = new StackPanel();
        body.Children.Add(ScreenKit.Mono($"{cue.Range} · {cue.Source}", 12));
        var english = ScreenKit.Muted(cue.Entry.Source, 13);
        english.Margin = Sp.T("0", "xs", "0", "0");
        body.Children.Add(english);
        var old = ScreenKit.Muted(correction?.Turkish ?? cue.Entry.Translation, 14);
        old.TextDecorations = TextDecorations.Strikethrough;
        old.Margin = Sp.T("0", "xs", "0", "0");
        body.Children.Add(old);
        var label = ScreenKit.Label("Senin çevirin · bu replik hep böyle çıkar", "amberTextBrush");
        label.Margin = Sp.T("0", "xs", "0", "0");
        body.Children.Add(label);
        var box = new System.Windows.Controls.TextBox
        {
            Text = editText, FontSize = 16, MinHeight = 42, VerticalContentAlignment = VerticalAlignment.Center, Margin = Sp.T("0", "xs", "0", "0"),
            BorderThickness = new Thickness(1), Padding = Sp.T("s", "0", "s", "0"),
        };
        box.SetResourceReference(BorderBrushProperty, "amberBrush");
        AutomationProperties.SetName(box, "Senin çevirin");
        body.Children.Add(box);
        var metrics = ScreenKit.Mono("", 12);
        metrics.Margin = Sp.T("0", "xs", "0", "0");
        AutomationProperties.SetLiveSetting(metrics, AutomationLiveSetting.Polite);
        void Measure()
        {
            editText = box.Text;
            metrics.Text = HistoryModel.EditMetrics(box.Text, cue.DurationMs);
            metrics.SetResourceReference(TextBlock.ForegroundProperty, HistoryModel.EditTooFast(box.Text, cue.DurationMs) ? "warnBrush" : "mutedBrush");
        }
        box.TextChanged += (_, _) => Measure();
        Measure();
        body.Children.Add(metrics);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var side = new StackPanel { Margin = Sp.T("m", "0", "0", "0"), VerticalAlignment = VerticalAlignment.Top };
        var save = ScreenKit.Button("Kaydet", "PrimaryButton", (_, _) => Save(cue, box.Text));
        save.MinHeight = 36;
        save.FontSize = 13;
        save.Padding = Sp.T("m", "0", "m", "0");
        save.HorizontalAlignment = HorizontalAlignment.Stretch;
        var cancel = ScreenKit.Button("Vazgeç", "OutlineButton", (_, _) => Close());
        cancel.HorizontalAlignment = HorizontalAlignment.Stretch;
        cancel.Margin = Sp.T("0", "xs", "0", "0");
        side.Children.Add(save);
        side.Children.Add(cancel);
        Grid.SetColumn(side, 2);
        grid.Children.Add(side);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Save(cue, box.Text);
            else if (e.Key == Key.Escape) Close();
        };
        box.Loaded += (_, _) =>
        {
            if (PreviewEdit is not null) return;
            box.Focus();
            box.CaretIndex = box.Text.Length;
        };

        // Düzenlenen satır taslaktaki gibi yanlara taşan zeminle: numara ve metinler diğer satırlarla hizalı kalır.
        var row = ScreenKit.Row(grid, "s,item");
        row.Margin = Sp.T("-s", "0", "-s", "0");
        row.SetResourceReference(Border.BackgroundProperty, "surfaceBrush");
        return row;
    }

    private void Save(HistoryCue cue, string text)
    {
        if (text.Trim().Length == 0) return;
        controller.SaveCorrection(cue.Entry.Source, text.Trim());
        Close();
    }

    private void Close()
    {
        editing = null;
        editText = "";
        LoadSessions();
    }

    /// <summary>Seçili oturum SRT olarak kaydedilir (Türkçe ya da iki dilli); sonuç sayfada yazılır.</summary>
    private void Export(SrtExport.Kind kind)
    {
        if (Session is not { } session) return;
        var game = IsCurrent(session) ? controller.PlayedGame : null;
        var name = string.Join("-", new[] { "Cevirgen", game, session.Origin.ToLocalTime().ToString("yyyy-MM-dd-HHmm", System.Globalization.CultureInfo.InvariantCulture) }
            .Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => string.Concat(part!.Split(Path.GetInvalidFileNameChars())).Replace(' ', '-')));
        var picker = new SaveFileDialog
        {
            FileName = name + (kind == SrtExport.Kind.Bilingual ? "-iki-dilli" : "") + ".srt",
            Filter = "Altyazı dosyası (*.srt)|*.srt",
            Title = kind == SrtExport.Kind.Bilingual ? "İki dilli .srt" : "Türkçe .srt kaydet",
        };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var count = controller.ExportSrt(picker.FileName, kind, session);
            ShowStatus($"{count} satır kaydedildi: {picker.FileName}", error: false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Kaydedilemedi: " + error.Message, error: true);
        }
    }

    private void ShowStatus(string text, bool error)
    {
        status.Text = text;
        status.SetResourceReference(TextBlock.ForegroundProperty, error ? "liveTextBrush" : "successBrush");
        status.Visibility = Visibility.Visible;
    }

    // ───── Sözlük ─────

    private FrameworkElement BuildGlossary()
    {
        var stack = new StackPanel { Margin = Sp.T("edge", "l", "edge", "l") };
        stack.Children.Add(ScreenKit.Title("Sözlük", 24));
        var lead = ScreenKit.Muted("Adlar ve terimler her oyunda aynı kalsın. Çeviri motoru bu kurallara uyar.", 14);
        lead.Margin = Sp.T("0", "xxs", "0", "m");
        stack.Children.Add(lead);
        var head = TermGrid();
        head.Children.Add(ScreenKit.Label("İngilizce"));
        var tr = ScreenKit.Label("Türkçe");
        Grid.SetColumn(tr, 1);
        head.Children.Add(tr);
        var rule = ScreenKit.Label("Kural");
        Grid.SetColumn(rule, 2);
        head.Children.Add(rule);
        head.Margin = Sp.T("0", "0", "0", "xs");
        stack.Children.Add(head);
        AutomationProperties.SetName(terms, "Sözlük terimleri");
        stack.Children.Add(terms);

        var add = TermGrid();
        AutomationProperties.SetName(termEnglish, "İngilizce terim");
        AutomationProperties.SetName(termTurkish, "Türkçe karşılık");
        termEnglish.KeyDown += OnTermKey;
        termTurkish.KeyDown += OnTermKey;
        var english = ScreenKit.Field(termEnglish, "terim");
        english.Margin = Sp.T("0", "0", "xs", "0");
        add.Children.Add(english);
        var turkish = ScreenKit.Field(termTurkish, "karşılık");
        turkish.Margin = Sp.T("0", "0", "xs", "0");
        Grid.SetColumn(turkish, 1);
        add.Children.Add(turkish);
        Grid.SetColumn(termSave, 2);
        Grid.SetColumnSpan(termSave, 2);
        termSave.HorizontalAlignment = HorizontalAlignment.Stretch;
        add.Children.Add(termSave);
        stack.Children.Add(ScreenKit.Row(add, "0,s"));
        termError.TextWrapping = TextWrapping.Wrap;
        termError.Visibility = Visibility.Collapsed;
        stack.Children.Add(termError);
        var hint = ScreenKit.Muted("Karşılığı boş bırakırsan terim çevrilmez (kural: çevirme); yazarsan hep o karşılık kullanılır (hep böyle). Düzenlemek için satıra tıkla.", 13);
        hint.Margin = Sp.T("0", "xs", "0", "0");
        stack.Children.Add(hint);
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = stack,
        };
    }

    private static Grid TermGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        return grid;
    }

    private void ShowTerms()
    {
        terms.Children.Clear();
        foreach (var term in controller.GlossaryTerms)
        {
            var grid = TermGrid();
            var english = ScreenKit.Text(term.English, 14, wrap: false);
            english.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(english);
            var turkish = term.Rule == GlossaryRule.Keep ? ScreenKit.Muted("aynen", 14) : ScreenKit.Text(term.Turkish ?? "", 14, wrap: false);
            turkish.VerticalAlignment = VerticalAlignment.Center;
            turkish.Margin = Sp.T("xs", "0", "0", "0");
            Grid.SetColumn(turkish, 1);
            grid.Children.Add(turkish);
            var rule = ScreenKit.Small(term.RuleLabel, 12);
            rule.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(rule, 2);
            grid.Children.Add(rule);
            var edit = new Button
            {
                Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = Sp.T("0", "s", "0", "s"), Cursor = Cursors.Hand,
                Background = Brushes.Transparent, Template = PlainTemplate(), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 0,
            };
            edit.SetResourceReference(FocusVisualStyleProperty, "FocusRing");
            AutomationProperties.SetName(edit, $"{term.English}: {(term.Rule == GlossaryRule.Keep ? "çevirme" : term.Turkish)}. Düzenle");
            var english2 = term.English;
            edit.Click += (_, _) => EditTerm(english2);
            var remove = new Button { Content = "×", Width = 32, Height = 32, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Sil" };
            remove.SetResourceReference(StyleProperty, "GhostButton");
            remove.Width = 32;
            remove.MinHeight = 32;
            remove.Padding = new Thickness(0);
            remove.FontSize = 16;
            AutomationProperties.SetName(remove, $"{term.English} terimini sil");
            remove.Click += (_, _) =>
            {
                controller.RemoveGlossaryTerm(english2);
                if (editingTerm == english2) ClearTermEditor();
            };
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            line.Children.Add(edit);
            Grid.SetColumn(remove, 1);
            line.Children.Add(remove);
            var row = ScreenKit.Row(line, "0");
            if (editingTerm == term.English) row.SetResourceReference(Border.BackgroundProperty, "fieldBrush");
            terms.Children.Add(row);
        }
        if (controller.GlossaryTerms.Count == 0)
        {
            var none = ScreenKit.Muted("Henüz terim yok. Örnek: Vice City (çevirme), bro → kanka (hep böyle).", 14);
            terms.Children.Add(ScreenKit.Row(none));
        }
    }

    private static ControlTemplate PlainTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Cell");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("fieldBrush"), "Cell"));
        template.Triggers.Add(hover);
        return template;
    }

    private void EditTerm(string english)
    {
        if (controller.GlossaryTerms.FirstOrDefault(term => term.English == english) is not { } term) return;
        editingTerm = term.English;
        termEnglish.Text = term.English;
        termTurkish.Text = term.Rule == GlossaryRule.Always ? term.Turkish ?? "" : "";
        termSave.Content = "Kaydet";
        termError.Visibility = Visibility.Collapsed;
        ShowTerms();
        termTurkish.Focus();
    }

    private void ClearTermEditor()
    {
        editingTerm = null;
        termEnglish.Text = "";
        termTurkish.Text = "";
        termSave.Content = "Ekle";
        ShowTerms();
    }

    private void OnTermKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SaveTerm();
        else if (e.Key == Key.Escape) ClearTermEditor();
    }

    /// <summary>Terim eklenir ya da güncellenir: karşılık boşsa "çevirme", doluysa "hep böyle". Ad değiştiyse eski terim silinir.</summary>
    private void SaveTerm()
    {
        var english = termEnglish.Text.Trim();
        var turkish = termTurkish.Text.Trim();
        if (english.Length == 0)
        {
            termError.Text = "Önce İngilizce terimi yaz.";
            termError.Visibility = Visibility.Visible;
            return;
        }
        var rule = turkish.Length == 0 ? GlossaryRule.Keep : GlossaryRule.Always;
        if (!controller.SaveGlossaryTerm(english, rule, turkish.Length == 0 ? null : turkish))
        {
            termError.Text = $"Terim kaydedilemedi (en çok {UserGlossary.MaxTermLength} karakter).";
            termError.Visibility = Visibility.Visible;
            return;
        }
        if (editingTerm is { } old && !string.Equals(old, english, StringComparison.OrdinalIgnoreCase)) controller.RemoveGlossaryTerm(old);
        termError.Visibility = Visibility.Collapsed;
        ClearTermEditor();
        termEnglish.Focus();
    }
}
