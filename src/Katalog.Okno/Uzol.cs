using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Katalog.Okno;

internal enum DruhUzla
{
    Katalog,
    Disk,
    Priecinok,
    /// <summary>Placeholder child of a node whose children are loaded only when it is opened.</summary>
    Nacitavam,
}

/// <summary>
/// A node of the tree of catalogues, disks and folders. Children of disks and folders are read from the library only when
/// the node is opened, so even a disk with hundreds of thousands of folders opens at once.
/// </summary>
internal sealed class Uzol : INotifyPropertyChanged
{
    private bool _otvoreny;
    private bool _vybraty;
    private string _nazov;
    private string _popis = "";
    private bool _pripojeny;

    public Uzol(DruhUzla druh, long id, string nazov, long diskId = 0, bool maDeti = false)
    {
        Druh = druh;
        Id = id;
        DiskId = diskId;
        _nazov = nazov;
        if (maDeti)
        {
            Deti.Add(new Uzol(DruhUzla.Nacitavam, 0, "…"));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Called when a node with unloaded children is opened.</summary>
    public static Action<Uzol>? NacitajDeti { get; set; }

    public DruhUzla Druh { get; }

    /// <summary>Id of the catalogue, disk or folder.</summary>
    public long Id { get; }

    /// <summary>Disk of a disk or folder node.</summary>
    public long DiskId { get; }

    public ObservableCollection<Uzol> Deti { get; } = [];

    public string Nazov
    {
        get => _nazov;
        set => Nastav(ref _nazov, value);
    }

    /// <summary>Second, quieter line ("003 · USB · 1,8 TB").</summary>
    public string Popis
    {
        get => _popis;
        set
        {
            Nastav(ref _popis, value);
            Zmenene(nameof(MaPopis));
        }
    }

    public bool MaPopis => _popis.Length > 0;

    /// <summary>A disk that is connected now (shown with a green dot).</summary>
    public bool Pripojeny
    {
        get => _pripojeny;
        set => Nastav(ref _pripojeny, value);
    }

    public bool JeDisk => Druh == DruhUzla.Disk;

    /// <summary>Glyph of Segoe Fluent Icons: catalogue, drive, folder.</summary>
    public string Ikona => Druh switch
    {
        DruhUzla.Katalog => "",
        DruhUzla.Disk => "",
        DruhUzla.Priecinok => "",
        _ => "",
    };

    public bool Otvoreny
    {
        get => _otvoreny;
        set
        {
            if (!Nastav(ref _otvoreny, value) || !value)
            {
                return;
            }

            if (Deti.Count == 1 && Deti[0].Druh == DruhUzla.Nacitavam)
            {
                NacitajDeti?.Invoke(this);
            }
        }
    }

    public bool Vybraty
    {
        get => _vybraty;
        set => Nastav(ref _vybraty, value);
    }

    private bool Nastav<T>(ref T pole, T hodnota, [CallerMemberName] string? meno = null)
    {
        if (EqualityComparer<T>.Default.Equals(pole, hodnota))
        {
            return false;
        }

        pole = hodnota;
        Zmenene(meno);
        return true;
    }

    private void Zmenene(string? meno) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(meno));
}
