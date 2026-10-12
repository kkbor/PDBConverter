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
                // HELIX
                if (line.StartsWith("HELIX"))
                {
                    ParseHelix(line, protein);
                    continue;
                }

                // SHEET
                if (line.StartsWith("SHEET"))
                {
                    ParseSheet(line, protein);
                    continue;
                }
                // ATOM / HETATM
                if (!(line.StartsWith("ATOM") ||
                      line.StartsWith("HETATM")))
                {
                    continue;
                }

                char altLoc = line.Length > 16
                        ? line[16]
                        : ' ';

                if (altLoc != ' ')
                {
                    alternateLocationCount++;
                }

                // Zachowujemy brak altLoc oraz pierwszą konformację A.
                if (altLoc != ' ' && altLoc != 'A')
                {
                    continue;
                }

                char insertionCode = line.Length > 26
                        ? line[26]
                        : ' ';

                Atoms atom = new Atoms
                {
                    Id = int.Parse(line.Substring(6, 5).Trim()),

                    Name = line.Substring(12, 4).Trim(),

                    ResidueName = line.Substring(17, 3).Trim(),

                    ChainId = line.Length > 21
                            ? line[21]
                            : 'A',

                    ResidueId = int.Parse(line.Substring(22, 4).Trim()),

                    InsertionCode = insertionCode,

                    X = double.Parse(line.Substring(30, 8).Trim(), CultureInfo.InvariantCulture),

                    Y = double.Parse(line.Substring(38, 8).Trim(), CultureInfo.InvariantCulture),

                    Z = double.Parse(line.Substring(46, 8).Trim(), CultureInfo.InvariantCulture),

                    Occupancy = double.Parse(line.Substring(54, 6).Trim(), CultureInfo.InvariantCulture),

                    BFactor = double.Parse(line.Substring(60, 6).Trim(), CultureInfo.InvariantCulture),

                    Element = line.Length >= 78
                            ? line.Substring(76, 2).Trim()
                            : string.Empty
                };

                ProteinBuilder.AddAtom(protein, atom);
            }

            Console.WriteLine(
                $"{Path.GetFileName(path)}: " +
                $"struktury drugorzędowe = {protein.SecondaryStructures.Count}");

            return protein;
        }
        private static void ParseHelix(string line, Protein protein)
        {
            try
            {
                // Legacy PDB:
                //
                // chain początku: kolumna 20
                // numer początku: kolumny 22-25
                // insertion code: kolumna 26
                //
                // chain końca: kolumna 32
                // numer końca: kolumny 34-37
                // insertion code: kolumna 38

                char startChain = line.Length > 19
                        ? line[19]
                        : ' ';

                int startResidue = int.Parse(line.Substring(21, 4).Trim());

                char startInsertion = line.Length > 25
                        ? line[25]
                        : ' ';

                char endChain = line.Length > 31
                        ? line[31]
                        : startChain;

                int endResidue = int.Parse(line.Substring(33, 4).Trim());

                char endInsertion = line.Length > 37
                        ? line[37]
                        : ' ';

                protein.SecondaryStructures.Add(
                    new SecondaryStructureSegment
                    {
                        ChainId = startChain,

                        StartResidueId = startResidue,
                        StartInsertionCode = startInsertion,

                        EndResidueId = endResidue,
                        EndInsertionCode = endInsertion,

                        Type = SecondaryStructureType.Helix
                    });
            }
            catch
            {
                // Uszkodzony rekord HELIX nie powinien
                // blokować wczytania całej struktury.
            }
        }
        private static void ParseSheet(string line, Protein protein)
        {
            try
            {
                // Legacy PDB:
                //
                // chain początku: kolumna 22
                // numer początku: kolumny 23-26
                // insertion code: kolumna 27
                //
                // chain końca: kolumna 33
                // numer końca: kolumny 34-37
                // insertion code: kolumna 38

                char startChain = line.Length > 21
                        ? line[21]
                        : ' ';

                int startResidue = int.Parse(line.Substring(22, 4).Trim());

                char startInsertion = line.Length > 26
                        ? line[26]
                        : ' ';

                char endChain = line.Length > 32
                        ? line[32]
                        : startChain;

                int endResidue = int.Parse(line.Substring(33, 4).Trim());

                char endInsertion = line.Length > 37
                        ? line[37]
                        : ' ';

                protein.SecondaryStructures.Add(
                    new SecondaryStructureSegment
                    {
                        ChainId = startChain,

                        StartResidueId = startResidue,
                        StartInsertionCode = startInsertion,

                        EndResidueId = endResidue,
                        EndInsertionCode = endInsertion,

                        Type = SecondaryStructureType.Sheet
                    });
            }
            catch
            {
                // Analogicznie - błędny SHEET
                // nie blokuje atomów.
            }
        }
    }
}
