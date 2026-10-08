using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PDBLibrary.Parsers
{
    /// <summary>
    /// Wykrywa wiazania miedzy atomami na podstawie odleglosci przestrzennej
    /// (promienie kowalencyjne + tolerancja). Dziala w obrebie kazdego residuum,
    /// miedzy sasiednimi residuami w tym samym lancuchu (wiazanie peptydowe C-N
    /// dla bialek, fosfodiestrowe O3'-P dla kwasow nukleinowych RNA/DNA),
    /// globalnie dla wiazan disulfidowych S-S, dla znanych wiazan miedzy-resztowych
    /// (np. zasada Schiffa LYS-PLP) oraz globalnie dla wiazan koordynacyjnych
    /// z jonami metali (ZN, NA, MG, CA, FE, itd.).
    /// </summary>
    public static class BondDetector
    {
        // Przyblizone promienie kowalencyjne (w Angstromach)
        private static readonly Dictionary<string, double> CovalentRadii = new()
        {
            { "H", 0.31 }, { "C", 0.76 }, { "N", 0.71 }, { "O", 0.66 },
            { "S", 1.05 }, { "P", 1.07 }, { "F", 0.57 }, { "CL", 0.99 },
            { "BR", 1.20 }, { "I", 1.39 }, { "FE", 1.32 }, { "ZN", 1.22 },
            { "CA", 1.76 }, { "MG", 1.41 }, { "NA", 1.66 }, { "K", 2.03 }
        };

        private const double Tolerance = 0.45; // margines tolerancji w A
        private const double DefaultRadius = 0.77;

        // Jony metali, dla ktorych sprawdzamy wiazania KOORDYNACYJNE (dative) -
        // te maja zupelnie inna, dluzsza "normalna" odleglosc niz zwykle wiazania
        // kowalencyjne, wiec licza sie osobnym progiem (MetalCoordinationDistance),
        // a nie przez CovalentRadii+Tolerance jak reszta.
        //
        // UWAGA: "CA" tutaj oznacza PIERWIASTEK wapn (element chemiczny Ca), a NIE
        // nazwe atomu alfa-wegla (ktory tez nazywa sie "CA", ale to zupelnie inne
        // pole - Atoms.Element, nie Atoms.Name). Nie pomyl tych dwoch rzeczy.
        private static readonly HashSet<string> MetalIons =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "ZN", "NA", "MG", "FE", "MN", "CU", "CO", "NI", "K", "CA"
            };

        // Atomy, ktore typowo koordynuja jony metali (tlen, azot, siarka, chlor)
        private static readonly HashSet<string> CoordinatingElements =
            new(StringComparer.OrdinalIgnoreCase) { "O", "N", "S", "CL" };

        // Hojny, jeden wspolny prog dla wszystkich par metal-ligand (typowe
        // odleglosci koordynacyjne to ~1.8-2.6 A w zaleznosci od pary atomow;
        // to jest "podstawowa" walidacja, nie precyzyjna chemia koordynacyjna)
        private const double MetalCoordinationDistance = 2.8;

        public static List<Bonds> DetectBonds(Protein protein)
        {
            var bonds = new List<Bonds>();

            foreach (var chain in protein.Chains)
            {
                // ThenBy(InsertionCode) jest konieczne: skoro "23" i "23A" to teraz
                // osobne obiekty Residues o tym samym Id, samo OrderBy(r => r.Id)
                // nie gwarantuje poprawnej kolejnosci biologicznej miedzy nimi
                // (spacja < litery w porownaniu char, wiec "23" trafi przed "23A" poprawnie)
                var residues = chain.Residues
                    .OrderBy(r => r.Id)
                    .ThenBy(r => r.InsertionCode)
                    .ToList();

                for (int i = 0; i < residues.Count; i++)
                {
                    var residue = residues[i];

                    // 1) Wiazania wewnatrz residuum (wszystkie pary atomow)
                    for (int a = 0; a < residue.Atoms.Count; a++)
                    {
                        for (int b = a + 1; b < residue.Atoms.Count; b++)
                        {
                            if (IsBonded(residue.Atoms[a], residue.Atoms[b]))
                                bonds.Add(new Bonds(residue.Atoms[a], residue.Atoms[b]));
                        }
                    }

                    // 2) Wiazanie z kolejnym residuum: peptydowe C(i)-N(i+1) dla bialek
                    //    LUB fosfodiestrowe O3'(i)-P(i+1) dla kwasow nukleinowych
                    //    (RNA/DNA). Sprawdzamy OBA - kazda reszta jest albo
                    //    aminokwasem albo nukleotydem, wiec tylko jeden z warunkow
                    //    kiedykolwiek faktycznie cos znajdzie.
                    if (i + 1 < residues.Count)
                    {
                        var next = residues[i + 1];

                        var atomC = residue.Atoms.FirstOrDefault(x => x.Name == "C");
                        var atomN = next.Atoms.FirstOrDefault(x => x.Name == "N");

                        if (atomC != null && atomN != null && IsBonded(atomC, atomN))
                            bonds.Add(new Bonds(atomC, atomN));

                        // O3' bywa zapisywane jako "O3'" (nowszy PDB) albo "O3*"
                        // (starszy zapis) - sprawdzamy oba warianty
                        var atomO3 = residue.Atoms.FirstOrDefault(
                            x => x.Name == "O3'" || x.Name == "O3*");
                        var atomP = next.Atoms.FirstOrDefault(x => x.Name == "P");

                        if (atomO3 != null && atomP != null && IsBonded(atomO3, atomP))
                            bonds.Add(new Bonds(atomO3, atomP));
                    }
                }
            }

            // Wiazania disulfidowe (S-S) moga laczyc reszty CYS dowolnie odlegle
            // w sekwencji (a nawet w roznych lancuchach) - powyzsza petla sprawdza
            // tylko sasiadujace reszty, wiec je pomija. Atomow siarki jest w typowym
            // bialku bardzo malo (tylko CYS/MET), wiec sprawdzenie WSZYSTKICH par
            // atomow siarki w calej strukturze jest tanie (male k, O(k^2) nieistotne).
            DetectDisulfideBonds(protein, bonds);
            DetectKnownCrossResidueBonds(protein, bonds);
            DetectMetalCoordinationBonds(protein, bonds);

            return bonds;
        }

        // Jony metali (ZN, NA, MG, CA jako pierwiastek, FE, itd.) tworza wiazania
        // koordynacyjne z pobliskimi atomami O/N/S/Cl - czesto z bocznych lancuchow
        // bialka (Cys, His, Asp, Glu), ale czasem tez z ligandow (np. woda, GOL).
        // Bez tego kazdy jon metalu wygladalby jak seria "zderzen" z otoczeniem,
        // dokladnie tak samo jak mostki S-S przed naprawa.
        private static void DetectMetalCoordinationBonds(Protein protein, List<Bonds> bonds)
        {
            var allAtoms = protein.Chains
                .SelectMany(c => c.Residues)
                .SelectMany(r => r.Atoms)
                .ToList();

            var metalAtoms = allAtoms
                .Where(a => MetalIons.Contains((a.Element ?? "").Trim()))
                .ToList();

            if (metalAtoms.Count == 0)
                return;

            foreach (var metal in metalAtoms)
            {
                foreach (var other in allAtoms)
                {
                    if (ReferenceEquals(metal, other))
                        continue;

                    if (!CoordinatingElements.Contains((other.Element ?? "").Trim()))
                        continue;

                    double dist = Distance(metal, other);

                    if (dist > 0.1 && dist <= MetalCoordinationDistance)
                        bonds.Add(new Bonds(metal, other));
                }
            }
        }

        private static void DetectKnownCrossResidueBonds(Protein protein, List<Bonds> bonds)
        {
            foreach (var chain in protein.Chains)
            {
                foreach (var residue in chain.Residues)
                {
                    // Lizyna
                    if (!residue.Name.Equals(
                            "LYS",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var nz = residue.Atoms.FirstOrDefault(
                        a => a.Name.Equals(
                            "NZ",
                            StringComparison.OrdinalIgnoreCase));

                    if (nz == null)
                        continue;

                    // Szukamy PLP w całej strukturze
                    foreach (var plpResidue in protein.Chains
                        .SelectMany(c => c.Residues)
                        .Where(r => r.Name.Equals(
                            "PLP",
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        var c4a = plpResidue.Atoms.FirstOrDefault(
                            a => a.Name.Equals(
                                "C4A",
                                StringComparison.OrdinalIgnoreCase));

                        if (c4a == null)
                            continue;

                        if (IsBonded(nz, c4a))
                        {
                            bonds.Add(new Bonds(nz, c4a));
                        }
                    }
                }
            }
        }

        private static void DetectDisulfideBonds(Protein protein, List<Bonds> bonds)
        {
            var sulfurAtoms = protein.Chains
                .SelectMany(c => c.Residues)
                .SelectMany(r => r.Atoms)
                .Where(a => string.Equals(a.Element?.Trim(), "S", StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < sulfurAtoms.Count; i++)
            {
                for (int j = i + 1; j < sulfurAtoms.Count; j++)
                {
                    var a = sulfurAtoms[i];
                    var b = sulfurAtoms[j];

                    // atomy siarki z tej samej reszty juz obsluzone przez petle
                    // wewnatrz-resztowa powyzej - pomijamy, zeby nie dodac duplikatu
                    if (a.ChainId == b.ChainId && a.ResidueId == b.ResidueId)
                        continue;

                    if (IsBonded(a, b))
                        bonds.Add(new Bonds(a, b));
                }
            }
        }

        private static bool IsBonded(Atoms a1, Atoms a2)
        {
            double dist = Distance(a1, a2);
            double r1 = GetRadius(a1.Element);
            double r2 = GetRadius(a2.Element);
            double maxDist = r1 + r2 + Tolerance;

            // odrzucamy zerowa/ujemna odleglosc (te same wspolrzedne / altloc duplikaty)
            return dist > 0.1 && dist <= maxDist;
        }

        private static double GetRadius(string element)
        {
            if (string.IsNullOrWhiteSpace(element))
                return DefaultRadius;

            return CovalentRadii.TryGetValue(element.ToUpperInvariant(), out double r)
                ? r
                : DefaultRadius;
        }

        private static double Distance(Atoms a, Atoms b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
