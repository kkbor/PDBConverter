using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Parsers
{
    /// <summary>
    /// Wybiera wlasciwy parser (PdbParsers albo MmcifParser) na podstawie
    /// rozszerzenia pliku, a w razie niejednoznacznosci - jego zawartosci.
    /// </summary>
    public static class StructureFileLoader
    {
        public static Protein Load(string path)
        {
            return IsMmcif(path)
                ? new MmcifParser().Load(path)
                : new PdbParsers().Load(path);
        }

        private static bool IsMmcif(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".cif" || ext == ".mmcif")
                return true;

            if (ext == ".pdb" || ext == ".ent")
                return false;

            // nierozpoznane rozszerzenie - zajrzyj do poczatku pliku:
            // mmCIF zaczyna sie od "data_", legacy PDB ma linie ATOM/HETATM
            // o stalej szerokosci kolumn juz od poczatku pliku
            foreach (var line in File.ReadLines(path).Take(20))
            {
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("data_", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (trimmed.StartsWith("ATOM") || trimmed.StartsWith("HETATM"))
                    return false;
            }

            return false; // domyslnie zakladamy legacy PDB
        }
    }
}

