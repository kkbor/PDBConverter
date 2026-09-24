using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Parsers
{
    /// <summary>
    /// Wykrywa wiazania miedzy atomami na podstawie odleglosci przestrzennej
    /// (promienie kowalencyjne + tolerancja). Dziala w obrebie kazdego residuum
    /// oraz miedzy sasiednimi residuami w tym samym lancuchu (wiazanie peptydowe C-N).
    /// </summary>
    public class BondDetector
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

        public static List<Bonds> DetectBonds(Protein protein)
        {
            var bonds = new List<Bonds>();

            foreach (var chain in protein.Chains)
            {
                var residues = chain.Residues.OrderBy(r => r.Id).ToList();

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

                    // 2) Wiazanie peptydowe C(i) - N(i+1) z kolejnym residuum
                    if (i + 1 < residues.Count)
                    {
                        var next = residues[i + 1];
                        var atomC = residue.Atoms.FirstOrDefault(x => x.Name == "C");
                        var atomN = next.Atoms.FirstOrDefault(x => x.Name == "N");

                        if (atomC != null && atomN != null && IsBonded(atomC, atomN))
                            bonds.Add(new Bonds(atomC, atomN));
                    }
                }
            }

            return bonds;
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
