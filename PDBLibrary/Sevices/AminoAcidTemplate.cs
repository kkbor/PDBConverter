using PDBLibrary.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDBLibrary.Sevices
{
    /// <summary>
    /// Pojedynczy atom należący do szablonu aminokwasu.
    /// Współrzędne znajdują się w lokalnym układzie odniesienia szablonu.
    /// </summary>
    public class TemplateAtom
    {
        public string Name { get; }
        public string Element { get; }
        public Vec3 Position { get; }

        public TemplateAtom(
            string name,
            string element,
            Vec3 position)
        {
            Name = name;
            Element = element;
            Position = position;
        }
    }

    /// <summary>
    /// Wyidealizowana geometria aminokwasu.
    ///
    /// Współrzędne znajdują się w lokalnym układzie odniesienia:
    /// CA = (0, 0, 0)
    /// C znajduje się na dodatniej osi X.
    ///
    /// N, CA i C służą do zdefiniowania układu odniesienia.
    /// SideChainAtoms zawiera atomy łańcucha bocznego.
    /// </summary>
    public class AminoAcidTemplate
    {
        public string Code { get; }

        public Vec3 N { get; }
        public Vec3 CA { get; }
        public Vec3 C { get; }

        public List<TemplateAtom> SideChainAtoms { get; }

        public AminoAcidTemplate(
            string code,
            Vec3 n,
            Vec3 ca,
            Vec3 c)
        {
            Code = code;
            N = n;
            CA = ca;
            C = c;

            SideChainAtoms = new List<TemplateAtom>();
        }
    }
}