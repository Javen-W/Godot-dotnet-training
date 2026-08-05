using System;
using System.Collections.Generic;
using Godot;


namespace GodotDotnetTraining
{
    /*
    Utility class for Hexagonal tile mathematics.
    */
    public static class HexMath
    {
        // Helper to convert axial coordinates to world position.
        public static Vector3 AxialToWorld(Vector2 axial, float size)
        {
            float x = size * Mathf.Sqrt(3.0f) * (axial.X + axial.Y / 2.0f);
            float z = size * 3.0f / 2.0f * axial.Y;
            return new Vector3(x, 0.0f, z);
        }

        public static readonly Vector2I[] AxialDirections =
        {
            new(+1, 0),
            new(+1, -1),
            new(0, -1),
            new(-1, 0),
            new(-1, +1),
            new(0, +1),
        };

        // Get neighboring hexes' axial coordinates.
        public static Vector2I[] AxialNeighbors(Vector2I axial)
        {
            var neighbors = new Vector2I[6];
            for (int i = 0; i < 6; i++)
            {
                neighbors[i] = AxialNeighbor(axial, i);
            }
            return neighbors;
        }

        public static Vector2I AxialNeighbor(Vector2I axial, int direction)
        {
            return axial + AxialDirections[direction];
        }

        public static Vector2I AxialScale(Vector2I axial, int factor)
        {
            return new Vector2I(axial.X * factor, axial.Y * factor);
        }

        public static Vector2I[] AxialRing(Vector2I axial, int radius)
        {
            var ring = new List<Vector2I>();
            var current = axial + AxialScale(AxialDirections[4], radius);
            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < radius; j++)
                {
                    ring.Add(current);
                    current = AxialNeighbor(current, i);
                }
            }
            return ring.ToArray();
        }

        public static Vector2I[] AxialSpiral(Vector2I axial, int radius)
        {
            var spiral = new List<Vector2I> { axial };
            for (var k = 1; k <= radius; k++)
            {
                spiral.AddRange(AxialRing(axial, k));
            }
            return spiral.ToArray();
        }

        public static int AxialDistance(Vector2I a, Vector2I b)
        {
            var c = a - b;
            return (Math.Abs(c.X) + Math.Abs(c.X + c.Y) + Math.Abs(c.Y)) / 2;
        }

        public static int AxialToSpiralIndex(Vector2I axial)
        {
            if (axial == Vector2I.Zero) 
                return 0;
            var radius = AxialDistance(axial, Vector2I.Zero);
            var ring = AxialRing(Vector2I.Zero, radius);
            for (var i = 0; i < ring.Length; i++)
            {
                if (ring[i] == axial) 
                    return i + (1 + 3 * radius * (radius - 1));
            }
            return -1;
        }
    }
}