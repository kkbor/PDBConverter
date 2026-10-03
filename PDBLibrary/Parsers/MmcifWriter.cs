using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Parsers
{
    /// <summary>
    /// Zapisuje model Protein jako petle _atom_site w formacie PDBx/mmCIF.
    /// Prosty, ale wystarczajacy do round-trip z MmcifParser - nie generuje
    /// pelnych metadanych (naglowki, _entity, _struct itd.), ktorych i tak
    /// MmcifParser nie odczytuje.
    /// </summary>
    public static class MmcifWriter
    {
        public static void Save(Protein protein, string path)
        {
            using var writer = new StreamWriter(path, false, Encoding.ASCII);

            writer.WriteLine("data_structure");
            writer.WriteLine("#");
            writer.WriteLine("loop_");
            writer.WriteLine("_atom_site.group_PDB");
            writer.WriteLine("_atom_site.id");
            writer.WriteLine("_atom_site.type_symbol");
            writer.WriteLine("_atom_site.label_atom_id");
            writer.WriteLine("_atom_site.label_comp_id");
            writer.WriteLine("_atom_site.auth_asym_id");
            writer.WriteLine("_atom_site.auth_seq_id");
            writer.WriteLine("_atom_site.Cartn_x");
            writer.WriteLine("_atom_site.Cartn_y");
            writer.WriteLine("_atom_site.Cartn_z");
            writer.WriteLine("_atom_site.occupancy");
            writer.WriteLine("_atom_site.B_iso_or_equiv");

            int serial = 1;

            foreach (var chain in protein.Chains)
            {
                foreach (var residue in chain.Residues.OrderBy(r => r.Id))
                {
                    foreach (var atom in residue.Atoms)
                    {
                        writer.WriteLine(FormatRow(atom, serial, chain.Id, residue));
                        serial++;
                    }
                }
            }

            writer.WriteLine("#");
        }

        private static string FormatRow(Atoms atom, int serial, char chainId, Residues residue)
        {
            string element = string.IsNullOrWhiteSpace(atom.Element) ? "?" : atom.Element.Trim();

            var fields = new[]
            {
                "ATOM",
                serial.ToString(CultureInfo.InvariantCulture),
                element,
                Escape(atom.Name),
                Escape(residue.Name),
                chainId.ToString(),
                residue.Id.ToString(CultureInfo.InvariantCulture),
                atom.X.ToString("F3", CultureInfo.InvariantCulture),
                atom.Y.ToString("F3", CultureInfo.InvariantCulture),
                atom.Z.ToString("F3", CultureInfo.InvariantCulture),
                atom.Occupancy.ToString("F2", CultureInfo.InvariantCulture),
                atom.BFactor.ToString("F2", CultureInfo.InvariantCulture)
            };

            return string.Join(" ", fields);
        }

        // mmCIF wymaga cudzyslowu wokol tokenu, jesli zawiera biale znaki
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "?";

            return value.Contains(' ') ? $"'{value}'" : value;
        }
    }
}

