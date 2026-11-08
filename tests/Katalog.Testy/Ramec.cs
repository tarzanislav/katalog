using System.Diagnostics;
using System.Reflection;

namespace Katalog.Testy;

/// <summary>Marks a test: a public parameterless method returning void or Task.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestAttribute : Attribute;

/// <summary>Thrown to skip a test whose precondition is missing (e.g. the speed test outside CI).</summary>
internal sealed class PreskocException(string dovod) : Exception(dovod);

internal sealed class ChybaTestuException(string sprava) : Exception(sprava);

internal static class Over
{
    public static void Pravda(bool podmienka, string sprava)
    {
        if (!podmienka)
        {
            throw new ChybaTestuException(sprava);
        }
    }

    public static void Rovne<T>(T ocakavane, T skutocne, string? co = null)
    {
        if (!EqualityComparer<T>.Default.Equals(ocakavane, skutocne))
        {
            throw new ChybaTestuException($"{co ?? "hodnota"}: očakávané <{ocakavane}>, skutočné <{skutocne}>");
        }
    }

    public static void Postupnost<T>(IEnumerable<T> ocakavane, IEnumerable<T> skutocne, string? co = null)
    {
        var o = ocakavane.ToList();
        var s = skutocne.ToList();
        if (!o.SequenceEqual(s))
        {
            throw new ChybaTestuException($"{co ?? "postupnosť"}: očakávané [{string.Join(", ", o)}], skutočné [{string.Join(", ", s)}]");
        }
    }

    public static TVynimka Vyhodi<TVynimka>(Action akcia, string co)
        where TVynimka : Exception
    {
        try
        {
            akcia();
        }
        catch (TVynimka ex)
        {
            return ex;
        }

        throw new ChybaTestuException($"{co}: očakávaná výnimka {typeof(TVynimka).Name}");
    }

    public static async Task<TVynimka> VyhodiAsync<TVynimka>(Func<Task> akcia, string co)
        where TVynimka : Exception
    {
        try
        {
            await akcia().ConfigureAwait(false);
        }
        catch (TVynimka ex)
        {
            return ex;
        }

        throw new ChybaTestuException($"{co}: očakávaná výnimka {typeof(TVynimka).Name}");
    }

    public static void Preskoc(string dovod) => throw new PreskocException(dovod);
}

internal static class Bezec
{
    public static async Task<int> SpustiAsync(string[] args)
    {
        var filter = args.FirstOrDefault(a => !a.StartsWith('-'));
        var testy = typeof(Bezec).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
            .Select(m => (Metoda: m, Nazov: $"{m.DeclaringType!.Name}.{m.Name}"))
            .Where(t => filter is null || t.Nazov.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Nazov, StringComparer.Ordinal)
            .ToList();

        int uspech = 0, chyba = 0, preskocene = 0;
        var celkom = Stopwatch.StartNew();
        foreach (var (metoda, nazov) in testy)
        {
            var cas = Stopwatch.StartNew();
            try
            {
                var instancia = metoda.IsStatic ? null : Activator.CreateInstance(metoda.DeclaringType!);
                if (metoda.Invoke(instancia, null) is Task uloha)
                {
                    await uloha.ConfigureAwait(false);
                }

                uspech++;
                Console.WriteLine($"OK    {nazov} ({cas.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex) when (Rozbal(ex) is PreskocException preskoc)
            {
                preskocene++;
                Console.WriteLine($"SKIP  {nazov}: {preskoc.Message}");
            }
            catch (Exception ex)
            {
                chyba++;
                var vnutorna = Rozbal(ex);
                Console.WriteLine($"CHYBA {nazov}: {vnutorna.Message}");
                if (vnutorna is not ChybaTestuException)
                {
                    Console.WriteLine(vnutorna);
                }

                if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
                {
                    Console.WriteLine($"::error title=Test {nazov}::{vnutorna.Message.Replace('\n', ' ')}");
                }
            }
        }

        Console.WriteLine($"{uspech} úspešných, {chyba} chýb, {preskocene} preskočených za {celkom.Elapsed.TotalSeconds:F1} s");
        return chyba == 0 && testy.Count > 0 ? 0 : 1;
    }

    private static Exception Rozbal(Exception ex) =>
        ex is TargetInvocationException { InnerException: { } vnutorna } ? Rozbal(vnutorna) : ex;
}
