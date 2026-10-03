using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Parsers
{
    /// <summary>
    /// Wspolna logika wstawiania atomu do drzewa Protein -> Chain -> Residue -> Atom,
    /// uzywana zarowno przez PdbParsers (legacy PDB), jak i MmcifParser (PDBx/mmCIF),
    /// zeby nie duplikowac tej samej logiki wyszukiwania/tworzenia lancucha i reszty.
    /// </summary>
    internal static class ProteinBuilder
    {
        public static void AddAtom(Protein protein, Atoms atom)
        {
            Chains? chain = protein.Chains.FirstOrDefault(c => c.Id == atom.ChainId);

            if (chain == null)
            {
                chain = new Chains { Id = atom.ChainId };
                protein.Chains.Add(chain);
            }

            Residues? residue = chain.Residues.FirstOrDefault(r => r.Id == atom.ResidueId);

            if (residue == null)
            {
                residue = new Residues { Id = atom.ResidueId, Name = atom.ResidueName };
                chain.Residues.Add(residue);
            }

            residue.Atoms.Add(atom);
        }
    }
}
