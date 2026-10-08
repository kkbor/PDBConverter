using PDBLibrary.Parsers;
using PDBLibrary.Validation;
using Xunit.Abstractions;

namespace PdbEditorApp.Tests
{
    /// <summary>
    /// Testy regresyjne walidacji struktur. Dla kazdego pliku w TestData/ (BEZ
    /// zadnej modyfikacji/podmiany aminokwasu - surowa, swiezo wczytana struktura)
    /// uruchamia pelna walidacje (ChainValidator + ClashDetector).
    ///
    /// Dwa rodzaje testow:
    ///  1) Validate_RunsWithoutException_AndReportsCounts - dziala dla WSZYSTKICH
    ///     plikow w TestData/. Nie zaklada "poprawnej" liczby bledow (nie znamy
    ///     jej z gory dla kazdego pliku) - sprawdza tylko, ze wczytanie+walidacja
    ///     nie wywraca sie wyjatkiem, i WYPISUJE liczby do logu testu (widoczne
    ///     w Test Explorerze / `dotnet test -v normal`) do recznej inspekcji.
    ///  2) Validate_HasNoErrors_ForKnownCleanFiles - TWARDA asercja (ErrorCount == 0)
    ///     tylko dla plikow z listy KnownCleanFiles, czyli tych, ktore juz RECZNIE
    ///     zweryfikowalas jako poprawne. To jest wlasciwy "straznik regresji":
    ///     jesli przyszla zmiana w BondDetector/ClashDetector/parserze zepsuje
    ///     cos, co wczesniej dzialalo poprawnie, ten test sie czerwieni.
    /// </summary>
    public class ValidationRegressionTests
    {
        private readonly ITestOutputHelper _output;

        public ValidationRegressionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // Pliki, dla ktorych na podstawie recznej weryfikacji WIADOMO, ze
        // walidacja powinna wykazac ZERO bledow (ostrzezenia - Warning - NIE sa
        // tu sprawdzane; kilka ostrzezen na realnej strukturze krystalograficznej
        // jest normalne i oczekiwane, patrz dyskusja o FAD/kofaktorach).
        //
        // Dopisuj tu kolejne pliki w miare, jak reczn potwierdzisz w UI, ze
        // powinny przechodzic bez bledow.
        private static readonly HashSet<string> KnownCleanFiles =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "1CRN.pdb",
                "1I1M.pdb",
                "5VME.pdb"
            };

        private static readonly string[] RecognizedExtensions =
            { ".pdb", ".ent", ".cif", ".mmcif" };

        public static IEnumerable<object[]> AllTestDataFiles()
        {
            foreach (var path in EnumerateTestDataFiles())
                yield return new object[] { path };
        }

        public static IEnumerable<object[]> KnownCleanTestDataFiles()
        {
            foreach (var path in EnumerateTestDataFiles())
            {
                if (KnownCleanFiles.Contains(Path.GetFileName(path)))
                    yield return new object[] { path };
            }
        }

        private static IEnumerable<string> EnumerateTestDataFiles()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "TestData");

            if (!Directory.Exists(dir))
                yield break;

            foreach (var path in Directory.GetFiles(dir).OrderBy(p => p))
            {
                string ext = Path.GetExtension(path);
                if (RecognizedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    yield return path;
            }
        }

        /// <summary>
        /// Zabezpieczenie przed cichym "zielonym" wynikiem, gdy folder TestData
        /// jest pusty/nieznaleziony - wtedy Theory ponizej uruchomilyby sie
        /// zero razy, co wygladaloby na sukces, a w rzeczywistosci nic by sie
        /// nie przetestowalo.
        /// </summary>
        [Fact]
        public void TestData_FolderExistsAndContainsRecognizedFiles()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "TestData");

            Assert.True(Directory.Exists(dir),
                $"Brak folderu TestData pod: {dir}\n" +
                "Upewnij sie, ze pliki sa kopiowane do katalogu wyjsciowego " +
                "(CopyToOutputDirectory w .csproj) i ze projekt zostal zbudowany.");

            var files = EnumerateTestDataFiles().ToList();

            Assert.True(files.Count > 0,
                $"Folder TestData ({dir}) istnieje, ale nie zawiera zadnych " +
                $"plikow .pdb/.cif.");
        }

        [Theory]
        [MemberData(nameof(AllTestDataFiles))]
        public void Validate_RunsWithoutException_AndReportsCounts(string path)
        {
            string fileName = Path.GetFileName(path);

            var protein = StructureFileLoader.Load(path);
            var report = StructureValidator.Validate(protein);

            _output.WriteLine(
                $"{fileName}: {report.Issues.Count} problem(ow) - " +
                $"{report.ErrorCount} blednow, {report.WarningCount} ostrzezen");

            foreach (var issue in report.Issues.Where(i => i.Severity == ValidationSeverity.Error))
                _output.WriteLine($"    BLAD: {issue.Message}");

            // Minimalna, twarda gwarancja dla KAZDEGO pliku testowego: wczytanie
            // i walidacja nigdy nie powinny rzucic wyjatku (NullReferenceException,
            // FormatException z parsera itp.) - to samo w sobie wylapuje realne bledy.
            Assert.NotNull(report);
        }

        [Theory]
        [MemberData(nameof(KnownCleanTestDataFiles))]
        public void Validate_HasNoErrors_ForKnownCleanFiles(string path)
        {
            string fileName = Path.GetFileName(path);

            var protein = StructureFileLoader.Load(path);
            var report = StructureValidator.Validate(protein);

            var errorDetails = string.Join("\n", report.Issues
                .Where(i => i.Severity == ValidationSeverity.Error)
                .Select(i => "  - " + i.Message));

            Assert.True(report.ErrorCount == 0,
                $"{fileName} jest na liscie znanych 'czystych' plikow, ale walidacja " +
                $"znalazla {report.ErrorCount} b³êdów:\n{errorDetails}");
        }
    }
}

