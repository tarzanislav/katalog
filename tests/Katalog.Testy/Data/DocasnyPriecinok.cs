namespace Katalog.Testy;

/// <summary>A temporary folder deleted after the test.</summary>
internal sealed class DocasnyPriecinok : IDisposable
{
    public DocasnyPriecinok()
    {
        Cesta = Path.Combine(Path.GetTempPath(), "katalog-test-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Cesta);
    }

    public string Cesta { get; }

    public string Subor(string nazov) => Path.Combine(Cesta, nazov);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Cesta, recursive: true);
        }
        catch (IOException)
        {
            // a file still open on Windows: the temp folder is cleaned by the system later
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
