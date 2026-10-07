using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Sevices
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    namespace PDBLibrary.Services
    {
        /// <summary>
        /// Magazyn szablonów geometrii 20 standardowych aminokwasów.
        ///
        /// Szablony są tworzone na podstawie idealnych współrzędnych
        /// atomowych z Chemical Component Dictionary (CCD).
        /// </summary>
        public static class AminoAcidTemplateLibrary
        {
            private static readonly Dictionary<string, AminoAcidTemplate> Templates =
                BuildTemplates();

            public static bool TryGet(
                string threeLetterCode,
                out AminoAcidTemplate? template)
            {
                return Templates.TryGetValue(
                    threeLetterCode.Trim().ToUpperInvariant(),
                    out template);
            }

            private static Dictionary<string, AminoAcidTemplate> BuildTemplates()
            {
                var dict = new Dictionary<string, AminoAcidTemplate>(
                    StringComparer.OrdinalIgnoreCase);

                string directory = GetAminoAcidDirectory();

                string[] aminoAcids =
                {
                "ALA",
                "ARG",
                "ASN",
                "ASP",
                "CYS",
                "GLN",
                "GLU",
                "GLY",
                "HIS",
                "ILE",
                "LEU",
                "LYS",
                "MET",
                "PHE",
                "PRO",
                "SER",
                "THR",
                "TRP",
                "TYR",
                "VAL"
            };

                foreach (string code in aminoAcids)
                {
                    string path = Path.Combine(
                        directory,
                        $"{code}.cif");

                    if (!File.Exists(path))
                    {
                        throw new FileNotFoundException(
                            $"Brakuje pliku szablonu aminokwasu: {path}");
                    }

                    AminoAcidTemplate template =
                        CifAminoAcidParser.Parse(path);

                    dict[template.Code] = template;
                }

                return dict;
            }

            private static string GetAminoAcidDirectory()
            {
                return Path.Combine(
                    AppContext.BaseDirectory,
                    "Data",
                    "AminoAcids");
            }
        }
    }
}