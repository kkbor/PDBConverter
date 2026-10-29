using PDBLibrary.Models;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PDBLibrary.Parsers
{
    /// <summary>
    /// Zapisuje model Protein z powrotem do pliku .pdb, w ukladzie kolumnowym
    /// DOKLADNIE zgodnym z tym, co odczytuje PdbParsers (te same indeksy
    /// Substring), zeby zapisany plik dalo sie bez strat wczytac ponownie -
    /// ORAZ zgodnym ze specyfikacja PDB na tyle, zeby otworzyl sie poprawnie
    /// w innych programach (PyMOL, Chimera/ChimeraX, VMD, Coot, MolProbity...).
    ///
    /// Kolumny (1-indeksowane, zgodnie ze specyfikacja PDB):
    ///   1-6   "ATOM  " albo "HETATM"
    ///   7-11  numer seryjny atomu
    ///   13-16 nazwa atomu
    ///   18-20 nazwa reszty
    ///   22    identyfikator lancucha
    ///   23-26 numer reszty
    ///   27    kod wstawienia (insertion code)
    ///   31-38 / 39-46 / 47-54   X / Y / Z  (%8.3f)
    ///   55-60 occupancy (%6.2f)
    ///   61-66 b-factor (%6.2f)
    ///   77-78 symbol pierwiastka
    /// </summary>
    public static class PdbWriter
    {
        public static void Save(Protein protein, string path)
        {
            using var writer = new StreamWriter(path, false, Encoding.ASCII);

            // Numery seryjne atomow sa NADAWANE NA NOWO, sekwencyjnie, przy kazdym
            // zapisie - nie polegamy na atom.Id z pliku wejsciowego. Jest to konieczne,
            // bo po mutacji (SideChainBuilder dokleja nowe atomy, numerujac je lokalnie
            // w obrebie reszty) stare numery moglyby sie powielac miedzy roznymi resztami.
            int serial = 1;

            foreach (var chain in protein.Chains)
            {
                // KLUCZOWE dla poprawnego otwarcia w innych programach: standardowe
                // aminokwasy musza isc jako ATOM, a wszystko inne (woda HOH, jony
                // NA/ZN/..., ligandy GOL/FAD/4MV itd.) jako HETATM - inaczej np.
                // PyMOL/Rosetta/MolProbity moga probowac traktowac czasteczke wody
                // albo jon jako czesc lancucha polimeru i zle policzyc polaczenia
                // czy walidacje geometrii. Zgodnie z konwencja PDB najpierw
                // zapisujemy caly polimer (ATOM) danego lancucha, potem TER, a
                // dopiero potem jego heteroatomy (HETATM).
                var polymerResidues = chain.Residues
                    .Where(r => AminoAcidCodes.IsStandardAminoAcid(r.Name))
                    .OrderBy(r => r.Id)
                    .ThenBy(r => r.InsertionCode)
                    .ToList();

                var heteroResidues = chain.Residues
                    .Where(r => !AminoAcidCodes.IsStandardAminoAcid(r.Name))
                    .OrderBy(r => r.Id)
                    .ThenBy(r => r.InsertionCode)
                    .ToList();

                int lastPolymerSerial = serial;

                foreach (var residue in polymerResidues)
                {
                    foreach (var atom in residue.Atoms)
                    {
                        writer.WriteLine(FormatAtomLine("ATOM  ", atom, serial, chain.Id, residue));
                        lastPolymerSerial = serial;
                        serial++;
                    }
                }

                // TER jest opcjonalny dla naszego wlasnego parsera (czyta tylko
                // ATOM/HETATM), ale dobra praktyka dla zgodnosci z innymi narzedziami -
                // oznacza koniec lancucha polimeru. Piszemy go TYLKO gdy w tym
                // lancuchu faktycznie byl jakis polimer (sam HETATM, np. czysty
                // lancuch wody, nie dostaje TER).
                if (polymerResidues.Count > 0)
                    writer.WriteLine(FormatTerLine(lastPolymerSerial + 1, chain.Id, polymerResidues[^1]));

                foreach (var residue in heteroResidues)
                {
                    foreach (var atom in residue.Atoms)
                    {
                        writer.WriteLine(FormatAtomLine("HETATM", atom, serial, chain.Id, residue));
                        serial++;
                    }
                }
            }

            writer.WriteLine("END");
        }

        private static string FormatAtomLine(string recordName, Atoms atom, int serial, char chainId, Residues residue)
        {
            var sb = new StringBuilder();

            sb.Append(recordName);                                        // 1-6 ("ATOM  " / "HETATM")
            sb.Append(serial.ToString(CultureInfo.InvariantCulture).PadLeft(5)); // 7-11
            sb.Append(' ');                                              // 12
            sb.Append(FormatAtomName(atom.Name));                        // 13-16
            sb.Append(' ');                                              // 17 (altLoc)
            sb.Append(FormatResidueName(residue.Name));                  // 18-20
            sb.Append(' ');                                              // 21
            sb.Append(chainId);                                          // 22
            sb.Append(residue.Id.ToString(CultureInfo.InvariantCulture).PadLeft(4)); // 23-26
            sb.Append(FormatInsertionCode(residue.InsertionCode));       // 27
            sb.Append("   ");                                            // 28-30 (blank)
            sb.Append(FormatCoord(atom.X));                              // 31-38
            sb.Append(FormatCoord(atom.Y));                              // 39-46
            sb.Append(FormatCoord(atom.Z));                              // 47-54
            sb.Append(FormatTwoDecimal(atom.Occupancy));                 // 55-60
            sb.Append(FormatTwoDecimal(atom.BFactor));                   // 61-66
            sb.Append(new string(' ', 10));                              // 67-76
            sb.Append(FormatElement(atom.Element));                      // 77-78

            return sb.ToString();
        }

        private static string FormatTerLine(int serial, char chainId, Residues lastResidue)
        {
            var sb = new StringBuilder();
            sb.Append("TER   ");
            sb.Append(serial.ToString(CultureInfo.InvariantCulture).PadLeft(5));
            sb.Append("      ");
            sb.Append(FormatResidueName(lastResidue.Name));
            sb.Append(' ');
            sb.Append(chainId);
            sb.Append(lastResidue.Id.ToString(CultureInfo.InvariantCulture).PadLeft(4));
            sb.Append(FormatInsertionCode(lastResidue.InsertionCode));
            return sb.ToString();
        }

        // Puste miejsce (nie '\0') gdy reszta nie ma kodu wstawienia - '\0'
        // wpisany doslownie do pliku tekstowego wygladalby jak uszkodzony plik
        // w niejednym parserze.
        private static char FormatInsertionCode(char insertionCode)
            => insertionCode == '\0' ? ' ' : insertionCode;

        private static string FormatAtomName(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length >= 4)
                return name.Substring(0, 4);

            // konwencja PDB: nazwy krotsze niz 4 znaki maja wiodaca spacje,
            // np. " CA ", " N  ", " OXT" (ten akurat ma dokladnie 4 znaki z ta spacja)
            return (" " + name).PadRight(4);
        }

        private static string FormatResidueName(string name)
        {
            name = (name ?? string.Empty).Trim();
            return name.PadRight(3).Substring(0, 3);
        }

        private static string FormatElement(string? element)
        {
            string value = string.IsNullOrWhiteSpace(element) ? string.Empty : element.Trim();
            return value.PadLeft(2).Substring(0, 2);
        }

        // UWAGA: zawsze CultureInfo.InvariantCulture - inaczej na np. polskim
        // Windows ToString("F3") zwrocilby przecinek zamiast kropki i zepsul format pliku
        private static string FormatCoord(double value)
            => value.ToString("F3", CultureInfo.InvariantCulture).PadLeft(8);

        private static string FormatTwoDecimal(double value)
            => value.ToString("F2", CultureInfo.InvariantCulture).PadLeft(6);
    }
}
