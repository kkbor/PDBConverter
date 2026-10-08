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
    /// Parser formatu PDBx/mmCIF. W odroznieniu od legacy PDB (stale kolumny),
    /// mmCIF to tokeny rozdzielone bialymi znakami, a KOLEJNOSC kolumn jest
    /// zdefiniowana w nagłówku petli (loop_) tuz nad danymi - wiec trzeba
    /// najpierw zmapowac nazwy kolumn na indeksy, zanim zacznie sie czytac wiersze.
    ///
    /// Obsluguje jedynie petle _atom_site (to one niosa wspolrzedne atomow -
    /// odpowiednik ATOM/HETATM w legacy PDB). Pozostale sekcje (_entity,
    /// _struct, naglowki metadanych itd.) sa pomijane.
    ///
    /// Ograniczenia swiadome na tym etapie:
    ///  - nie obslugiwane sa wieloliniowe wartosci tekstowe (bloki zaczynajace
    ///    sie od ';' na poczatku linii) - w petli _atom_site praktycznie nie
    ///    wystepuja, wiec to bezpieczne uproszczenie,
    ///  - identyfikator lancucha brany jest z pierwszego znaku auth_asym_id
    ///    (model Atoms.ChainId to char, zgodnie z ograniczeniem legacy PDB) -
    ///    mmCIF formalnie dopuszcza wieloznakowe ID lancuchow.
    /// </summary>
    public class MmcifParser
    {
        public Protein Load(string path)
        {
            var protein = new Protein();
            var lines = File.ReadAllLines(path);

            int i = 0;
            while (i < lines.Length)
            {
                string trimmed = lines[i].TrimStart();

                if (trimmed.Equals("loop_", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    var columns = new List<string>();

                    while (i < lines.Length && lines[i].TrimStart().StartsWith("_"))
                    {
                        columns.Add(lines[i].Trim());
                        i++;
                    }

                    if (columns.Count > 0 &&
                        columns[0].StartsWith("_atom_site.", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseAtomSiteLoop(protein, lines, ref i, columns);
                        continue; // i zostalo juz przesuniete za wiersze danych tej petli
                    }

                    // inna petla (np. _entity, _struct_conf) - pomijamy jej wiersze danych
                    while (i < lines.Length && IsLoopDataRow(lines[i]))
                        i++;

                    continue;
                }

                i++;
            }

            return protein;
        }

        private static void ParseAtomSiteLoop(
            Protein protein,
            string[] lines,
            ref int i,
            List<string> columns)
        {
            var colIndex =  new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int c = 0; c < columns.Count; c++)
            {
                const string prefix = "_atom_site.";

                string name = columns[c].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        ? columns[c].Substring(prefix.Length)
                        : columns[c];

                colIndex[name] = c;
            }

            int idxGroup = GetIndex(colIndex, "group_PDB");
            int idxSerial = GetIndex(colIndex, "id");

            int idxAtomName = GetIndex(colIndex, "auth_atom_id", "label_atom_id");

            int idxResName = GetIndex(colIndex, "auth_comp_id", "label_comp_id");

            int idxChain = GetIndex(colIndex, "auth_asym_id", "label_asym_id");

            int idxResSeq = GetIndex(colIndex, "auth_seq_id", "label_seq_id");

            int idxX = GetIndex(colIndex, "Cartn_x");
            int idxY = GetIndex(colIndex, "Cartn_y");
            int idxZ = GetIndex(colIndex, "Cartn_z");

            int idxOcc = GetIndex(colIndex, "occupancy");
            int idxBFactor = GetIndex(colIndex, "B_iso_or_equiv");

            int idxElement = GetIndex(colIndex, "type_symbol");
            int idxInsCode = GetIndex(colIndex, "pdbx_PDB_ins_code");

            // Alternatywna lokalizacja atomu.
            int idxAltLoc = GetIndex(colIndex, "label_alt_id");

            int alternateLocationCount = 0;

            while (i < lines.Length && IsLoopDataRow(lines[i]))
            {
                var tokens = TokenizeLine(lines[i]);
                i++;

                if (tokens.Count < columns.Count)
                    continue;

                string group = idxGroup >= 0
                        ? tokens[idxGroup]
                        : "ATOM";

                bool isAtomRecord = group.Equals( "ATOM", StringComparison.OrdinalIgnoreCase) ||
                    group.Equals("HETATM", StringComparison.OrdinalIgnoreCase);

                if (!isAtomRecord)
                    continue;

                string chainToken = idxChain >= 0
                        ? CleanToken(tokens[idxChain])
                        : "A";

                char chainId = chainToken.Length > 0
                        ? chainToken[0]
                        : 'A';

                string insCodeToken = idxInsCode >= 0
                        ? CleanToken(tokens[idxInsCode])
                        : string.Empty;

                char insertionCode = insCodeToken.Length > 0
                        ? insCodeToken[0]
                        : ' ';

                // Alternate location
                string altLocToken = idxAltLoc >= 0
                        ? CleanToken(tokens[idxAltLoc])
                        : string.Empty;

                char altLoc = altLocToken.Length > 0
                        ? altLocToken[0]
                        : ' ';

                if (altLoc != ' ')
                {
                    alternateLocationCount++;
                }

                var atom = new Atoms
                {
                    Id = idxSerial >= 0
                            ? ParseIntSafe(tokens[idxSerial])
                            : 0,

                    Name = idxAtomName >= 0
                            ? CleanToken(tokens[idxAtomName])
                            : string.Empty,

                    ResidueName = idxResName >= 0
                            ? CleanToken(tokens[idxResName])
                            : string.Empty,

                    ChainId = chainId,

                    ResidueId = idxResSeq >= 0
                            ? ParseIntSafe(tokens[idxResSeq])
                            : 0,

                    InsertionCode = insertionCode,

                    X = idxX >= 0
                            ? ParseDoubleSafe(tokens[idxX])
                            : 0.0,

                    Y = idxY >= 0
                            ? ParseDoubleSafe(tokens[idxY])
                            : 0.0,

                    Z = idxZ >= 0
                            ? ParseDoubleSafe(tokens[idxZ])
                            : 0.0,

                    Occupancy = idxOcc >= 0
                            ? ParseDoubleSafe(tokens[idxOcc], 1.0)
                            : 1.0,

                    BFactor = idxBFactor >= 0
                            ? ParseDoubleSafe(tokens[idxBFactor], 0.0)
                            : 0.0,

                    Element = idxElement >= 0
                            ? CleanToken(tokens[idxElement])
                            : string.Empty
                };

                ProteinBuilder.AddAtom(protein, atom);
            }

            Console.WriteLine(
                $"Atomy z altLoc w mmCIF: {alternateLocationCount}");
        }

        private static int GetIndex(Dictionary<string, int> map, params string[] candidateNames)
        {
            foreach (var name in candidateNames)
                if (map.TryGetValue(name, out int idx))
                    return idx;

            return -1;
        }

        /// <summary>Czy linia wyglada na wiersz danych petli (nie naglowek, nie komentarz, nie nowy blok).</summary>
        private static bool IsLoopDataRow(string line)
        {
            string trimmed = line.TrimStart();

            if (trimmed.Length == 0) return false;
            if (trimmed.StartsWith("_")) return false;
            if (trimmed.StartsWith("#")) return false;
            if (trimmed.StartsWith(";")) return false; // wieloliniowy blok tekstowy - nieobslugiwany
            if (trimmed.StartsWith("loop_", StringComparison.OrdinalIgnoreCase)) return false;
            if (trimmed.StartsWith("data_", StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }

        /// <summary>Dzieli linie na tokeny, respektujac proste cudzyslowy ('...' / "...").</summary>
        private static List<string> TokenizeLine(string line)
        {
            var tokens = new List<string>();
            int i = 0;

            while (i < line.Length)
            {
                while (i < line.Length && char.IsWhiteSpace(line[i]))
                    i++;

                if (i >= line.Length)
                    break;

                if (line[i] == '\'' || line[i] == '"')
                {
                    char quote = line[i];
                    i++;
                    int start = i;

                    while (i < line.Length &&
                           !(line[i] == quote && (i + 1 >= line.Length || char.IsWhiteSpace(line[i + 1]))))
                        i++;

                    tokens.Add(line.Substring(start, i - start));
                    if (i < line.Length) i++; // pomin zamykajacy cudzyslow
                }
                else
                {
                    int start = i;
                    while (i < line.Length && !char.IsWhiteSpace(line[i]))
                        i++;

                    tokens.Add(line.Substring(start, i - start));
                }
            }

            return tokens;
        }

        /// <summary>Usuwa oznaczenia wartosci brakujacej ('?', '.') i ewentualne cudzyslowy.</summary>
        private static string CleanToken(string token)
        {
            token = token.Trim();

            if (token == "?" || token == ".")
                return string.Empty;

            if (token.Length >= 2 &&
                (token[0] == '\'' || token[0] == '"') &&
                token[^1] == token[0])
            {
                token = token.Substring(1, token.Length - 2);
            }

            return token;
        }

        private static int ParseIntSafe(string token)
        {
            token = CleanToken(token);
            return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : 0;
        }

        private static double ParseDoubleSafe(string token, double fallback = 0.0)
        {
            token = CleanToken(token);
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : fallback;
        }
    }
}
