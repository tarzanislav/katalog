using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Katalog.Jadro.Model;

namespace Katalog.Okno;

/// <summary>
/// The details of a video file in the way of MediaInfo (owner, 8. 11. 2026): sections with a bold coloured heading,
/// lines of a label and a value in two columns, the main values in bold. It is a read-only rich text box: the text can
/// be selected with the mouse or the keyboard (Ctrl+A, Ctrl+C) and copied – as aligned plain text.
/// </summary>
public sealed class PodrobnostiSuboru : RichTextBox
{
    public static readonly DependencyProperty SekcieProperty = DependencyProperty.Register(
        nameof(Sekcie), typeof(IReadOnlyList<SekciaMedia>), typeof(PodrobnostiSuboru),
        new PropertyMetadata(null, (d, _) => ((PodrobnostiSuboru)d).Postav()));

    /// <summary>The values shown in bold: what tells one version from another at a glance.</summary>
    private static readonly HashSet<string> Hlavne = new(StringComparer.Ordinal)
    {
        "Formát", "Profil formátu", "Dĺžka", "Celkový dátový tok", "Dátový tok", "Šírka", "Výška", "Snímková frekvencia", "Bitová hĺbka",
        "Kanály", "Jazyk", "Veľkosť súboru", "Veľkosť streamu", "HDR", "Obchodný názov", "Názov",
    };

    public PodrobnostiSuboru()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsTabStop = true;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        DataObject.AddCopyingHandler(this, Kopirovanie);
        ToolTip = "Označ myšou alebo Ctrl+A, skopíruj Ctrl+C";
    }

    public IReadOnlyList<SekciaMedia>? Sekcie
    {
        get => (IReadOnlyList<SekciaMedia>?)GetValue(SekcieProperty);
        set => SetValue(SekcieProperty, value);
    }

    /// <summary>The details as aligned plain text ("Formát                     : Matroska").</summary>
    public static string Text(IEnumerable<SekciaMedia> sekcie)
    {
        var zoznam = sekcie.ToList();
        var sirka = zoznam.SelectMany(s => s.Polozky).Select(p => p.Popis.Length).DefaultIfEmpty(0).Max() + 2;
        var text = new StringBuilder();
        foreach (var sekcia in zoznam)
        {
            if (text.Length > 0)
            {
                text.AppendLine();
            }

            text.AppendLine(sekcia.Nazov);
            foreach (var p in sekcia.Polozky)
            {
                text.Append(p.Popis.PadRight(sirka)).Append(": ").AppendLine(p.Hodnota.Replace(' ', ' '));
            }
        }

        return text.ToString();
    }

    private void Postav()
    {
        var dokument = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = FontFamily,
            FontSize = 12.5,
            TextAlignment = TextAlignment.Left,
        };
        var tlmeny = new SolidColorBrush(Color.FromArgb(0xA8, 0x80, 0x80, 0x80));
        var pruh = new SolidColorBrush(Color.FromArgb(0x10, 0x80, 0x80, 0x80));
        tlmeny.Freeze();
        pruh.Freeze();
        var prva = true;
        foreach (var sekcia in Sekcie ?? [])
        {
            var nadpis = new Paragraph(new Run(sekcia.Nazov))
            {
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, prva ? 0 : 12, 0, 4),
            };
            nadpis.SetResourceReference(TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            dokument.Blocks.Add(nadpis);
            prva = false;

            var tabulka = new Table { CellSpacing = 0, Margin = new Thickness(0) };
            tabulka.Columns.Add(new TableColumn { Width = new GridLength(178) });
            tabulka.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            var riadky = new TableRowGroup();
            var i = 0;
            foreach (var p in sekcia.Polozky)
            {
                var riadok = new TableRow { Background = i++ % 2 == 1 ? pruh : Brushes.Transparent };
                riadok.Cells.Add(new TableCell(new Paragraph(new Run(p.Popis)) { Margin = new Thickness(4, 1, 8, 1), Foreground = tlmeny }));
                riadok.Cells.Add(new TableCell(new Paragraph(new Run(p.Hodnota))
                {
                    Margin = new Thickness(0, 1, 4, 1),
                    FontWeight = Hlavne.Contains(p.Popis) ? FontWeights.SemiBold : FontWeights.Normal,
                }));
                riadky.Rows.Add(riadok);
            }

            tabulka.RowGroups.Add(riadky);
            dokument.Blocks.Add(tabulka);
        }

        Document = dokument;
    }

    /// <summary>A copy of all of it (Ctrl+A) is the aligned text; a part is copied as the user selected it.</summary>
    private void Kopirovanie(object sender, DataObjectCopyingEventArgs e)
    {
        if (Sekcie is not { Count: > 0 } sekcie)
        {
            return;
        }

        var cele = Selection.Start.CompareTo(Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)) <= 0 &&
                   Selection.End.CompareTo(Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)) >= 0;
        if (cele)
        {
            e.CancelCommand();
            try
            {
                Clipboard.SetText(Text(sekcie));
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // the clipboard is held by another program: nothing is copied this time
            }
        }
    }
}
