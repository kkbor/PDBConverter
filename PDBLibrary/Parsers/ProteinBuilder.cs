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
    /// uzywana zarowno przez PdbParsers (legacy PDB), jak i MmcifParser (PDBx/mmCIF).
    ///
    /// WAZNE: reszta jest identyfikowana parą (ResidueId, InsertionCode), NIE samym
    /// ResidueId. Dwie reszty o tym samym numerze ale roznym insertion code (np.
    /// "23" i "23A") to FIZYCZNIE ROZNE reszty - bez tego rozroznienia zlewalyby
    /// sie w jeden obiekt z podwojnym kompletem atomow szkieletu, co psuje m.in.
    /// wykrywanie wiazan peptydowych w BondDetector.
    ///
    /// Analogiczny problem dotyczy ALTERNATYWNYCH KONFORMACJI (altLoc) - jeden
    /// fizyczny atom moze miec w pliku kilka wpisow (np. dwa rozne "CA" dla
    /// altLoc A i B). Parser nie odrzuca ich explicite na podstawie kolumny
    /// altLoc, ale ten builder zachowuje TYLKO PIERWSZY napotkany atom o danej
    /// nazwie w obrebie reszty (w pliku PDB altLoc ' '/'A' - czyli ta "glowna",
    /// najczesciej najwyzej obsadzona konformacja - pojawia sie pierwsza) i
    /// pomija kolejne kopie. Bez tego FirstOrDefault(x => x.Name == "...") w
    /// BondDetector moze losowo trafic na "niewlasciwa" kopie przy parowaniu
    /// z sasiednia reszta.
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

            Residues? residue = chain.Residues.FirstOrDefault(r =>
                r.Id == atom.ResidueId && r.InsertionCode == atom.InsertionCode);

            if (residue == null)
            {
                residue = new Residues
                {
                    Id = atom.ResidueId,
                    Name = atom.ResidueName,
                    InsertionCode = atom.InsertionCode
                };
                chain.Residues.Add(residue);
            }

            bool isDuplicateAltLoc = residue.Atoms.Any(a =>
                a.Name.Equals(atom.Name, System.StringComparison.OrdinalIgnoreCase));

            if (isDuplicateAltLoc)
                return; // druga/kolejna alternatywna konformacja tego samego atomu - pomijamy

            residue.Atoms.Add(atom);
        }
    }
}
