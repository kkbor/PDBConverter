using PDBLibrary.Models;
using PDBLibrary.Parsers;
using PDBLibrary.Srevices;
using PDBLibrary.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace PdbEditorApp.Tests
{
    /// <summary>
    /// Testy podmiany aminokwasu (ResidueMutator.Mutate) oraz walidacji PO
    /// mutacji (StructureValidator.ValidateAfterMutation / ChainValidator /
    /// ClashDetector).
    ///
    /// W odroznieniu od ValidationRegressionTests (ktore sprawdzaja SUROWE,
    /// niezmodyfikowane struktury), te testy sprawdzaja efekt samej operacji
    /// podmiany: czy szkielet zostaje nietkniety, czy nowy lancuch boczny
    /// faktycznie odpowiada nowemu aminokwasowi, oraz czy walidacja scoped
    /// (po mutacji) poprawnie ogranicza sie do zmutowanej reszty i jej
    /// bezposrednich sasiadow.
    ///
    /// WYMAGANIA URUCHOMIENIOWE (tak samo jak dla ValidationRegressionTests):
    ///  - TestData/1CRN.pdb musi byc skopiowany do katalogu wyjsciowego
    ///    (CopyToOutputDirectory w .csproj) - to ta sama, znana-dobra
    ///    struktura (baseline 0 bledow), ktorej uzywaja testy regresyjne.
    ///  - Data/AminoAcids/*.cif (20 szablonow CCD) musi byc skopiowane do
    ///    katalogu wyjsciowego - bez nich AminoAcidTemplateLibrary rzuci
    ///    wyjatkiem juz przy pierwszym dotknieciu (statyczna inicjalizacja).
    /// </summary>
    public class ResidueMutationTests
    {
        private readonly ITestOutputHelper _output;

        public ResidueMutationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static readonly HashSet<string> BackboneAtomNames =
            new(StringComparer.OrdinalIgnoreCase) { "N", "CA", "C", "O", "OXT" };

        // ---------- pomocnicze ----------

        private static Protein LoadKnownGoodProtein()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "TestData", "1CRN.pdb");

            if (!File.Exists(path))
                throw new InvalidOperationException(
                    $"Brak pliku testowego {path} - testy mutacji wymagają 1CRN.pdb " +
                    "w TestData (ten sam plik, którego używają testy regresyjne walidacji).");

            return StructureFileLoader.Load(path);
        }

        // Wybiera resztę "bezpieczną" do testu: standardowy aminokwas, NIE na
        // samym końcu łańcucha (żeby sprawdzenie wiązania peptydowego miało
        // sąsiadów po obu stronach) i o innej nazwie niż podana (żeby test
        // faktycznie testował ZMIANĘ, a nie przypadkowe "mutowanie na to samo").
        private static Residues PickMutableResidue(Protein protein, string excludeName)
        {
            var chain = protein.Chains.First(c => c.Residues.Count(IsStandardAminoAcid) >= 5);

            var residues = chain.Residues
                .Where(IsStandardAminoAcid)
                .OrderBy(r => r.Id)
                .ToList();

            return residues
                .Skip(2)
                .SkipLast(2)
                .First(r => !r.Name.Equals(excludeName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsStandardAminoAcid(Residues r) =>
            AminoAcidCodes.IsStandardAminoAcid(r.Name);

        // ============================================================
        // Testy na realnej, znanej-dobrej strukturze (1CRN.pdb)
        // ============================================================

        [Fact]
        public void Mutate_ChangesResidueNameAndAtoms()
        {
            var protein = LoadKnownGoodProtein();
            var residue = PickMutableResidue(protein, excludeName: "TRP");

            string oldName = residue.Name;

            var oldBackbonePositions = residue.Atoms
                .Where(a => BackboneAtomNames.Contains(a.Name))
                .ToDictionary(a => a.Name.ToUpperInvariant(), a => (a.X, a.Y, a.Z));

            ResidueMutator.Mutate(residue, "TRP");

            Assert.Equal("TRP", residue.Name);
            Assert.NotEqual(oldName, residue.Name);

            // wszystkie atomy w resztce mają teraz spójną nazwę reszty
            Assert.All(residue.Atoms, a => Assert.Equal("TRP", a.ResidueName));

            // szkielet NIE zmienił położenia - mutacja dotyka wyłącznie łańcucha bocznego
            foreach (var atom in residue.Atoms.Where(a => BackboneAtomNames.Contains(a.Name)))
            {
                var (ox, oy, oz) = oldBackbonePositions[atom.Name.ToUpperInvariant()];
                Assert.Equal(ox, atom.X, precision: 6);
                Assert.Equal(oy, atom.Y, precision: 6);
                Assert.Equal(oz, atom.Z, precision: 6);
            }

            // TRP ma duży, charakterystyczny łańcuch boczny (pierścień indolowy) -
            // sprawdzamy, że doszły atomy specyficzne dla tryptofanu.
            var sideChainNames = residue.Atoms
                .Where(a => !BackboneAtomNames.Contains(a.Name))
                .Select(a => a.Name.ToUpperInvariant())
                .ToHashSet();

            Assert.Contains("CB", sideChainNames);
            Assert.Contains("CG", sideChainNames);
            Assert.Contains("NE1", sideChainNames); // azot pierścienia indolowego - unikalny dla TRP
        }

        [Fact]
        public void Mutate_ToGlycine_RemovesAllSideChainHeavyAtoms()
        {
            var protein = LoadKnownGoodProtein();

            var residue =
                PickMutableResidue(
                    protein,
                    excludeName: "GLY");

            ResidueMutator.Mutate(
                residue,
                "GLY");

            Assert.Equal("GLY", residue.Name);

            // Glicyna nie posiada atomu C-beta.
            Assert.DoesNotContain(
                residue.Atoms,
                a => a.Name.Equals(
                    "CB",
                    StringComparison.OrdinalIgnoreCase));

            // Nie powinno być żadnych ciężkich atomów
            // łańcucha bocznego.
            var sideChainHeavyAtoms = residue.Atoms
                .Where(a =>
                    !BackboneAtomNames.Contains(a.Name) &&
                    !string.Equals(
                        a.Element,
                        "H",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Empty(sideChainHeavyAtoms);
        }

        [Fact]
        public void Mutate_PreservesBackboneConnectivity_NoNewBackboneErrors()
        {
            var protein = LoadKnownGoodProtein();
            var residue = PickMutableResidue(protein, excludeName: "PHE");

            ResidueMutator.Mutate(residue, "PHE");

            // Walidacja scoped - dokładnie to, czego używa UI po każdej mutacji.
            var scoped = StructureValidator.ValidateAfterMutation(protein, residue);

            // Szkielet jest nietknięty, więc NIE powinno przybyć ani jednego błędu
            // związanego ze szkieletem/wiązaniem peptydowym (brak atomów N/CA/C,
            // zła długość wiązania C-N) wokół zmutowanej reszty.
            var backboneRelated = scoped.Issues.Where(i =>
                i.Severity == ValidationSeverity.Error &&
                (i.Message.Contains("szkieletu") || i.Message.Contains("wiązania peptydowego")));

            Assert.Empty(backboneRelated);

            _output.WriteLine(
                $"Po mutacji {residue.Name} {residue.Id}: {scoped.Issues.Count} problem(ów) " +
                $"w walidacji scoped ({scoped.ErrorCount} błędów, {scoped.WarningCount} ostrzeżeń).");

            foreach (var issue in scoped.Issues)
                _output.WriteLine($"  [{issue.Severity}] {issue.Message}");
        }

        [Fact]
        public void ValidateAfterMutation_ChainIssues_OnlyTouchMutatedResidueOrItsNeighbours()
        {
            // ChainValidator.ValidateResidue powinno zgłaszać WYŁĄCZNIE problemy
            // dotyczące zmutowanej reszty i jej bezpośrednich sąsiadów w
            // numeracji - a nie przeglądać cały łańcuch od nowa.
            var protein = LoadKnownGoodProtein();
            var residue = PickMutableResidue(protein, excludeName: "LEU");

            ResidueMutator.Mutate(residue, "LEU");

            var report = new ValidationReport();
            ChainValidator.ValidateResidue(protein, residue, report);

            foreach (var issue in report.Issues)
            {
                bool touchesMutatedResidue =
                    ReferenceEquals(issue.Residue, residue) ||
                    ReferenceEquals(issue.ResidueB, residue);

                Assert.True(touchesMutatedResidue,
                    $"Walidacja scoped zgłosiła problem niezwiązany ze zmutowaną resztą: {issue.Message}");
            }
        }

        [Fact]
        public void Mutate_SameAminoAcid_KeepsBackboneValid()
        {
            // Podmiana "na to samo" (np. po to, by odbudować łańcuch boczny z
            // szablonu) powinna być bezpieczna - szkielet nietknięty, żadnych
            // nowych błędów szkieletu/wiązania peptydowego.
            var protein = LoadKnownGoodProtein();
            var residue = PickMutableResidue(protein, excludeName: "___none___");
            string name = residue.Name;

            ResidueMutator.Mutate(residue, name);

            Assert.Equal(name, residue.Name);

            var report = new ValidationReport();
            ChainValidator.ValidateResidue(protein, residue, report);

            Assert.DoesNotContain(report.Issues, i => i.Severity == ValidationSeverity.Error);
        }

        // ============================================================
        // Testy na ręcznie skonstruowanych, minimalnych wejściach
        // (bez wczytywania pliku - szybkie, deterministyczne przypadki brzegowe)
        // ============================================================

        [Fact]
        public void Mutate_NullResidue_ThrowsArgumentNullException()
        {
            var protein = new Protein();

            Assert.Throws<ArgumentNullException>(() =>
                ResidueMutator.Mutate(null!, "GLY"));
        }

        [Fact]
        public void Mutate_EmptyNewCode_ThrowsArgumentException()
        {
            var protein = new Protein();
            var residue = new Residues { Id = 1, Name = "ALA", InsertionCode = ' ' };

            Assert.Throws<ArgumentException>(() =>
                ResidueMutator.Mutate(residue, "   "));
        }

        [Fact]
        public void Mutate_UnknownAminoAcidCode_ThrowsInvalidOperationException()
        {
            var protein = new Protein();
            var residue = new Residues { Id = 1, Name = "ALA", InsertionCode = ' ' };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ResidueMutator.Mutate(residue, "ZZZ"));

            Assert.Contains("szablonu", ex.Message);
        }

        [Fact]
        public void Mutate_IncompleteBackbone_ThrowsInvalidOperationException()
        {
            // Reszta ma tylko atom N (brak CA/C) - SideChainBuilder nie ma na
            // czym oprzeć obrotu, więc Mutate powinien rzucić wyjątkiem zamiast
            // zostawić resztę w połowie podmienioną.
            var chain = new Chains { Id = 'A' };
            var residue = new Residues { Id = 1, Name = "ALA", InsertionCode = ' ' };

            residue.Atoms.Add(new Atoms
            {
                Id = 1,
                Name = "N",
                ResidueName = "ALA",
                ChainId = 'A',
                ResidueId = 1,
                InsertionCode = ' ',
                X = 0,
                Y = 0,
                Z = 0,
                Occupancy = 1,
                BFactor = 0,
                Element = "N"
            });

            chain.Residues.Add(residue);

            var protein = new Protein();
            protein.Chains.Add(chain);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ResidueMutator.Mutate(residue, "SER"));

            Assert.Contains("szkieletu", ex.Message);
        }
    }
}
