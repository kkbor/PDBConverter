using PDBLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace PDBViewer.Vizualization
{
    public partial class ProteinVisualizer
    {
        /// <summary>
        /// Dodaje sferę pojedynczego atomu do istniejącego
        /// MeshGeometry3D.
        ///
        /// Metoda NIE tworzy GeometryModel3D.
        ///
        /// Dzięki temu wiele atomów może zostać połączonych
        /// w jeden wspólny mesh.
        /// </summary>
        private void AddAtomSphere(MeshGeometry3D mesh, Atoms atom, int segments, int rings)
        {
            string element = NormalizeElement(atom.Element);

            double radius = AtomRadii.TryGetValue(element, out double r)
                    ? r
                    : DefaultAtomRadius;

            int startIndex = mesh.Positions.Count;


            for (int ring = 0; ring <= rings; ring++)
            {
                double phi = Math.PI * ring / rings;

                for (int segment = 0; segment <= segments; segment++)
                {
                    double theta = 2.0 * Math.PI * segment / segments;

                    double x = Math.Sin(phi) * Math.Cos(theta);

                    double y = Math.Cos(phi);

                    double z = Math.Sin(phi) * Math.Sin(theta);

                    mesh.Positions.Add(new Point3D(atom.X + radius * x, atom.Y + radius * y, atom.Z + radius * z));

                    mesh.Normals.Add(new Vector3D(x, y, z));
                }
            }


            for (int ring = 0; ring < rings; ring++)
            {
                for (int segment = 0; segment < segments; segment++)
                {
                    int current = startIndex + ring * (segments + 1) + segment;

                    int next = current + segments + 1;

                    // Pierwszy trójkąt
                    mesh.TriangleIndices.Add(current);

                    mesh.TriangleIndices.Add(next);

                    mesh.TriangleIndices.Add(current + 1);

                    // Drugi trójkąt
                    mesh.TriangleIndices.Add(current + 1);

                    mesh.TriangleIndices.Add(next);

                    mesh.TriangleIndices.Add(next + 1);
                }
            }
        }
    }
}
