using PDBLibrary.Models;
using PDBLibrary.Parsers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PDBLibrary.Validation
{
    /// <summary>
    /// Wykrywa konflikty przestrzenne (steric clashes): pary atomow, ktore NIE sa
    /// ze soba powiazane wiazaniem (i nie naleza do tej samej reszty), a mimo to
    /// sa blizej siebie niz pozwala na to suma ich promieni van der Waalsa.
    ///
    /// Promienie van der Waalsa (Bondi, 1964) - standardowe, powszechnie
    /// tabelaryzowane wartosci, bezpieczne do uzycia.
    ///
    /// Wydajnosc: zamiast naiwnego sprawdzenia WSZYSTKICH par atomow (O(n^2),
    /// nie do przyjecia dla duzych struktur), atomy sa najpierw przypisywane do
    /// komorek siatki przestrzennej (spatial hashing) o rozmiarze wiekszym niz
    /// najwieksza mozliwa suma promieni - sprawdzamy tylko atomy w sasiadujacych
    /// komorkach, co w praktyce redukuje liczbe porownan o rzedy wielkosci.
    /// </summary>
    public static class ClashDetector
    {
        private static readonly Dictionary<string, double> VdwRadii =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["H"] = 1.20,
                ["C"] = 1.70,
                ["N"] = 1.55,
                ["O"] = 1.52,
                ["S"] = 1.80,
                ["P"] = 1.80,
                ["F"] = 1.47,
                ["CL"] = 1.75,
                ["BR"] = 1.85,
                ["I"] = 1.98,
                // jony metali - promienie efektywne (jonowe) sa znaczaco mniejsze
                // niz elementarny promien van der Waalsa, bo to naladowane kationy
                // w wiazaniu koordynacyjnym, nie neutralne atomy w kontakcie vdW
                ["ZN"] = 0.88,
                ["NA"] = 1.02,
                ["MG"] = 0.86,
                ["FE"] = 0.78,
                ["MN"] = 0.97,
                ["CU"] = 0.91,
                ["CO"] = 0.89,
                ["NI"] = 0.83,
                ["K"] = 1.51,
                ["CA"] = 1.14,
                ["XE"] = 2.16 // ksenon - gaz szlachetny czasem uzywany w fazowaniu krystalograficznym
            };

        private const double DefaultVdwRadius = 1.70;

        // ile "nachodzenia" tolerujemy, zanim uznamy pare za konflikt (podobna
        // konwencja do np. MolProbity clashscore - niewielkie nachodzenie jest
        // normalne/czeste nawet w poprawnych strukturach eksperymentalnych)
        private const double OverlapTolerance = 0.4;

        // overlap powyzej tego progu traktujemy jako powazne starcie (Error),
        // mniejsze jako ostrzezenie (Warning)
        private const double SevereOverlapThreshold = 1.0;


        private const double CellSize = 5.0; // > najwieksza mozliwa suma promieni + tolerancja

        public static void Validate(Protein protein, ValidationReport report)
        {
            var allAtoms = protein.Chains
                .SelectMany(c => c.Residues)
                .SelectMany(r => r.Atoms)
                .ToList();

            if (allAtoms.Count == 0)
                return;

            var atomToResidue = BuildAtomToResidueMap(protein);
            var adjacency = BuildAdjacency(protein);

            var grid = BuildSpatialGrid(allAtoms, out var atomIndex);

            var reportedPairs = new HashSet<(int, int)>();

            foreach (var atom in allAtoms)
            {
                var cell = CellOf(atom);

                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            var neighborCell = (cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz);
                            if (!grid.TryGetValue(neighborCell, out var neighbors))
                                continue;

                            foreach (var other in neighbors)
                            {
                                if (ReferenceEquals(atom, other))
                                    continue;

                                int indexA = atomIndex[atom];
                                int indexB = atomIndex[other];
                                if (indexA >= indexB)
                                    continue; // kazda para rozwazamy dokladnie raz

                                var pairKey = (indexA, indexB);
                                if (!reportedPairs.Add(pairKey))
                                    continue;

                                CheckPair(atom, other, atomToResidue, adjacency, report);
                            }
                        }
            }
        }
        private static bool IsHydrogen(Atoms atom)
        {
            return string.Equals(
                atom.Element?.Trim(),
                "H",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWater(Residues residue)
        {
            return string.Equals(
                residue.Name?.Trim(),
                "HOH",
                StringComparison.OrdinalIgnoreCase);
        }
        private static void CheckPair(
            Atoms a, Atoms b,
            Dictionary<Atoms, Residues> atomToResidue,
            Dictionary<Atoms, HashSet<Atoms>> adjacency,
            ValidationReport report)
        {
            // Pobieramy reszty, do których należą oba atomy.
            if (!atomToResidue.TryGetValue(a, out var residueA) ||
                !atomToResidue.TryGetValue(b, out var residueB))
            {
                return;
            }

            // Atomy należące do tej samej reszty nie są tutaj traktowane
            // jako konflikt przestrzenny.
            if (ReferenceEquals(residueA, residueB))
            {
                return;
            }

            // Na tym etapie sprawdzamy clash'e tylko między atomami ciężkimi.
            // Wodory mogą brać udział m.in. w wiązaniach wodorowych,
            // dlatego pomijamy je w podstawowej walidacji sterycznej.
            if (IsHydrogen(a) || IsHydrogen(b))
            {
                return;
            }

            // Pomijamy kontakty z cząsteczkami wody.
            // HOH można później analizować osobno jako kontakty białko-woda.
            if (IsWater(residueA) || IsWater(residueB))
            {
                return;
            }
            // Pomijamy pary atomów oddalone w grafie wiązań
            // o maksymalnie 3 wiązania:
            //
            // 1-2: A-B
            // 1-3: A-B-C
            // 1-4: A-B-C-D
            //
            // Takie kontakty wynikają z lokalnej geometrii cząsteczki
            // i nie powinny być traktowane jako niezależne clash'e steryczne.
            if (IsWithinBondDistance(a, b, adjacency, 3))
            {
                return;
            }


            double distance = Distance(a, b);

            double vdwSum =
                GetVdwRadius(a.Element) +
                GetVdwRadius(b.Element);

            double overlap = vdwSum - distance;

            // Brak istotnego nachodzenia promieni van der Waalsa.
            if (overlap <= OverlapTolerance)
            {
                return;
            }

            var severity = overlap > SevereOverlapThreshold
                ? ValidationSeverity.Error
                : ValidationSeverity.Warning;

            string residueDescA = $"{residueA.Name} {residueA.Id}";
            string residueDescB = $"{residueB.Name} {residueB.Id}";

            report.Issues.Add(new ValidationIssue
            {
                Severity = severity,

                Message =
                    $"Konflikt przestrzenny: atom {a.Name} ({residueDescA}) " +
                    $"i atom {b.Name} ({residueDescB}) są w odległości " +
                    $"{distance:F2} Å " +
                    $"(nachodzenie {overlap:F2} Å ponad sumę promieni van der Waalsa).",

                Residue = residueA,
                ResidueB = residueB
            });
        }

        private static Dictionary<(int, int, int), List<Atoms>> BuildSpatialGrid(
            List<Atoms> atoms, out Dictionary<Atoms, int> atomIndex)
        {
            var grid = new Dictionary<(int, int, int), List<Atoms>>();
            atomIndex = new Dictionary<Atoms, int>();

            for (int i = 0; i < atoms.Count; i++)
            {
                var atom = atoms[i];
                atomIndex[atom] = i;

                var cell = CellOf(atom);
                if (!grid.TryGetValue(cell, out var list))
                {
                    list = new List<Atoms>();
                    grid[cell] = list;
                }
                list.Add(atom);
            }

            return grid;
        }

        private static (int, int, int) CellOf(Atoms atom) => (
            (int)Math.Floor(atom.X / CellSize),
            (int)Math.Floor(atom.Y / CellSize),
            (int)Math.Floor(atom.Z / CellSize));

        private static Dictionary<Atoms, Residues> BuildAtomToResidueMap(Protein protein)
        {
            var map = new Dictionary<Atoms, Residues>();
            foreach (var chain in protein.Chains)
                foreach (var residue in chain.Residues)
                    foreach (var atom in residue.Atoms)
                        map[atom] = residue;
            return map;
        }

        // Lista sasiedztwa (graf polaczen) budowana raz z wykrytych wiazan -
        // potrzebna do sprawdzenia nie tylko bezposrednich polaczen (1-2),
        // ale i par majacych wspolnego sasiada (1-3).
        private static Dictionary<Atoms, HashSet<Atoms>> BuildAdjacency(Protein protein)
        {
            var bonds = BondDetector.DetectBonds(protein);
            var adjacency = new Dictionary<Atoms, HashSet<Atoms>>();

            void AddEdge(Atoms from, Atoms to)
            {
                if (!adjacency.TryGetValue(from, out var set))
                {
                    set = new HashSet<Atoms>();
                    adjacency[from] = set;
                }
                set.Add(to);
            }

            foreach (var bond in bonds)
            {
                AddEdge(bond.Atom1, bond.Atom2);
                AddEdge(bond.Atom2, bond.Atom1);
            }

            return adjacency;
        }

        private static bool IsWithinBondDistance(
            Atoms start,
            Atoms target,
            Dictionary<Atoms, HashSet<Atoms>> adjacency,
            int maxBonds)
                {
                    if (ReferenceEquals(start, target))
                        return true;

                    var visited = new HashSet<Atoms>
            {
                start
            };

            var queue = new Queue<(Atoms Atom, int Distance)>();

            queue.Enqueue((start, 0));

            while (queue.Count > 0)
            {
                var (current, distance) = queue.Dequeue();

                if (distance >= maxBonds)
                    continue;

                if (!adjacency.TryGetValue(current, out var neighbours))
                    continue;

                foreach (var neighbour in neighbours)
                {
                    if (ReferenceEquals(neighbour, target))
                        return true;

                    if (visited.Add(neighbour))
                    {
                        queue.Enqueue(
                            (neighbour, distance + 1));
                    }
                }
            }

            return false;
        }

        private static double GetVdwRadius(string? element) =>
            !string.IsNullOrWhiteSpace(element) && VdwRadii.TryGetValue(element.Trim(), out double r)
                ? r
                : DefaultVdwRadius;

        private static double Distance(Atoms a, Atoms b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        public static void ValidateResidue(Protein protein,Residues changedResidue,ValidationReport report)
        {
            var allAtoms = protein.Chains
                .SelectMany(c => c.Residues)
                .SelectMany(r => r.Atoms)
                .ToList();

            var changedAtoms = changedResidue.Atoms.ToList();

            if (changedAtoms.Count == 0)
                return;

            // Mapa atomów -> reszty
            var atomToResidue = BuildAtomToResidueMap(protein);

            // Aktualne wiązania po mutacji
            var adjacency = BuildAdjacency(protein);

            // Spatial hashing dla całej struktury
            var grid = BuildSpatialGrid(allAtoms, out var atomIndex);

            // Zapamiętujemy już sprawdzone pary atomów
            var reportedPairs = new HashSet<(int, int)>();

            // Indeksy atomów potrzebne do jednoznacznego identyfikowania par
            var atomIndexes = allAtoms
                .Select((atom, index) => new { atom, index })
                .ToDictionary(x => x.atom, x => x.index);

            foreach (var changedAtom in changedAtoms)
            {
                if (!atomIndexes.TryGetValue(changedAtom, out int changedIndex))
                    continue;

                var cell = CellOf(changedAtom);

                // Sprawdzamy komórkę atomu oraz 26 sąsiednich komórek
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            var neighborCell = (cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz);

                            if (!grid.TryGetValue(neighborCell, out var nearbyAtoms))
                                continue;

                            foreach (var otherAtom in nearbyAtoms)
                            {
                                if (ReferenceEquals(changedAtom, otherAtom))
                                    continue;

                                if (!atomIndexes.TryGetValue(otherAtom, out int otherIndex))
                                    continue;

                                // Para jest zapisywana zawsze w tej samej kolejności
                                var pair = changedIndex < otherIndex
                                    ? (changedIndex, otherIndex)
                                    : (otherIndex, changedIndex);

                                if (!reportedPairs.Add(pair))
                                    continue;

                                CheckPair(
                                    changedAtom,
                                    otherAtom,
                                    atomToResidue,
                                    adjacency,
                                    report);
                            }
                        }
                    }
                }
            }
        }
    }
}
