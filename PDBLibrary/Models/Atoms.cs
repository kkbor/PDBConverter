using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Models
{
    public class Atoms
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? ResidueName { get; set; }
        public char ChainId { get; set; }
        public int ResidueId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Occupancy { get; set; }
        public double BFactor { get; set; }
        public string? Element { get; set; }
        public char InsertionCode { get; set; } = ' ';
    }
}
