using System;
using System.Collections.Generic;
using Godot;

namespace GodotDotnetTraining
{
    public partial class HexTile : Node3D
    {
        [Export] public float Size { get; set; } // Outer-circle radius.

        [Export] public Vector2I AxialCoord { get; set; } // Axial coordinates (q, r) for hex grid.

        [Export]
        public HexTileBaseType BaseType
        {
            get => _baseType;
            set
            {
                _baseType = value;
                InstantiateBaseStructureNode(value);
            }
        }

        public Vector3 Centroid => Position; // Use Node3D's Position property.
        public float Height => 2.0f * Size;
        public float Width => Mathf.Sqrt(3.0f) * Size;
        public IReadOnlyList<Vector3> Vertices { get; private set; } // Cached vertices.

        private MeshInstance3D _surfaceOverlayMesh;
        private MeshInstance3D _surfaceBorderMesh;
        private HexTileBaseType _baseType;
        private Node3D _baseStruct;

        // Called when the node enters the scene tree
        public override void _Ready()
        {
            // Initialize geometry.
            Vertices = InitializeVertices();
            
            // Initialize surface overlay.
            InitializeSurfaceOverlayMeshes();
            AddChild(_surfaceOverlayMesh);
            AddChild(_surfaceBorderMesh);
            
            // Initialize world position.
            UpdatePosition();
        }

        // Update position based on axial coordinates
        private void UpdatePosition()
        {
            Position = HexMath.AxialToWorld(AxialCoord, Size);
        }

        // Load and instantiate the appropriate base structure scene.
        public void InstantiateBaseStructureNode(HexTileBaseType baseType)
        {
            // Base structure.
            string baseScenePath = baseType switch
            {
                // TODO: Add assets and fix path to base structures.
                HexTileBaseType.Grass => "res://assets/tile_structures/base/hex_grass/HexGrass.tscn",
                HexTileBaseType.Ocean => "res://assets/tile_structures/base/hex_water/HexWater.tscn",
                HexTileBaseType.Coast => "res://assets/tile_structures/base/hex_coast/HexCoast.tscn",
                _ => throw new Exception("Unknown base structure.")
            };
            var baseStruct = GD.Load<PackedScene>(baseScenePath).Instantiate<Node3D>();
            AddChild(baseStruct);
        }

        private IReadOnlyList<Vector3> InitializeVertices()
        {
            var vertices = new List<Vector3>(6);
            for (int i = 0; i < 6; i++)
            {
                var angleDeg = 60 * i - 30; // Pointy-top hex orientation.
                var angleRad = Mathf.DegToRad(angleDeg);
                vertices.Add(new Vector3(Mathf.Cos(angleRad) * Size, 1.0f, Mathf.Sin(angleRad) * Size));
            }

            return vertices.AsReadOnly();
        }

        private void InitializeSurfaceOverlayMeshes()
        {
            // Surface mesh (transparent filled hexagon).
            _surfaceOverlayMesh = new MeshInstance3D();
            var surfaceArrayMesh = new ArrayMesh();
            var surfaceArrays = new Godot.Collections.Array();
            surfaceArrays.Resize((int)Mesh.ArrayType.Max);

            var surfaceVertices = new Vector3[Vertices.Count + 1]; // Center + vertices.
            surfaceVertices[0] = Vector3.Zero; // Center.
            for (int i = 0; i < Vertices.Count; i++)
                surfaceVertices[i + 1] = Vertices[i];

            var surfaceIndices = new int[Vertices.Count * 3];
            for (int i = 0; i < Vertices.Count; i++)
            {
                surfaceIndices[i * 3] = 0; // Center.
                surfaceIndices[i * 3 + 1] = i + 1; // Current vertex.
                surfaceIndices[i * 3 + 2] = (i + 1) % Vertices.Count + 1; // Next vertex.
            }

            surfaceArrays[(int)Mesh.ArrayType.Vertex] = surfaceVertices;
            surfaceArrays[(int)Mesh.ArrayType.Index] = surfaceIndices;
            surfaceArrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArrays);
            _surfaceOverlayMesh.Mesh = surfaceArrayMesh;
            _surfaceOverlayMesh.MaterialOverride = new StandardMaterial3D()
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            UpdateSurfaceOverlayDisplay();

            // Border mesh (colored outline)
            _surfaceBorderMesh = new MeshInstance3D();
            var borderArrayMesh = new ArrayMesh();
            var borderArrays = new Godot.Collections.Array();
            borderArrays.Resize((int)Mesh.ArrayType.Max);

            var borderVertices = new Vector3[Vertices.Count];
            for (int i = 0; i < Vertices.Count; i++)
                borderVertices[i] = Vertices[i];

            var borderIndices = new int[Vertices.Count * 2];
            for (int i = 0; i < Vertices.Count; i++)
            {
                borderIndices[i * 2] = i;
                borderIndices[i * 2 + 1] = (i + 1) % Vertices.Count;
            }

            borderArrays[(int)Mesh.ArrayType.Vertex] = borderVertices;
            borderArrays[(int)Mesh.ArrayType.Index] = borderIndices;
            borderArrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, borderArrays);
            _surfaceBorderMesh.Mesh = borderArrayMesh;
            _surfaceBorderMesh.MaterialOverride = new StandardMaterial3D()
            {
                AlbedoColor = Colors.Blue, // Solid blue border.
            };
        }

        // Updates the surface overlay color accordingly to the current display mode.
        private void UpdateSurfaceOverlayDisplay()
        {
            if (_surfaceOverlayMesh != null)
            {
                // Update color.
                ((StandardMaterial3D)_surfaceOverlayMesh.MaterialOverride).AlbedoColor = new Color(0f, 0f, 0f, 0f);
            }
        }
    }
}