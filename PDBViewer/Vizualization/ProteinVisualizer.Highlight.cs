using PDBLibrary.Models;
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
        public Model3DGroup BuildHighlightModel(ResidueGeometryInfo info)
        {
            var group = new Model3DGroup();

            var mesh = new MeshGeometry3D();

            foreach (var atom in info.Atoms)
            {
                string element = NormalizeElement(atom.Element);
                double baseRadius = GetDisplayAtomRadius(element, _lastStyle);

                AddAtomSphere(mesh, atom, baseRadius * 1.2, 8, 6);
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
    
        // PODŚWIETLENIE KOLIZJI STERYCZNEJ (NA CZERWONO)

        /// <summary>
        /// Buduje overlay podświetlający na czerwono atomy biorące udział w
        /// konflikcie sterycznym (np. po podmianie aminokwasu, gdy nawet obrót
        /// wokół CA-CB nie wyeliminował kolizji z sąsiadami).
        ///
        /// Każdy kolidujący atom rysowany jest jako sfera NIECO WIĘKSZA niż jego
        /// rzeczywisty promień van der Waalsa (x1.35) - celowo, żeby czerwona
        /// "otoczka" była widoczna NAD normalnym kolorem atomu w głównym modelu,
        /// zamiast nakładać się z nim idealnie 1:1 (co w WPF 3D powoduje migotanie
        /// / z-fighting, bo obie powierzchnie leżałyby dokładnie w tej samej
        /// odległości od kamery).
        ///
        /// Osobna warstwa (jak BuildHighlightModel) - nie modyfikuje głównego,
        /// zamrożonego modelu całego białka.
        /// </summary>
        public Model3DGroup BuildClashHighlightModel(IEnumerable<Atoms> clashingAtoms)
        {
            var group = new Model3DGroup();

            var mesh = new MeshGeometry3D();

            foreach (var atom in clashingAtoms.Distinct())
            {
                string element = NormalizeElement(atom.Element);

                double baseRadius = GetDisplayAtomRadius(element, _lastStyle);

                AddAtomSphere(mesh, atom, baseRadius * 1.35, 10, 8);
            }

            if (mesh.Positions.Count == 0)
                return group;

            var material = CreateClashMaterial();

            mesh.Freeze();

            var model = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            };

            group.Children.Add(model);

            return group;
        }

        /// <summary>Materiał używany do podświetlania atomów w konflikcie sterycznym.</summary>
        private static Material CreateClashMaterial()
        {
            var group = new MaterialGroup();

            group.Children.Add(new DiffuseMaterial(new SolidColorBrush(Colors.Red)));

            group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(140, 0, 0))));

            group.Freeze();

            return group;
        }
    }
}
