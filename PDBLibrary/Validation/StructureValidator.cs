using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Validation
{
    /// <summary>Punkt wejscia do walidacji - uruchamia wszystkie sprawdzenia i zwraca jeden raport.</summary>
    public static class StructureValidator
    {
        public static ValidationReport Validate(Protein protein)
        {
            var report = new ValidationReport();

            ChainValidator.Validate(protein, report);
            ClashDetector.Validate(protein, report);

            return report;
        }
        public static ValidationReport ValidateAfterMutation(Protein protein,Residues changedResidue)
        {
            var report = new ValidationReport();

            ChainValidator.ValidateResidue(protein,changedResidue,report);

            ClashDetector.ValidateResidue(protein,changedResidue,report);

            return report;
        }
    }
}
