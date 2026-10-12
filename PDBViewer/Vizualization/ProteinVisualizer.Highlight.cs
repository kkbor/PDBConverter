using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PDBViewer.Vizualization
{
    public partial class ProteinVisualizer
    {
        // PODŚWIETLENIE RESZTY

        /// <summary>
        /// Buduje mały model overlay dla jednej reszty.
        ///
        /// Ten model zawiera wyłącznie atomy i wiązania należące
        /// do aktualnie podświetlanej reszty.
        ///
        /// Nie modyfikuje głównego, zamrożonego modelu.
        /// </summary>
        public Model3DGroup BuildHighlightModel(
            ResidueGeometryInfo info)
        {
            var group = new Model3DGroup();

            var mesh = new MeshGeometry3D();

            foreach (var atom in info.Atoms)
            {
                AddAtomSphere(mesh, atom, 8, 6);
            }

            foreach (var bond in info.Bonds)
            {
                var p1 = new Point3D(bond.Atom1.X, bond.Atom1.Y, bond.Atom1.Z);

                var p2 = new Point3D(bond.Atom2.X, bond.Atom2.Y, bond.Atom2.Z);

                AddCylinderMesh(mesh, p1, p2, BondRadius * 1.4, 8);
            }

            var material = CreateHighlightMaterial();

            mesh.Freeze();

            var model = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            };

            group.Children.Add(model);

            return group;
        }

        /// <summary>
        /// Materiał używany do podświetlania reszty.
        /// </summary>
        private static Material CreateHighlightMaterial()
        {
            var group = new MaterialGroup();

            group.Children.Add(new DiffuseMaterial(new SolidColorBrush(Colors.Orange)));

            group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(180, 90, 0))));

            group.Freeze();

            return group;
        }
    }
}
