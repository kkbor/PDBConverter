using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Parsers
{
    public class PdbParsers
    {
        public Protein Load(string path)
        {
            Protein protein = new Protein();

            foreach (string line in File.ReadLines(path))
            {
                if (!(line.StartsWith("ATOM") || line.StartsWith("HETATM")))
                    continue;

                Atoms atom = new Atoms
                {
                    Id = int.Parse(line.Substring(6, 5).Trim()),
                    Name = line.Substring(12, 4).Trim(),
                    ResidueName = line.Substring(17, 3).Trim(),
                    ChainId = line[21],
                    ResidueId = int.Parse(line.Substring(22, 4).Trim()),

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

                Chains? chain = protein.Chains.FirstOrDefault(c => c.Id == atom.ChainId);

                if (chain == null)
                {
                    chain = new Chains
                    {
                        Id = atom.ChainId
                    };

                    protein.Chains.Add(chain);
                }

                Residues? residue = chain.Residues.FirstOrDefault(r => r.Id == atom.ResidueId);

                if (residue == null)
                {
                    residue = new Residues
                    {
                        Id = atom.ResidueId,
                        Name = atom.ResidueName
                    };

                    chain.Residues.Add(residue);
                }

                residue.Atoms.Add(atom);
            }

            return protein;
        }
    }
}