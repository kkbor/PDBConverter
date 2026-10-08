using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Validation
{
    public enum ValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public class ValidationIssue
    {
        public ValidationSeverity Severity { get; init; }
        public string Message { get; init; } = "";

        /// <summary>Glowna reszta, ktorej dotyczy problem (do podswietlenia w UI).</summary>
        public Residues? Residue { get; init; }

        /// <summary>Druga reszta - uzywane przy problemach LACZACYCH dwie reszty (np. przerwa w lancuchu, starcie).</summary>
        public Residues? ResidueB { get; init; }
    }

    public class ValidationReport
    {
        public List<ValidationIssue> Issues { get; } = new();

        public int ErrorCount => Issues.FindAll(i => i.Severity == ValidationSeverity.Error).Count;
        public int WarningCount => Issues.FindAll(i => i.Severity == ValidationSeverity.Warning).Count;
        public bool HasErrors => ErrorCount > 0;
    }
}