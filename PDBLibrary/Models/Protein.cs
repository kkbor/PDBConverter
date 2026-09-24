using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Models
{
    public class Protein
    {
        public string? Name { get; set; }
        public List<Chains> Chains { get; set; } = new();
    }
}
