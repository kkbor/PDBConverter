using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Models
{
    public enum SecondaryStructureType
    {
        Coil,
        Helix,
        Sheet
    }
    public class SecondaryStructureSegment
    {
        public char ChainId { get; set; }
        public int StartResidueId { get; set; }
        public char StartInsertionCode { get; set; } = ' ';
        public int EndResidueId { get; set; }
        public char EndInsertionCode { get; set; } = ' ';
        public SecondaryStructureType Type { get; set; }
    }
    
}
