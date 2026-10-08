using PDBLibrary.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PDBViewer
{
    /// <summary>Opakowanie ValidationIssue do wyswietlenia w ListBox (kolor wg wagi problemu).</summary>
    public class ValidationIssueViewModel
    {
        public ValidationIssue Issue { get; }

        public string Message => Issue.Message;

        public string SeverityLabel => Issue.Severity switch
        {
            ValidationSeverity.Error => "BŁĄD",
            ValidationSeverity.Warning => "OSTRZEŻENIE",
            _ => "INFO"
        };

        public Brush SeverityBrush => Issue.Severity switch
        {
            ValidationSeverity.Error => Brushes.Crimson,
            ValidationSeverity.Warning => Brushes.DarkOrange,
            _ => Brushes.Gray
        };

        public ValidationIssueViewModel(ValidationIssue issue) => Issue = issue;
    }
}
