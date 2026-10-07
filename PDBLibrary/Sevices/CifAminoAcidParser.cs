using PDBLibrary.Geometry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Sevices
{
    // <summary>
    /// Wczytuje idealne współrzędne atomów aminokwasu z pliku
    /// Chemical Component Dictionary (CIF).
    ///
    /// Współrzędne są następnie przekształcane do lokalnego układu:
    /// CA = (0,0,0)
    /// CA -> C = dodatnia oś X
    /// N wyznacza orientację osi Y.
    /// </summary>
    public static class CifAminoAcidParser
    {
        /// <summary>
        /// Wczytuje szablon aminokwasu z pliku CIF.
        /// </summary>
        public static AminoAcidTemplate Parse(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"Nie znaleziono pliku CIF: {path}");

            string[] lines = File.ReadAllLines(path);

            var atoms = ParseAtoms(lines);

            if (atoms.Count == 0)
                throw new InvalidDataException(
                    $"Plik CIF nie zawiera atomów: {path}");

            return BuildTemplate(atoms, path);
        }

        private static List<CifAtom> ParseAtoms(string[] lines)
        {
            var atoms = new List<CifAtom>();

            int loopStart = -1;
            int atomDataStart = -1;

            string? nameColumn = null;
            string? elementColumn = null;
            string? xColumn = null;
            string? yColumn = null;
            string? zColumn = null;

            // Szukamy sekcji _chem_comp_atom
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (line.Equals("loop_", StringComparison.OrdinalIgnoreCase))
                {
                    int j = i + 1;

                    var columns = new List<string>();

                    while (j < lines.Length)
                    {
                        string column = lines[j].Trim();

                        if (column.StartsWith("_"))
                        {
                            columns.Add(column);
                            j++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    bool isAtomLoop = columns.Any(c =>
                        c.StartsWith("_chem_comp_atom.",
                            StringComparison.OrdinalIgnoreCase));

                    if (!isAtomLoop)
                        continue;

                    loopStart = i;

                    foreach (string column in columns)
                    {
                        if (column.Equals(
                            "_chem_comp_atom.atom_id",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            nameColumn = column;
                        }
                        else if (column.Equals(
                            "_chem_comp_atom.type_symbol",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            elementColumn = column;
                        }
                        else if (column.Equals(
                            "_chem_comp_atom.pdbx_model_Cartn_x_ideal",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            xColumn = column;
                        }
                        else if (column.Equals(
                            "_chem_comp_atom.pdbx_model_Cartn_y_ideal",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            yColumn = column;
                        }
                        else if (column.Equals(
                            "_chem_comp_atom.pdbx_model_Cartn_z_ideal",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            zColumn = column;
                        }
                    }

                    atomDataStart = j;
                    break;
                }
            }

            if (loopStart < 0 || atomDataStart < 0)
                throw new InvalidDataException(
                    "Nie znaleziono sekcji _chem_comp_atom w pliku CIF.");

            if (nameColumn == null ||
                elementColumn == null ||
                xColumn == null ||
                yColumn == null ||
                zColumn == null)
            {
                throw new InvalidDataException(
                    "Brakuje wymaganych kolumn idealnych współrzędnych atomów w CIF.");
            }

            // Ponownie odczytujemy nagłówki, aby znać kolejność kolumn.
            var columnNames = new List<string>();

            for (int i = loopStart + 1; i < atomDataStart; i++)
            {
                string line = lines[i].Trim();

                if (line.StartsWith("_"))
                    columnNames.Add(line);
            }

            int nameIndex = columnNames.IndexOf(nameColumn);
            int elementIndex = columnNames.IndexOf(elementColumn);
            int xIndex = columnNames.IndexOf(xColumn);
            int yIndex = columnNames.IndexOf(yColumn);
            int zIndex = columnNames.IndexOf(zColumn);

            // Czytamy rekordy aż do końca sekcji.
            for (int i = atomDataStart; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("#"))
                    break;

                if (line.StartsWith("_") ||
                    line.Equals("loop_", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                List<string> values = SplitCifLine(line);

                if (values.Count <= Math.Max(
                    Math.Max(nameIndex, elementIndex),
                    Math.Max(Math.Max(xIndex, yIndex), zIndex)))
                {
                    continue;
                }

                string atomName = values[nameIndex];
                string element = values[elementIndex];

                if (!TryParseDouble(values[xIndex], out double x) ||
                    !TryParseDouble(values[yIndex], out double y) ||
                    !TryParseDouble(values[zIndex], out double z))
                {
                    // Brak idealnych współrzędnych.
                    continue;
                }

                atoms.Add(new CifAtom(
                    atomName,
                    element,
                    new Vec3(x, y, z)));
            }

            return atoms;
        }

        private static AminoAcidTemplate BuildTemplate(
            List<CifAtom> atoms,
            string path)
        {
            CifAtom n = FindAtom(atoms, "N", path);
            CifAtom ca = FindAtom(atoms, "CA", path);
            CifAtom c = FindAtom(atoms, "C", path);

            Vec3 e1 = Normalize(c.Position - ca.Position);

            // Kierunek od CA do N.
            Vec3 nDirection = n.Position - ca.Position;

            // Usuwamy składową równoległą do CA->C.
            double projection = Vec3.Dot(nDirection, e1);

            Vec3 projectedN = nDirection - projection * e1;

            Vec3 e2 = Normalize(projectedN);

            // Trzecia oś lokalnego układu.
            Vec3 e3 = Vec3.Cross(e1, e2);

            // Upewniamy się, że układ jest prawoskrętny.
            e3 = Normalize(e3);

            string code = Path.GetFileNameWithoutExtension(path)
                .Trim()
                .ToUpperInvariant();

            var template = new AminoAcidTemplate(
                code,
                Transform(n.Position, ca.Position, e1, e2, e3),
                Transform(ca.Position, ca.Position, e1, e2, e3),
                Transform(c.Position, ca.Position, e1, e2, e3));

            foreach (CifAtom atom in atoms)
            {
                string atomName = atom.Name.Trim().ToUpperInvariant();

                // N, CA i C są już zapisane osobno.
                if (atomName == "N" ||
                    atomName == "CA" ||
                    atomName == "C")
                {
                    continue;
                }

                // O oraz OXT nie są częścią łańcucha bocznego.
                if (atomName == "O" ||
                    atomName == "OXT")
                {
                    continue;
                }

                // GLY nie posiada łańcucha bocznego.
                template.SideChainAtoms.Add(
                    new TemplateAtom(
                        atom.Name,
                        atom.Element,
                        Transform(
                            atom.Position,
                            ca.Position,
                            e1,
                            e2,
                            e3)));
            }

            return template;
        }

        private static CifAtom FindAtom(
            List<CifAtom> atoms,
            string name,
            string path)
        {
            CifAtom? atom = atoms.FirstOrDefault(a =>
                a.Name.Trim().Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase));

            if (atom == null)
            {
                throw new InvalidDataException(
                    $"Plik {path} nie zawiera wymaganego atomu {name}.");
            }

            return atom;
        }

        private static Vec3 Transform(
            Vec3 position,
            Vec3 origin,
            Vec3 e1,
            Vec3 e2,
            Vec3 e3)
        {
            Vec3 v = position - origin;

            return new Vec3(
                Vec3.Dot(v, e1),
                Vec3.Dot(v, e2),
                Vec3.Dot(v, e3));
        }

        private static Vec3 Normalize(Vec3 v)
        {
            double length = Math.Sqrt(
                v.X * v.X +
                v.Y * v.Y +
                v.Z * v.Z);

            if (length < 1e-10)
                throw new InvalidDataException(
                    "Nie można znormalizować wektora o długości 0.");

            return new Vec3(
                v.X / length,
                v.Y / length,
                v.Z / length);
        }

        private static bool TryParseDouble(
            string value,
            out double result)
        {
            value = value.Trim();

            if (value == "?" || value == ".")
            {
                result = 0;
                return false;
            }

            return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result);
        }

        private static List<string> SplitCifLine(string line)
        {
            return line
                .Split(
                    new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        private sealed class CifAtom
        {
            public string Name { get; }
            public string Element { get; }
            public Vec3 Position { get; }

            public CifAtom(
                string name,
                string element,
                Vec3 position)
            {
                Name = name;
                Element = element;
                Position = position;
            }
        }
    }
}