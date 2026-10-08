using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Parsers
{
    /// <summary>Parser klasycznego formatu PDB (stale-szerokosciowe kolumny).</summary>
    public class PdbParsers
    {
        public Protein Load(string path)
        {
            Protein protein = new Protein();

            int alternateLocationCount = 0;

            foreach (string line in File.ReadLines(path))
            {
                if (!(line.StartsWith("ATOM") || line.StartsWith("HETATM")))
                    continue;

                // Kolumna 17 - alternate location indicator.
                char altLoc = line.Length > 16 ? line[16] : ' ';

                if (altLoc != ' ')
                {
                    alternateLocationCount++;
                }
                // Nie ładujemy jednocześnie kilku alternatywnych
                // konformacji tego samego atomu.
                //
                // Zachowujemy brak altLoc oraz konformację A.
                if (altLoc != ' ' && altLoc != 'A')
                {
                    continue;
                }
                // Kolumna 27 - insertion code.
                char insertionCode = line.Length > 26 ? line[26] : ' ';

                Atoms atom = new Atoms
                {
                    Id = int.Parse(line.Substring(6, 5).Trim()),

                    Name = line.Substring(12, 4).Trim(),

                    ResidueName = line.Substring(17, 3).Trim(),

                    ChainId = line[21],

                    ResidueId = int.Parse(
                        line.Substring(22, 4).Trim()),

                    InsertionCode = insertionCode,

                    X = double.Parse(
                        line.Substring(30, 8).Trim(),
                        CultureInfo.InvariantCulture),

                    Y = double.Parse(
                        line.Substring(38, 8).Trim(),
                        CultureInfo.InvariantCulture),

                    Z = double.Parse(
                        line.Substring(46, 8).Trim(),
                        CultureInfo.InvariantCulture),

                    Occupancy = double.Parse(
                        line.Substring(54, 6).Trim(),
                        CultureInfo.InvariantCulture),

                    BFactor = double.Parse(
                        line.Substring(60, 6).Trim(),
                        CultureInfo.InvariantCulture),

                    Element = line.Length >= 78
                        ? line.Substring(76, 2).Trim()
                        : string.Empty
                };

                ProteinBuilder.AddAtom(protein, atom);
            }

            Console.WriteLine(
                $"{Path.GetFileName(path)}: atomy z altLoc = {alternateLocationCount}");

            return protein;
        }
    }
}
