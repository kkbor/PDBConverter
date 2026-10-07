using PDBLibrary.Geometry;
using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Sevices
{
    /// <summary>
    /// Dokleja atomy lancucha bocznego do reszty na podstawie szablonu (AminoAcidTemplate),
    /// nakladajac uklad odniesienia szablonu (zbudowany z N-CA-C) na rzeczywisty
    /// szkielet tej konkretnej reszty w strukturze.
    ///
    /// Zasada dziala tak:
    ///  1) Buduje sie ortonormalny uklad wspolrzednych z trojki atomow N, CA, C -
    ///     zarowno dla szablonu, jak i dla reszty docelowej.
    ///  2) Macierz obrotu laczaca oba uklady liczy sie jako: R = ramkaDocelowa * (ramkaSzablonu)^T
    ///     (obie ramki sa ortonormalne, wiec transpozycja = odwrotnosc - bez kosztownej inwersji).
    ///  3) Kazdy atom lancucha bocznego z szablonu jest obracany ta sama macierza R
    ///     i przesuwany wzgledem rzeczywistego CA.
    ///
    /// To jest geometrycznie DOKLADNE przeniesienie wyidealizowanej geometrii na
    /// aktualny szkielet (zaden blad dopasowania) - jedyna "niedokladnosc" to to,
    /// ze sama geometria szablonu jest jedna, uśredniona/kanoniczna konformacja,
    /// a nie statystycznie najlepszy rotamer dla danego kontekstu (to jest juz
    /// domena prawdziwej biblioteki rotamerow, np. Dunbrack).
    /// </summary>
    public static class SideChainBuilder
    {
        public static bool TryBuildSideChain(Residues residue, AminoAcidTemplate template)
        {
            var n = FindAtom(residue, "N");
            var ca = FindAtom(residue, "CA");
            var c = FindAtom(residue, "C");

            if (n == null || ca == null || c == null)
                return false; // brak kompletnego szkieletu - nie ma na czym oprzec obrotu

            var actualFrame = BuildFrame(ToVec(n), ToVec(ca), ToVec(c));
            var templateFrame = BuildFrame(template.N, template.CA, template.C);

            var rotation = actualFrame * templateFrame.Transposed();
            var actualCA = ToVec(ca);

            int nextId = residue.Atoms.Count == 0 ? 1 : residue.Atoms.Max(a => a.Id) + 1;

            foreach (var templateAtom in template.SideChainAtoms)
            {
                var localOffset = templateAtom.Position - template.CA;
                var rotatedOffset = rotation * localOffset;
                var worldPos = rotatedOffset + actualCA;

                residue.Atoms.Add(new Atoms
                {
                    Id = nextId++,
                    Name = templateAtom.Name,
                    ResidueName = residue.Name,
                    ChainId = ca.ChainId,
                    ResidueId = residue.Id,
                    X = worldPos.X,
                    Y = worldPos.Y,
                    Z = worldPos.Z,
                    Occupancy = 1.0,
                    BFactor = 20.0, // umowna wartosc dla "sztucznie" dobudowanych atomow
                    Element = templateAtom.Element
                });
            }

            return true;
        }

        private static Atoms? FindAtom(Residues residue, string name) =>
            residue.Atoms.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private static Vec3 ToVec(Atoms atom) => new(atom.X, atom.Y, atom.Z);

        private static Mat3 BuildFrame(Vec3 n, Vec3 ca, Vec3 c)
        {
            var e1 = (c - ca).Normalized();              // os w strone C
            var towardN = n - ca;
            var e3 = Vec3.Cross(towardN, e1).Normalized(); // prostopadla do plaszczyzny N-CA-C
            var e2 = Vec3.Cross(e3, e1);                   // dopelnia uklad do ortonormalnego
            return new Mat3(e1, e2, e3);
        }
    }
}
