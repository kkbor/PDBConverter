using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PDBLibrary.Validation
{
    /// <summary>
    /// Sprawdza spojnosc lancucha polipeptydowego:
    ///  1) czy kazda reszta ma komplet atomow szkieletu (N, CA, C),
    ///  2) czy odleglosc C(i)-N(i+1) miedzy kolejnymi resztami miesci sie
    ///     w normalnym zakresie dlugosci wiazania peptydowego (typowo ~1.33 A) -
    ///     jesli nie, oznacza to przerwe w lancuchu (albo zle dane wejsciowe).
    ///
    /// Rozroznienie (wazne, zeby nie mylic dwoch roznych sytuacji):
    ///  - LUKA W NUMERACJI (brakujace reszty, np. 23 -> 77) -> Warning
    ///    "Brakujace reszty" - to nie jest zla geometria, po prostu plik nie
    ///    ma danych dla tego fragmentu (typowe dla nierozwiazanych petli).
    ///  - RESZTY SA KOLEJNE, ale odleglosc C-N jest nienormalna -> Error
    ///    "Nieprawidlowa dlugosc wiazania peptydowego" - to faktyczny problem
    ///    geometryczny.
    ///
    /// Cala ta logika zyje WYLACZNIE w CheckPeptideBond - Validate() i
    /// ValidateResidue() wywoluja ja bezwarunkowo dla kazdej sasiadujacej pary,
    /// zeby obie sciezki (pelna walidacja / walidacja po mutacji) dawaly
    /// identyczny wynik dla tej samej pary reszt.
    /// </summary>
    public static class ChainValidator
    {
        private static readonly HashSet<string> StandardAminoAcids =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "ALA", "ARG", "ASN", "ASP", "CYS",
                "GLN", "GLU", "GLY", "HIS", "ILE",
                "LEU", "LYS", "MET", "PHE", "PRO",
                "SER", "THR", "TRP", "TYR", "VAL"
            };

        private static readonly string[] RequiredBackboneAtoms = { "N", "CA", "C" };

        // typowa dlugosc wiazania peptydowego C-N to ok. 1.33 A; zakres ponizej
        // jest swiadomie szeroki (tolerancja na realne odksztalcenia struktur
        // eksperymentalnych), to "podstawowa" walidacja, nie precyzyjny osad
        private const double MinPeptideBond = 1.0;
        private const double MaxPeptideBond = 1.9;

        public static void Validate(Protein protein, ValidationReport report)
        {
            foreach (var chain in protein.Chains)
            {
                // Bierzemy pod uwagę tylko standardowe aminokwasy.
                // Ligandy, wodę, jony i inne HETATM są pomijane.
                //
                // ThenBy(InsertionCode) jest konieczne: "23" i "23A" to teraz
                // osobne obiekty Residues o tym samym Id - samo OrderBy(r => r.Id)
                // nie gwarantuje poprawnej kolejnosci biologicznej miedzy nimi.
                var aminoAcidResidues = chain.Residues
                    .Where(IsStandardAminoAcid)
                    .OrderBy(r => r.Id)
                    .ThenBy(r => r.InsertionCode)
                    .ToList();

                foreach (var residue in aminoAcidResidues)
                    CheckBackboneCompleteness(chain, residue, report);

                // CheckPeptideBond sam rozroznia "luka w numeracji" (Warning) od
                // "zla geometria miedzy kolejnymi resztami" (Error) - wywolujemy
                // go bezwarunkowo dla kazdej sasiadujacej pary.
                for (int i = 0; i < aminoAcidResidues.Count - 1; i++)
                    CheckPeptideBond(chain, aminoAcidResidues[i], aminoAcidResidues[i + 1], report);
            }
        }

        /// <summary>
        /// Walidacja ograniczona do JEDNEJ reszty (np. po podmianie aminokwasu) -
        /// sprawdza jej szkielet oraz ciaglosc z bezposrednimi sasiadami w
        /// sekwencji. Celowo wywoluje CheckPeptideBond DOKLADNIE tak samo jak
        /// Validate() (bez dodatkowego warunku na zewnatrz) - dzieki temu jesli
        /// podmieniana reszta sasiaduje z prawdziwa luka w strukturze, user
        /// dostanie o tym informacje (Warning) tutaj tak samo, jak dostalby
        /// przy pelnej walidacji, zamiast ciszy.
        /// </summary>
        public static void ValidateResidue(Protein protein, Residues changedResidue, ValidationReport report)
        {
            var chain = protein.Chains.FirstOrDefault(c => c.Residues.Contains(changedResidue));

            if (chain == null)
                return;

            CheckBackboneCompleteness(chain, changedResidue, report);

            var residues = chain.Residues
                .Where(IsStandardAminoAcid)
                .OrderBy(r => r.Id)
                .ThenBy(r => r.InsertionCode)
                .ToList();

            int index = residues.IndexOf(changedResidue);

            if (index == -1)
                return;

            if (index > 0)
                CheckPeptideBond(chain, residues[index - 1], residues[index], report);

            if (index < residues.Count - 1)
                CheckPeptideBond(chain, residues[index], residues[index + 1], report);
        }

        /// <summary>
        /// Sprawdza, czy dana reszta jest jednym z 20 standardowych
        /// aminokwasów.
        /// </summary>
        private static bool IsStandardAminoAcid(Residues residue)
        {
            if (residue == null || string.IsNullOrWhiteSpace(residue.Name))
                return false;

            return StandardAminoAcids.Contains(residue.Name.Trim().ToUpperInvariant());
        }

        private static void CheckBackboneCompleteness(Chains chain, Residues residue, ValidationReport report)
        {
            foreach (var required in RequiredBackboneAtoms)
            {
                bool present = residue.Atoms.Any(a => a.Name.Equals(required, StringComparison.OrdinalIgnoreCase));

                if (!present)
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Reszta {residue.Name} {residue.Id} (łańcuch {chain.Id}) " +
                                  $"nie ma atomu szkieletu {required}.",
                        Residue = residue
                    });
                }
            }
        }

        /// <summary>
        /// Jedyne miejsce, w ktorym zyje rozroznienie "luka w numeracji" vs
        /// "zla geometria". Wywolywane bezwarunkowo z Validate() i
        /// ValidateResidue() dla kazdej sasiadujacej pary reszt - gwarantuje
        /// to identyczny wynik niezaleznie od tego, ktora sciezka wywolala walidacje.
        /// </summary>
        private static void CheckPeptideBond(Chains chain, Residues first, Residues second, ValidationReport report)
        {
            // Jezeli numery reszt nie sa kolejne, oznacza to brak fragmentu
            // lancucha (np. nierozwiazana petla w krysztale), a NIE bledna
            // geometrie wiazania - to rozne sytuacje, rozne komunikaty.
            if (second.Id > first.Id + 1)
            {
                report.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"Brakujące reszty w łańcuchu {chain.Id} między " +
                              $"{first.Name} {first.Id} a {second.Name} {second.Id}.",
                    Residue = first,
                    ResidueB = second
                });
                return;
            }

            var c = FindAtom(first, "C");
            var n = FindAtom(second, "N");

            if (c == null || n == null)
                return; // brak atomu juz zgloszony przez CheckBackboneCompleteness

            double distance = Distance(c, n);

            if (distance < MinPeptideBond || distance > MaxPeptideBond)
            {
                report.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Nieprawidłowa długość wiązania peptydowego w łańcuchu {chain.Id} " +
                              $"między {first.Name} {first.Id} a {second.Name} {second.Id}: " +
                              $"C–N = {distance:F2} Å.",
                    Residue = first,
                    ResidueB = second
                });
            }
        }

        private static Atoms? FindAtom(Residues residue, string name) =>
            residue.Atoms.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private static double Distance(Atoms a, Atoms b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}