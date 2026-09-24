using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Models
{
    public class Bonds
    {
        public Atoms Atom1 { get; set; }
        public Atoms Atom2 { get; set; }

        public Bonds(Atoms atom1, Atoms atom2)
        {
            Atom1 = atom1;
            Atom2 = atom2;
        }
    }
}
