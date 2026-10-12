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
        /// Dodaje cylinder pomiędzy dwoma punktami
        /// do istniejącego MeshGeometry3D.
        ///
        /// Metoda nie tworzy osobnego GeometryModel3D.
        /// </summary>
        private void AddCylinderMesh(MeshGeometry3D mesh, Point3D p1, Point3D p2, double radius, int segments)
        {
            Vector3D direction = p2 - p1;

            double length = direction.Length;

            if (length < 0.0001)
                return;

            direction.Normalize();

            Vector3D reference;

            if (Math.Abs(direction.Y) < 0.9)
            {
                reference = new Vector3D(0, 1, 0);
            }
            else
            {
                reference = new Vector3D(1, 0, 0);
            }


            Vector3D u = Vector3D.CrossProduct(direction, reference);

            u.Normalize();

            Vector3D v = Vector3D.CrossProduct(direction, u);

            v.Normalize();

            int startIndex = mesh.Positions.Count;

            for (int i = 0; i < segments; i++)
            {
                double angle = 2.0 * Math.PI * i / segments;

                double cos = Math.Cos(angle);

                double sin = Math.Sin(angle);

                Vector3D offset = u * (radius * cos) + v * (radius * sin);

                Point3D point1 = p1 + offset;

                Point3D point2 = p2 + offset;

                mesh.Positions.Add(point1);
                mesh.Positions.Add(point2);

                mesh.Normals.Add(offset);
                mesh.Normals.Add(offset);
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;

                int a = startIndex + i * 2;

                int b = startIndex + next * 2;

                int c = startIndex + i * 2 + 1;

                int d = startIndex + next * 2 + 1;

                // Pierwszy trójkąt
                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(c);

                // Drugi trójkąt
                mesh.TriangleIndices.Add(c);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(d);
            }

            int center1 = mesh.Positions.Count;

            mesh.Positions.Add(p1);
            mesh.Normals.Add(-direction);

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;

                int current = startIndex + i * 2;

                int nextIndex = startIndex + next * 2;

                mesh.TriangleIndices.Add(center1);

                mesh.TriangleIndices.Add(nextIndex);

                mesh.TriangleIndices.Add(current);
            }


            int center2 = mesh.Positions.Count;

            mesh.Positions.Add(p2);
            mesh.Normals.Add(direction);

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;

                int current = startIndex + i * 2 + 1;

                int nextIndex = startIndex + next * 2 + 1;

                mesh.TriangleIndices.Add(center2);

                mesh.TriangleIndices.Add(current);

                mesh.TriangleIndices.Add(nextIndex);
            }
        }
    }
}
