using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Models
{
    public class Chains
    {
        public char Id { get; set; }
        public List<Residues> Residues { get; set; } = new();
    }
}
