using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Models
{
    public class Residues
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public List<Atoms> Atoms { get; set; } = new ();
        public char InsertionCode { get; set; } = ' ';
    }
}
