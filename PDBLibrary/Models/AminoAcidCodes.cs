using System.Collections.Generic;

namespace PDBLibrary.Models
{
    public static class AminoAcidCodes
    {
        /// <summary>Lista 20 standardowych aminokwasow do wyboru w UI (np. ComboBox przy podmianie).</summary>
        public static readonly IReadOnlyList<(string Code, string Name)> StandardAminoAcids = new[]
        {
            ("ALA", "Alanina"), ("ARG", "Arginina"), ("ASN", "Asparagina"), ("ASP", "Kwas asparaginowy"),
            ("CYS", "Cysteina"), ("GLN", "Glutamina"), ("GLU", "Kwas glutaminowy"), ("GLY", "Glicyna"),
            ("HIS", "Histydyna"), ("ILE", "Izoleucyna"), ("LEU", "Leucyna"), ("LYS", "Lizyna"),
            ("MET", "Metionina"), ("PHE", "Fenyloalanina"), ("PRO", "Prolina"), ("SER", "Seryna"),
            ("THR", "Treonina"), ("TRP", "Tryptofan"), ("TYR", "Tyrozyna"), ("VAL", "Walina")
        };
        private static readonly Dictionary<string, char> ThreeToOne = new()
        {
            { "ALA", 'A' }, { "ARG", 'R' }, { "ASN", 'N' }, { "ASP", 'D' },
            { "CYS", 'C' }, { "GLN", 'Q' }, { "GLU", 'E' }, { "GLY", 'G' },
            { "HIS", 'H' }, { "ILE", 'I' }, { "LEU", 'L' }, { "LYS", 'K' },
            { "MET", 'M' }, { "PHE", 'F' }, { "PRO", 'P' }, { "SER", 'S' },
            { "THR", 'T' }, { "TRP", 'W' }, { "TYR", 'Y' }, { "VAL", 'V' },
            // warianty protonacji / niestandardow
            { "HSD", 'H' }, { "HSE", 'H' }, { "HSP", 'H' }, { "HID", 'H' },
            { "HIE", 'H' }, { "HIP", 'H' }, { "CYX", 'C' }, { "MSE", 'M' }
        };

        /// <summary>
        /// Zwraca jednoliterowy kod aminokwasu. Dla nierozpoznanych reszt
        /// (np. ligandy, jony, wody HETATM) zwraca 'X'.
        /// </summary>
        public static char GetOneLetterCode(string threeLetterResidueName)
        {
            if (string.IsNullOrWhiteSpace(threeLetterResidueName))
                return 'X';

            return ThreeToOne.TryGetValue(threeLetterResidueName.Trim().ToUpperInvariant(), out char code)
                ? code
                : 'X';
        }

        /// <summary>
        /// Sprawdza, czy reszta jest jednym z 20 standardowych aminokwasów.
        ///
        /// Ważne:
        /// warianty takie jak MSE, HSD czy CYX nie są tutaj
        /// traktowane jako standardowe aminokwasy.
        /// </summary>
        public static bool IsStandardAminoAcid(string threeLetterResidueName)
        {
            if (string.IsNullOrWhiteSpace(threeLetterResidueName))
                return false;

            string code = threeLetterResidueName
                .Trim()
                .ToUpperInvariant();

            return StandardAminoAcids.Any(x => x.Code == code);
        }
    }
}
