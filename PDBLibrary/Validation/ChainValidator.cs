using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Validation
{
    /// <summary>
    /// Sprawdza spojnosc lancucha polipeptydowego:
    ///  1) czy kazda reszta ma komplet atomow szkieletu (N, CA, C),
    ///  2) czy odleglosc C(i)-N(i+1) miedzy kolejnymi resztami miesci sie
    ///     w normalnym zakresie dlugosci wiazania peptydowego (typowo ~1.33 A) -
    ///     jesli nie, oznacza to przerwe w lancuchu (albo zle dane wejsciowe).
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

                for (int i = 0; i < aminoAcidResidues.Count - 1; i++)
                {
                    var current = aminoAcidResidues[i];
                    var next = aminoAcidResidues[i + 1];

                    // Jeżeli numeracja wskazuje na brakujące reszty,
                    // nie sprawdzamy między nimi wiązania peptydowego.
                    if (next.Id > current.Id + 1)
                    {
                        report.Issues.Add(new ValidationIssue
                        {
                            Severity = ValidationSeverity.Warning,
                            Message =
                                $"Brakujące reszty w łańcuchu {chain.Id} " +
                                $"między {current.Name} {current.Id} a {next.Name} {next.Id}.",
                            Residue = current,
                            ResidueB = next
                        });

                        continue;
                    }

                    CheckPeptideBond(chain, current, next, report);
                }
            }
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

        private static void CheckPeptideBond(Chains chain, Residues first, Residues second, ValidationReport report)
        {
            // Jeżeli numery reszt nie są kolejne,
            // oznacza to brak fragmentu łańcucha,
            // a nie błędną geometrię wiązania.
            if (second.Id > first.Id + 1)
            { 
                report.Issues.Add(new ValidationIssue 
                { 
                    Severity = ValidationSeverity.Warning,
                    Message = $"Brakujące reszty w łańcuchu" +
                    $" {chain.Id} między " + $"{first.Name} {first.Id} a {second.Name} {second.Id}.",
                    Residue = first, ResidueB = second 
                }); 
                return;
            } 
            var c = FindAtom(first, "C");
            var n = FindAtom(second, "N");
            // Brak atomu został już zgłoszony przez CheckBackboneCompleteness.
            if (c == null || n == null) 
                return;
            double distance = Distance(c, n); 
            // Sprawdzamy geometrię tylko pomiędzy sąsiednimi resztami.
            if (distance < MinPeptideBond || distance > MaxPeptideBond)
            { 
                report.Issues.Add(new ValidationIssue 
                { 
                    Severity = ValidationSeverity.Error, 
                    Message = $"Nieprawidłowa długość wiązania peptydowego w łańcuchu {chain.Id} " + 
                    $"między {first.Name} {first.Id} a {second.Name} {second.Id}: " + $"C–N = {distance:F2} Å.",
                    Residue = first, ResidueB = second 
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
        public static void ValidateResidue(Protein protein, Residues changedResidue, ValidationReport report)
        {
            // 1. Znalezienie łańcucha, do którego należy zmieniona reszta
            var chain = protein.Chains
                .FirstOrDefault(c => c.Residues.Contains(changedResidue));

            if (chain == null)
                return;

            // 2. Sprawdzenie kompletności szkieletu zmienionej reszty
            CheckBackboneCompleteness(chain, changedResidue, report);

            // 3. Pobranie standardowych aminokwasów w tej samej
            //    kolejności co w pełnej walidacji
            var residues = chain.Residues
                .Where(r => StandardAminoAcids.Contains(r.Name))
                .OrderBy(r => r.Id)
                .ThenBy(r => r.InsertionCode)
                .ToList();

            // 4. Znalezienie pozycji zmienionej reszty
            int index = residues.IndexOf(changedResidue);

            if (index == -1)
                return;
            // W walidacji po mutacji nie raportujemy istniejących wcześniej
            // luk w numeracji. Pełna walidacja struktury robi to w Validate().
            // Tutaj sprawdzamy jedynie wiązanie z poprzednią resztą,
            // jeśli są one rzeczywiście kolejne w sekwencji.
            // 5. Poprzednia reszta -> zmieniona reszta
            if (index > 0)
            {
                var previous = residues[index - 1];
                var current = residues[index];

                if (current.Id <= previous.Id + 1)
                {
                    CheckPeptideBond(chain, residues[index - 1], residues[index], report);
                }
            }

            // 6. Zmieniona reszta -> następna reszta
            if (index < residues.Count - 1)
            {
                var current = residues[index];
                var next = residues[index + 1];

                if (next.Id <= current.Id + 1)
                {
                    CheckPeptideBond(chain, residues[index], residues[index + 1], report);
                }
            }
        }
    }
}
